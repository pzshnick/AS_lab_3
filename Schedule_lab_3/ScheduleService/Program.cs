using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SharedModels;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddSource("ScheduleService")
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("ScheduleService"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

builder.Services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
builder.Services.AddScoped<IRabbitMqPublisher, RabbitMqPublisher>();
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/api/schedules", (IScheduleRepository repo) =>
{
    return Results.Ok(repo.GetAll());
})
.WithName("GetSchedules")
.WithOpenApi();

app.MapGet("/api/schedules/{id}", (int id, IScheduleRepository repo) =>
{
    var schedule = repo.GetById(id);
    return schedule != null ? Results.Ok(schedule) : Results.NotFound();
})
.WithName("GetScheduleById")
.WithOpenApi();

app.MapPost("/api/schedules", async (Schedule schedule, IScheduleRepository repo, IRabbitMqPublisher publisher) =>
{
    Console.WriteLine($"[VALIDATION] Checking conflicts for schedule '{schedule.Name}' with {schedule.Entries.Count} entries");

    // Validate required fields
    if (string.IsNullOrWhiteSpace(schedule.Name))
    {
        return Results.BadRequest(new { error = "Schedule name is required" });
    }

    foreach (var entry in schedule.Entries)
    {
        if (string.IsNullOrWhiteSpace(entry.Subject) ||
            string.IsNullOrWhiteSpace(entry.Teacher) ||
            string.IsNullOrWhiteSpace(entry.Group) ||
            string.IsNullOrWhiteSpace(entry.Room))
        {
            return Results.BadRequest(new { error = "All fields (Subject, Teacher, Group, Room) must be filled" });
        }

        if (entry.EndTime <= entry.StartTime)
        {
            return Results.BadRequest(new { error = "End time must be after start time" });
        }
    }

    // Validate time slot conflicts WITHIN the schedule
    var conflicts = CheckConflicts(schedule);
    Console.WriteLine($"[VALIDATION] Found {conflicts.Count} internal conflicts");

    if (conflicts.Any())
    {
        Console.WriteLine($"[VALIDATION] Rejecting schedule due to internal conflicts:");
        conflicts.ForEach(c => Console.WriteLine($"  - {c}"));

        // Publish conflict event even when schedule is rejected
        var conflictEvent = new ConflictDetectedEvent
        {
            ScheduleId = 0, // Not yet created
            ConflictType = "Internal",
            AffectedEntities = conflicts,
            Description = $"Found {conflicts.Count} internal conflicts in schedule '{schedule.Name}'",
            DetectedAt = DateTime.Now
        };
        await publisher.PublishAsync(conflictEvent, RabbitMqSettings.RoutingKeyConflict);

        return Results.BadRequest(new
        {
            error = "Schedule has conflicts",
            conflicts = conflicts
        });
    }

    // Validate conflicts with OTHER existing schedules
    var existingSchedules = repo.GetAll();
    var globalConflicts = CheckGlobalConflicts(schedule.Entries, existingSchedules);
    Console.WriteLine($"[VALIDATION] Found {globalConflicts.Count} global conflicts");

    if (globalConflicts.Any())
    {
        Console.WriteLine($"[VALIDATION] Rejecting schedule due to global conflicts:");
        globalConflicts.ForEach(c => Console.WriteLine($"  - {c}"));

        // Publish conflict event even when schedule is rejected
        var conflictEvent = new ConflictDetectedEvent
        {
            ScheduleId = 0, // Not yet created
            ConflictType = "Global",
            AffectedEntities = globalConflicts,
            Description = $"Found {globalConflicts.Count} conflicts with existing schedules for '{schedule.Name}'",
            DetectedAt = DateTime.Now
        };
        await publisher.PublishAsync(conflictEvent, RabbitMqSettings.RoutingKeyConflict);

        return Results.BadRequest(new
        {
            error = "Schedule conflicts with existing schedules",
            conflicts = globalConflicts
        });
    }

    Console.WriteLine($"[VALIDATION] No conflicts found, creating schedule");

    schedule.CreatedAt = DateTime.Now;
    schedule.Status = ScheduleStatus.Draft;
    repo.Add(schedule);

    var updateEvent = new ScheduleUpdatedEvent
    {
        ScheduleId = schedule.Id,
        UpdatedBy = "System",
        ChangeType = "Created",
        Details = $"Schedule '{schedule.Name}' created",
        UpdatedAt = DateTime.Now
    };

    await publisher.PublishAsync(updateEvent, RabbitMqSettings.RoutingKeyUpdated);

    return Results.Created($"/api/schedules/{schedule.Id}", schedule);
})
.WithName("CreateSchedule")
.WithOpenApi();

