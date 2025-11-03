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

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// After app is built
app.UseCors();

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
    private int _totalOptimizations = 0;
    private int _totalConflicts = 0;
    private int _totalUpdates = 0;

    public SystemStatistics GetStatistics()
    {
        return new SystemStatistics
        {
            TotalSchedules = _scheduleMetrics.Count,
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
        
        switch (analyticsEvent.Type)
        {
            case "Optimization":
                _totalOptimizations++;
                break;
            case "Conflict":
                _totalConflicts++;
                break;
            case "Update":
                _totalUpdates++;
                break;
        }
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
            var factory = new ConnectionFactory { HostName = RabbitMqSettings.HostName };
            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

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
                routingKey: "schedule.*");

            Console.WriteLine($"Analytics listening on queue '{queueName}'");

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    var routingKey = ea.RoutingKey;

                    var analyticsEvent = new AnalyticsEvent
                    {
                        Type = routingKey.Split('.').Last(),
                        RoutingKey = routingKey,
                        Payload = json,
                        Timestamp = DateTime.Now
                    };

                    _repository.RecordEvent(analyticsEvent);
                    
                    Console.WriteLine($"[Analytics] Recorded event: {routingKey}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Analytics Error] {ex.Message}");
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