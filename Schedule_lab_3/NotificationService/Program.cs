using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedModels;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
    .AddSource("NotificationService")
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("NotificationService"))
    .AddConsoleExporter()
    .Build();

const string NotificationsFile = "schedule_notifications.txt";

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║   Schedule Notification Service (Consumer)                ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝\n");

try
{
    var factory = new ConnectionFactory { HostName = RabbitMqSettings.HostName };
    await using var connection = await factory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();

    await channel.ExchangeDeclareAsync(
        exchange: RabbitMqSettings.ExchangeName,
        type: ExchangeType.Topic,
        durable: true);

    await channel.QueueDeclareAsync(
        queue: RabbitMqSettings.QueueName,
        durable: false,
        exclusive: false,
        autoDelete: false);

    await channel.QueueBindAsync(
        queue: RabbitMqSettings.QueueName,
        exchange: RabbitMqSettings.ExchangeName,
        routingKey: "schedule.*");

    Console.WriteLine($"✓ Connected to RabbitMQ");
    Console.WriteLine($"✓ Queue '{RabbitMqSettings.QueueName}' bound to pattern 'schedule.*'\n");
    Console.WriteLine($"Notifications will be saved to: {Path.GetFullPath(NotificationsFile)}\n");

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += async (model, ea) =>
    {
        try
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var routingKey = ea.RoutingKey;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received: {routingKey}");

            string notificationText = routingKey switch
            {
                var key when key == RabbitMqSettings.RoutingKeyOptimized =>
                    await ProcessOptimizationEvent(json),
                var key when key == RabbitMqSettings.RoutingKeyUpdated =>
                    await ProcessUpdateEvent(json),
                var key when key == RabbitMqSettings.RoutingKeyConflict =>
                    await ProcessConflictEvent(json),
                _ => $"Unknown routing key: {routingKey}"
            };

            await SaveNotification(notificationText);
        }
        catch (JsonException jsonEx)
        {
            Console.WriteLine($"[ERROR] Failed to deserialize: {jsonEx.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Processing error: {ex.Message}");
        }
        await Task.Yield();
    };

    await channel.BasicConsumeAsync(
        queue: RabbitMqSettings.QueueName,
        autoAck: true,
        consumer: consumer);

    Console.WriteLine("Press [Enter] to exit.\n");
    Console.ReadLine();
}
catch (Exception ex)
{
    Console.WriteLine($"[FATAL] Service failed: {ex.Message}");
}
finally
{
    tracerProvider?.Dispose();
}

static async Task<string> ProcessOptimizationEvent(string json)
{
    var evt = JsonSerializer.Deserialize<ScheduleOptimizedEvent>(json);
    if (evt == null) return "Invalid optimization event";

    var notification = new StringBuilder();
    notification.AppendLine($"╔═══════════════════════════════════════════════════════════╗");
    notification.AppendLine($"║ SCHEDULE OPTIMIZATION NOTIFICATION                        ║");
    notification.AppendLine($"╠═══════════════════════════════════════════════════════════╣");
    notification.AppendLine($" Schedule ID: {evt.ScheduleId}");
    notification.AppendLine($" Schedule Name: {evt.ScheduleName}");
    notification.AppendLine($" Status: {evt.Status}");
    notification.AppendLine($" Windows Reduced: {evt.WindowsReduced}");
    notification.AppendLine($" Load Balance Improvement: {evt.LoadBalanceImprovement}%");
    notification.AppendLine($" Conflicts Resolved: {evt.ConflictsResolved}");
    notification.AppendLine($" Optimized At: {evt.OptimizedAt:yyyy-MM-dd HH:mm:ss}");
    notification.AppendLine($" Message: {evt.Message}");
    notification.AppendLine($"╚═══════════════════════════════════════════════════════════╝");

    var text = notification.ToString();
    Console.WriteLine(text);
    return text;
}

static async Task<string> ProcessUpdateEvent(string json)
{
    var evt = JsonSerializer.Deserialize<ScheduleUpdatedEvent>(json);
    if (evt == null) return "Invalid update event";

    var notification = new StringBuilder();
    notification.AppendLine($"╔═══════════════════════════════════════════════════════════╗");
    notification.AppendLine($"║ SCHEDULE UPDATE NOTIFICATION                              ║");
    notification.AppendLine($"╠═══════════════════════════════════════════════════════════╣");
    notification.AppendLine($" Schedule ID: {evt.ScheduleId}");
    notification.AppendLine($" Updated By: {evt.UpdatedBy}");
    notification.AppendLine($" Change Type: {evt.ChangeType}");
    notification.AppendLine($" Details: {evt.Details}");
    notification.AppendLine($" Updated At: {evt.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
    notification.AppendLine($"╚═══════════════════════════════════════════════════════════╝");

    var text = notification.ToString();
    Console.WriteLine(text);
    return text;
}

static async Task<string> ProcessConflictEvent(string json)
{
    var evt = JsonSerializer.Deserialize<ConflictDetectedEvent>(json);
    if (evt == null) return "Invalid conflict event";

    var notification = new StringBuilder();
    notification.AppendLine($"╔═══════════════════════════════════════════════════════════╗");
    notification.AppendLine($"║ SCHEDULE CONFLICT DETECTED                                ║");
    notification.AppendLine($"╠═══════════════════════════════════════════════════════════╣");
    notification.AppendLine($" Schedule ID: {evt.ScheduleId}");
    notification.AppendLine($" Conflict Type: {evt.ConflictType}");
    notification.AppendLine($" Affected Entities:");
    foreach (var entity in evt.AffectedEntities)
    {
        notification.AppendLine($"   - {entity}");
    }
    notification.AppendLine($" Description: {evt.Description}");
    notification.AppendLine($" Detected At: {evt.DetectedAt:yyyy-MM-dd HH:mm:ss}");
    notification.AppendLine($"╚═══════════════════════════════════════════════════════════╝");

    var text = notification.ToString();
    Console.WriteLine(text);
    return text;
}

static async Task SaveNotification(string notification)
{
    try
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{notification}\n";
        await File.AppendAllTextAsync(NotificationsFile, entry);
        Console.WriteLine($"✓ Notification saved\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to save: {ex.Message}");
    }
}