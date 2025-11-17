using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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
            .AddSource("AnalyticsService")
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("AnalyticsService"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

builder.Services.AddSingleton<IAnalyticsRepository, InMemoryAnalyticsRepository>();
builder.Services.AddHostedService<EventConsumerService>();

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

app.MapGet("/api/analytics/stats", (IAnalyticsRepository repo) =>
{
    return Results.Ok(repo.GetStatistics());
})
.WithName("GetStatistics")
.WithOpenApi();

app.MapGet("/api/analytics/schedule/{scheduleId}", (int scheduleId, IAnalyticsRepository repo) =>
{
    var metrics = repo.GetScheduleMetrics(scheduleId);
    return metrics != null ? Results.Ok(metrics) : Results.NotFound();
})
.WithName("GetScheduleMetrics")
.WithOpenApi();

app.MapGet("/api/analytics/events", (IAnalyticsRepository repo) =>
{
    return Results.Ok(repo.GetRecentEvents());
})
.WithName("GetRecentEvents")
.WithOpenApi();

app.Run();

public interface IAnalyticsRepository
{
    SystemStatistics GetStatistics();
    ScheduleMetrics? GetScheduleMetrics(int scheduleId);
    List<AnalyticsEvent> GetRecentEvents();
    void RecordEvent(AnalyticsEvent analyticsEvent);
    void UpdateScheduleMetrics(int scheduleId, ScheduleMetrics metrics);
}

public class InMemoryAnalyticsRepository : IAnalyticsRepository
{
    private readonly List<AnalyticsEvent> _events = new();
    private readonly Dictionary<int, ScheduleMetrics> _scheduleMetrics = new();
    private readonly HashSet<int> _activeScheduleIds = new();
    private int _totalOptimizations = 0;
    private int _totalConflicts = 0;
    private int _totalUpdates = 0;

    public SystemStatistics GetStatistics()
    {
        return new SystemStatistics
        {
            TotalSchedules = _activeScheduleIds.Count,
            TotalOptimizations = _totalOptimizations,
            TotalConflictsDetected = _totalConflicts,
            TotalUpdates = _totalUpdates,
            AverageOptimizationTime = 3.2,
            LastUpdated = DateTime.Now
        };
    }

    public ScheduleMetrics? GetScheduleMetrics(int scheduleId)
    {
        return _scheduleMetrics.TryGetValue(scheduleId, out var metrics) ? metrics : null;
    }

    public List<AnalyticsEvent> GetRecentEvents()
    {
        return _events.OrderByDescending(e => e.Timestamp).Take(50).ToList();
    }

    public void RecordEvent(AnalyticsEvent analyticsEvent)
    {
        _events.Add(analyticsEvent);

        var eventType = analyticsEvent.RoutingKey.Split('.').LastOrDefault()?.ToLower();
        var changeType = ExtractChangeType(analyticsEvent.Payload);
        var scheduleId = ExtractScheduleId(analyticsEvent.Payload);

        Console.WriteLine($"[Analytics] Processing event - Type: {eventType}, ChangeType: {changeType}, ScheduleId: {scheduleId}");
        Console.WriteLine($"[Analytics] Current stats - Schedules: {_activeScheduleIds.Count}, Optimizations: {_totalOptimizations}, Conflicts: {_totalConflicts}, Updates: {_totalUpdates}");

        switch (eventType)
        {
            case "optimized":
                _totalOptimizations++;
                Console.WriteLine($"[Analytics] Optimization recorded. Total: {_totalOptimizations}");
                break;
            case "conflict":
                _totalConflicts++;
                Console.WriteLine($"[Analytics] Conflict recorded. Total: {_totalConflicts}");
                break;
            case "updated":
                _totalUpdates++;
                Console.WriteLine($"[Analytics] Update recorded. Total: {_totalUpdates}");

                // Handle schedule creation and deletion
                if (changeType == "Created" && scheduleId > 0)
                {
                    _activeScheduleIds.Add(scheduleId);
                    Console.WriteLine($"[Analytics] Schedule #{scheduleId} created. Active schedules: {_activeScheduleIds.Count}");
                    if (!_scheduleMetrics.ContainsKey(scheduleId))
                    {
                        _scheduleMetrics[scheduleId] = new ScheduleMetrics
                        {
                            ScheduleId = scheduleId,
                            LastCalculated = DateTime.Now
                        };
                    }
                }
                else if (changeType == "Deleted" && scheduleId > 0)
                {
                    _activeScheduleIds.Remove(scheduleId);
                    Console.WriteLine($"[Analytics] Schedule #{scheduleId} deleted. Active schedules: {_activeScheduleIds.Count}");
                }
                break;
            default:
                Console.WriteLine($"[Analytics] Unknown event type: {eventType}");
                break;
        }
    }

    private int ExtractScheduleId(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ScheduleId", out var prop))
            {
                return prop.GetInt32();
            }
        }
        catch { }
        return 0;
    }

    private string ExtractChangeType(string json)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ChangeType", out var prop))
            {
                return prop.GetString() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }

    public void UpdateScheduleMetrics(int scheduleId, ScheduleMetrics metrics)
    {
        _scheduleMetrics[scheduleId] = metrics;
    }
}

