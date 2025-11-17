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
builder.Services.AddSingleton<IOptimizationEngine, SimpleOptimizationEngine>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
    public async Task<OptimizationResult> OptimizeAsync(OptimizationRequest request)
    {
        await Task.Delay(3000);

        var random = new Random();

        var result = new OptimizationResult
        {
            ScheduleId = request.ScheduleId,
            Success = true,
            WindowsReduced = random.Next(5, 25),
            LoadBalanceImprovement = Math.Round(random.NextDouble() * 30 + 10, 2),
            ConflictsResolved = random.Next(0, 15),
            CompletedAt = DateTime.Now,
            Message = "Schedule successfully optimized"
        };

        return result;
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