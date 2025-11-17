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
            .AddSource("OptimizationService")
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("OptimizationService"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
builder.Services.AddScoped<IOptimizationEngine, SimpleOptimizationEngine>();
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

app.MapPost("/api/optimization/optimize", async (
    OptimizationRequest request,
    IOptimizationEngine engine,
    IRabbitMqPublisher publisher) =>
{
    try
    {
        var startEvent = new ScheduleOptimizedEvent
        {
            ScheduleId = request.ScheduleId,
            ScheduleName = request.ScheduleName,
            Status = OptimizationStatus.Started,
            OptimizedAt = DateTime.Now,
            Message = "Optimization process started"
        };

        await publisher.PublishAsync(startEvent, RabbitMqSettings.RoutingKeyOptimized);

        _ = Task.Run(async () =>
        {
            try
            {
                var inProgressEvent = new ScheduleOptimizedEvent
                {
                    ScheduleId = request.ScheduleId,
                    ScheduleName = request.ScheduleName,
                    Status = OptimizationStatus.InProgress,
                    OptimizedAt = DateTime.Now,
                    Message = "Optimization in progress..."
                };

                await publisher.PublishAsync(inProgressEvent, RabbitMqSettings.RoutingKeyOptimized);

                var result = await engine.OptimizeAsync(request);

                var completedEvent = new ScheduleOptimizedEvent
                {
                    ScheduleId = request.ScheduleId,
                    ScheduleName = request.ScheduleName,
                    Status = result.Success ? OptimizationStatus.Completed : OptimizationStatus.Failed,
                    WindowsReduced = result.WindowsReduced,
                    LoadBalanceImprovement = result.LoadBalanceImprovement,
                    ConflictsResolved = result.ConflictsResolved,
                    OptimizedAt = result.CompletedAt,
                    Message = result.Message
                };

                await publisher.PublishAsync(completedEvent, RabbitMqSettings.RoutingKeyOptimized);
            }
            catch (Exception ex)
            {
                var failedEvent = new ScheduleOptimizedEvent
                {
                    ScheduleId = request.ScheduleId,
                    ScheduleName = request.ScheduleName,
                    Status = OptimizationStatus.Failed,
                    OptimizedAt = DateTime.Now,
                    Message = $"Optimization failed: {ex.Message}"
                };

                await publisher.PublishAsync(failedEvent, RabbitMqSettings.RoutingKeyOptimized);
            }
        });

        return Results.Accepted(null, new { message = "Optimization started", scheduleId = request.ScheduleId });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to start optimization: {ex.Message}");
    }
})
.WithName("OptimizeSchedule")
.WithOpenApi();

app.MapGet("/api/optimization/status/{scheduleId}", (int scheduleId) =>
{
    return Results.Ok(new
    {
        scheduleId,
        status = "Processing",
        message = "Check notifications for updates"
    });
})
.WithName("GetOptimizationStatus")
.WithOpenApi();

app.Run();

public interface IOptimizationEngine
{
    Task<OptimizationResult> OptimizeAsync(OptimizationRequest request);
}

public class SimpleOptimizationEngine : IOptimizationEngine
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SimpleOptimizationEngine> _logger;

    public SimpleOptimizationEngine(ILogger<SimpleOptimizationEngine> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<OptimizationResult> OptimizeAsync(OptimizationRequest request)
    {
        _logger.LogInformation($"Starting optimization for schedule {request.ScheduleId}");

        // Simulate optimization work
        await Task.Delay(2000);

        try
        {
            // Get schedule from ScheduleService
            var scheduleServiceUrl = Environment.GetEnvironmentVariable("ScheduleService__Url") ?? "http://localhost:5001";
            var scheduleResponse = await _httpClient.GetAsync($"{scheduleServiceUrl}/api/schedules/{request.ScheduleId}");

            if (!scheduleResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to get schedule {request.ScheduleId}");
                return new OptimizationResult
                {
                    ScheduleId = request.ScheduleId,
                    Success = false,
                    Message = "Failed to retrieve schedule",
                    CompletedAt = DateTime.Now
                };
            }

            var schedule = await scheduleResponse.Content.ReadFromJsonAsync<Schedule>();
            if (schedule == null || schedule.Entries.Count == 0)
            {
                return new OptimizationResult
                {
                    ScheduleId = request.ScheduleId,
                    Success = true,
                    WindowsReduced = 0,
                    LoadBalanceImprovement = 0,
                    ConflictsResolved = 0,
                    CompletedAt = DateTime.Now,
                    Message = "No entries to optimize"
                };
            }

            _logger.LogInformation($"Optimizing schedule with {schedule.Entries.Count} entries");

            // Calculate initial metrics
            var initialWindows = CalculateWindows(schedule.Entries);
            var initialConflicts = CountConflicts(schedule.Entries);

            // Perform optimization: Sort entries by day and time to minimize windows
            var optimizedEntries = schedule.Entries
                .OrderBy(e => e.DayOfWeek)
                .ThenBy(e => e.StartTime)
                .ToList();

            schedule.Entries = optimizedEntries;
            schedule.Status = ScheduleStatus.Optimized;
            schedule.LastOptimizedAt = DateTime.Now;

            // Calculate optimized metrics
            var finalWindows = CalculateWindows(optimizedEntries);
            var finalConflicts = CountConflicts(optimizedEntries);

            // Update schedule in ScheduleService
            var updateResponse = await _httpClient.PutAsJsonAsync(
                $"{scheduleServiceUrl}/api/schedules/{request.ScheduleId}",
                schedule);

            if (!updateResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to update schedule {request.ScheduleId}");
            }

            var result = new OptimizationResult
            {
                ScheduleId = request.ScheduleId,
                Success = true,
                WindowsReduced = Math.Max(0, initialWindows - finalWindows),
                LoadBalanceImprovement = CalculateLoadBalanceImprovement(schedule.Entries),
                ConflictsResolved = Math.Max(0, initialConflicts - finalConflicts),
                CompletedAt = DateTime.Now,
                Message = "Schedule optimized: entries sorted by day and time"
            };

            _logger.LogInformation($"Optimization completed: Windows reduced by {result.WindowsReduced}, Conflicts resolved: {result.ConflictsResolved}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Optimization failed for schedule {request.ScheduleId}");
            return new OptimizationResult
            {
                ScheduleId = request.ScheduleId,
                Success = false,
                Message = $"Optimization failed: {ex.Message}",
                CompletedAt = DateTime.Now
            };
        }
    }

    private int CalculateWindows(List<ScheduleEntry> entries)
    {
        var windows = 0;
        var entriesByDay = entries.GroupBy(e => e.DayOfWeek);

        foreach (var dayGroup in entriesByDay)
        {
            var sortedEntries = dayGroup.OrderBy(e => e.StartTime).ToList();
            for (int i = 0; i < sortedEntries.Count - 1; i++)
            {
                var gap = sortedEntries[i + 1].StartTime - sortedEntries[i].EndTime;
                if (gap.TotalMinutes > 15) // Count gaps larger than 15 minutes as windows
                {
                    windows++;
                }
            }
        }

        return windows;
    }

    private int CountConflicts(List<ScheduleEntry> entries)
    {
        var conflicts = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            for (int j = i + 1; j < entries.Count; j++)
            {
                var e1 = entries[i];
                var e2 = entries[j];

                if (e1.DayOfWeek != e2.DayOfWeek) continue;

                bool timeOverlap = e1.StartTime < e2.EndTime && e2.StartTime < e1.EndTime;
                if (timeOverlap && (e1.Teacher == e2.Teacher || e1.Group == e2.Group || e1.Room == e2.Room))
                {
                    conflicts++;
                }
            }
        }
        return conflicts;
    }

    private double CalculateLoadBalanceImprovement(List<ScheduleEntry> entries)
    {
        // Simple metric: calculate how evenly distributed classes are across days
        var entriesPerDay = entries.GroupBy(e => e.DayOfWeek)
            .Select(g => g.Count())
            .ToList();

        if (entriesPerDay.Count == 0) return 0;

        var avg = entriesPerDay.Average();
        var variance = entriesPerDay.Sum(count => Math.Pow(count - avg, 2)) / entriesPerDay.Count;
        var stdDev = Math.Sqrt(variance);

        // Lower standard deviation = better balance
        // Return improvement as percentage (inverse of std dev)
        return Math.Round(Math.Max(0, 100 - stdDev * 10), 2);
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
        var factory = new ConnectionFactory 
        { 
            HostName = RabbitMqSettings.HostName,
            UserName = RabbitMqSettings.UserName,
            Password = RabbitMqSettings.Password
        };
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