app.MapPut("/api/schedules/{id}", async (int id, Schedule schedule, IScheduleRepository repo, IRabbitMqPublisher publisher) =>
{
    var existing = repo.GetById(id);
    if (existing == null) return Results.NotFound();

    // Validate required fields
    if (string.IsNullOrWhiteSpace(schedule.Name))
    {
        return Results.BadRequest(new { error = "Schedule name is required" });
    }

    foreach (var entry in schedule.Entries)
    {
        if (string.IsNullOrWhiteSpace(entry.Subject) ||
            string.IsNullOrWhiteSpace(entry.Teacher) ||
            string.IsNullOrWhiteSpace(entry.Group) ||
            string.IsNullOrWhiteSpace(entry.Room))
        {
            return Results.BadRequest(new { error = "All fields (Subject, Teacher, Group, Room) must be filled" });
        }

        if (entry.EndTime <= entry.StartTime)
        {
            return Results.BadRequest(new { error = "End time must be after start time" });
        }
    }

    // Validate time slot conflicts WITHIN the schedule
    var conflicts = CheckConflicts(schedule);
    if (conflicts.Any())
    {
        return Results.BadRequest(new
        {
            error = "Schedule has conflicts",
            conflicts = conflicts
        });
    }

    // Validate conflicts with OTHER existing schedules (exclude current schedule being updated)
    var existingSchedules = repo.GetAll().Where(s => s.Id != id).ToList();
    var globalConflicts = CheckGlobalConflicts(schedule.Entries, existingSchedules);

    if (globalConflicts.Any())
    {
        return Results.BadRequest(new
        {
            error = "Schedule conflicts with existing schedules",
            conflicts = globalConflicts
        });
    }

    schedule.Id = id;
    repo.Update(schedule);

    var updateEvent = new ScheduleUpdatedEvent
    {
        ScheduleId = schedule.Id,
        UpdatedBy = "System",
        ChangeType = "Updated",
        Details = $"Schedule '{schedule.Name}' updated",
        UpdatedAt = DateTime.Now
    };

    await publisher.PublishAsync(updateEvent, RabbitMqSettings.RoutingKeyUpdated);

    return Results.Ok(schedule);
})
.WithName("UpdateSchedule")
.WithOpenApi();

app.MapDelete("/api/schedules/{id}", async (int id, IScheduleRepository repo, IRabbitMqPublisher publisher) =>
{
    var existing = repo.GetById(id);
    if (existing == null) return Results.NotFound();

    repo.Delete(id);

    var updateEvent = new ScheduleUpdatedEvent
    {
        ScheduleId = id,
        UpdatedBy = "System",
        ChangeType = "Deleted",
        Details = $"Schedule deleted",
        UpdatedAt = DateTime.Now
    };

    await publisher.PublishAsync(updateEvent, RabbitMqSettings.RoutingKeyUpdated);

    return Results.NoContent();
})
.WithName("DeleteSchedule")
.WithOpenApi();