public class EventConsumerService : BackgroundService
{
    private readonly IAnalyticsRepository _repository;
    private IConnection? _connection;
    private IChannel? _channel;

    public EventConsumerService(IAnalyticsRepository repository)
    {
        _repository = repository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Analytics Event Consumer starting...");

        try
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

            // Retry logic for RabbitMQ connection
            const int maxRetries = 10;
            const int retryDelayMs = 2000;

            for (int i = 1; i <= maxRetries; i++)
            {
                try
                {
                    Console.WriteLine($"Attempting to connect to RabbitMQ... ({i}/{maxRetries})");
                    _connection = await factory.CreateConnectionAsync(stoppingToken);
                    _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
                    Console.WriteLine("Successfully connected to RabbitMQ");
                    break;
                }
                catch (Exception ex) when (i < maxRetries)
                {
                    Console.WriteLine($"Connection failed (attempt {i}/{maxRetries}): {ex.Message}");
                    await Task.Delay(retryDelayMs, stoppingToken);
                }
            }

            await _channel.ExchangeDeclareAsync(
                exchange: RabbitMqSettings.ExchangeName,
                type: ExchangeType.Topic,
                durable: true);

            var queueName = "analytics_queue";
            await _channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,
                exclusive: false,
                autoDelete: false);

            await _channel.QueueBindAsync(
                queue: queueName,
                exchange: RabbitMqSettings.ExchangeName,
                routingKey: "schedule.updated");

            await _channel.QueueBindAsync(
                queue: queueName,
                exchange: RabbitMqSettings.ExchangeName,
                routingKey: "schedule.optimized");

            await _channel.QueueBindAsync(
                queue: queueName,
                exchange: RabbitMqSettings.ExchangeName,
                routingKey: "schedule.conflict");

            Console.WriteLine($"Analytics listening on queue '{queueName}'");

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    var routingKey = ea.RoutingKey;

                    Console.WriteLine($"[Analytics Consumer] Received event - RoutingKey: {routingKey}");
                    Console.WriteLine($"[Analytics Consumer] Payload: {json}");

                    var analyticsEvent = new AnalyticsEvent
                    {
                        Type = routingKey.Split('.').Last(),
                        RoutingKey = routingKey,
                        Payload = json,
                        Timestamp = DateTime.Now
                    };

                    _repository.RecordEvent(analyticsEvent);

                    Console.WriteLine($"[Analytics Consumer] Successfully recorded event: {routingKey}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Analytics Consumer Error] {ex.Message}");
                    Console.WriteLine($"[Analytics Consumer Error] Stack: {ex.StackTrace}");
                }
                await Task.Yield();
            };

            await _channel.BasicConsumeAsync(
                queue: queueName,
                autoAck: true,
                consumer: consumer);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Analytics service error: {ex.Message}");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _channel?.Dispose();
        _connection?.Dispose();
    }
}