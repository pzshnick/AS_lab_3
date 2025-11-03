using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
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
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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

    var response = await httpClient.PostAsJsonAsync("http://localhost:5002/api/optimization/optimize", request);

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
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    public RabbitMqPublisher()
    {
        var factory = new ConnectionFactory { HostName = RabbitMqSettings.HostName };
        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

        _channel.ExchangeDeclareAsync(
            exchange: RabbitMqSettings.ExchangeName,
            type: ExchangeType.Topic,
            durable: true).GetAwaiter().GetResult();
    }

    public async Task PublishAsync<T>(T message, string routingKey)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        await _channel.BasicPublishAsync(
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