app.MapPost("/api/schedules/{id}/optimize", async (int id, IScheduleRepository repo, HttpClient httpClient) =>
{
    var schedule = repo.GetById(id);
    if (schedule == null) return Results.NotFound();

    schedule.Status = ScheduleStatus.Optimizing;
    repo.Update(schedule);

    var request = new OptimizationRequest
    {
        ScheduleId = schedule.Id,
        ScheduleName = schedule.Name,
        Criteria = new OptimizationCriteria
        {
            MinimizeWindows = true,
            BalanceLoad = true,
            ResolveConflicts = true
        }
    };

    // Read from environment, default to localhost:5002 for local development
    var optimizationServiceUrl = Environment.GetEnvironmentVariable("OptimizationService__Url") ?? "http://localhost:5002";

    var response = await httpClient.PostAsJsonAsync(
        $"{optimizationServiceUrl}/api/optimization/optimize",
        request);

    if (response.IsSuccessStatusCode)
    {
        return Results.Accepted($"/api/schedules/{id}", new { message = "Optimization started" });
    }

    return Results.Problem("Failed to start optimization");
})
.WithName("OptimizeSchedule")
.WithOpenApi();

app.MapPost("/api/schedules/{id}/check-conflicts", async (int id, IScheduleRepository repo, IRabbitMqPublisher publisher) =>
{
    var schedule = repo.GetById(id);
    if (schedule == null) return Results.NotFound();

    var conflicts = CheckConflicts(schedule);

    if (conflicts.Any())
    {
        var conflictEvent = new ConflictDetectedEvent
        {
            ScheduleId = schedule.Id,
            ConflictType = "Multiple",
            AffectedEntities = conflicts,
            Description = $"Found {conflicts.Count} conflicts in schedule",
            DetectedAt = DateTime.Now
        };

        await publisher.PublishAsync(conflictEvent, RabbitMqSettings.RoutingKeyConflict);

        return Results.Ok(new { conflicts });
    }

    return Results.Ok(new { message = "No conflicts detected" });
})
.WithName("CheckConflicts")
.WithOpenApi();

app.Run();

static List<string> CheckConflicts(Schedule schedule)
{
    var conflicts = new List<string>();
    var entries = schedule.Entries.OrderBy(e => e.DayOfWeek).ThenBy(e => e.StartTime).ToList();

    for (int i = 0; i < entries.Count; i++)
    {
        for (int j = i + 1; j < entries.Count; j++)
        {
            var e1 = entries[i];
            var e2 = entries[j];

            if (e1.DayOfWeek != e2.DayOfWeek) continue;

            bool timeOverlap = e1.StartTime < e2.EndTime && e2.StartTime < e1.EndTime;

            if (!timeOverlap) continue;

            if (e1.Teacher == e2.Teacher)
            {
                conflicts.Add($"Teacher '{e1.Teacher}' has overlapping classes on {e1.DayOfWeek} at {e1.StartTime}");
            }

            if (e1.Group == e2.Group)
            {
                conflicts.Add($"Group '{e1.Group}' has overlapping classes on {e1.DayOfWeek} at {e1.StartTime}");
            }

            if (e1.Room == e2.Room)
            {
                conflicts.Add($"Room '{e1.Room}' is double-booked on {e1.DayOfWeek} at {e1.StartTime}");
            }
        }
    }

    return conflicts;
}

static List<string> CheckGlobalConflicts(List<ScheduleEntry> newEntries, List<Schedule> existingSchedules)
{
    var conflicts = new List<string>();

    foreach (var newEntry in newEntries)
    {
        foreach (var existingSchedule in existingSchedules)
        {
            foreach (var existingEntry in existingSchedule.Entries)
            {
                // Перевірка чи той самий день
                if (newEntry.DayOfWeek != existingEntry.DayOfWeek) continue;

                // Перевірка чи є накладення часу
                bool timeOverlap = newEntry.StartTime < existingEntry.EndTime &&
                                   existingEntry.StartTime < newEntry.EndTime;

                if (!timeOverlap) continue;

                // Конфлікт по викладачу
                if (!string.IsNullOrEmpty(newEntry.Teacher) &&
                    !string.IsNullOrEmpty(existingEntry.Teacher) &&
                    newEntry.Teacher.Equals(existingEntry.Teacher, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add($"Teacher '{newEntry.Teacher}' is already scheduled on {newEntry.DayOfWeek} from {existingEntry.StartTime:hh\\:mm} to {existingEntry.EndTime:hh\\:mm} in schedule '{existingSchedule.Name}'");
                }

                // Конфлікт по групі
                if (!string.IsNullOrEmpty(newEntry.Group) &&
                    !string.IsNullOrEmpty(existingEntry.Group) &&
                    newEntry.Group.Equals(existingEntry.Group, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add($"Group '{newEntry.Group}' is already scheduled on {newEntry.DayOfWeek} from {existingEntry.StartTime:hh\\:mm} to {existingEntry.EndTime:hh\\:mm} in schedule '{existingSchedule.Name}'");
                }

                // Конфлікт по аудиторії
                if (!string.IsNullOrEmpty(newEntry.Room) &&
                    !string.IsNullOrEmpty(existingEntry.Room) &&
                    newEntry.Room.Equals(existingEntry.Room, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add($"Room '{newEntry.Room}' is already booked on {newEntry.DayOfWeek} from {existingEntry.StartTime:hh\\:mm} to {existingEntry.EndTime:hh\\:mm} in schedule '{existingSchedule.Name}'");
                }
            }
        }
    }

    return conflicts;
}

public interface IScheduleRepository
{
    List<Schedule> GetAll();
    Schedule? GetById(int id);
    void Add(Schedule schedule);
    void Update(Schedule schedule);
    void Delete(int id);
}

public class InMemoryScheduleRepository : IScheduleRepository
{
    private readonly List<Schedule> _schedules = new();
    private int _nextId = 1;

    public List<Schedule> GetAll() => _schedules;

    public Schedule? GetById(int id) => _schedules.FirstOrDefault(s => s.Id == id);

    public void Add(Schedule schedule)
    {
        schedule.Id = _nextId++;
        _schedules.Add(schedule);
    }

    public void Update(Schedule schedule)
    {
        var index = _schedules.FindIndex(s => s.Id == schedule.Id);
        if (index >= 0) _schedules[index] = schedule;
    }

    public void Delete(int id)
    {
        var schedule = GetById(id);
        if (schedule != null) _schedules.Remove(schedule);
    }
}

public interface IRabbitMqPublisher
{
    Task PublishAsync<T>(T message, string routingKey);
}

public class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
{
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;
        InitializeConnection();
    }

    private void InitializeConnection()
    {
        var factory = new ConnectionFactory 
        { 
            HostName = RabbitMqSettings.HostName,
            UserName = RabbitMqSettings.UserName,
            Password = RabbitMqSettings.Password,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(30),
            SocketReadTimeout = TimeSpan.FromSeconds(30),
            SocketWriteTimeout = TimeSpan.FromSeconds(30)
        };

        const int maxRetries = 10;
        const int retryDelayMs = 2000;

        for (int i = 1; i <= maxRetries; i++)
        {
            try
            {
                _logger.LogInformation("Attempting to connect to RabbitMQ... ({Attempt}/{MaxRetries})", i, maxRetries);
                _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
                _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

                _channel.ExchangeDeclareAsync(
                    exchange: RabbitMqSettings.ExchangeName,
                    type: ExchangeType.Topic,
                    durable: true).GetAwaiter().GetResult();

                _logger.LogInformation("Successfully connected to RabbitMQ");
                return;
            }
            catch (BrokerUnreachableException ex)
            {
                if (i == maxRetries)
                {
                    _logger.LogError(ex, "Failed to connect to RabbitMQ after {MaxRetries} attempts", maxRetries);
                    throw;
                }
                _logger.LogWarning("Connection failed (attempt {Attempt}/{MaxRetries}): {Message}", i, maxRetries, ex.Message);
                Thread.Sleep(retryDelayMs);
            }
        }
    }

    public async Task PublishAsync<T>(T message, string routingKey)
    {
        if (_channel == null)
        {
            _logger.LogWarning("Channel is null, attempting to reconnect...");
            InitializeConnection();
        }

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel!.BasicPublishAsync(
            exchange: RabbitMqSettings.ExchangeName,
            routingKey: routingKey,
            body: body);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}