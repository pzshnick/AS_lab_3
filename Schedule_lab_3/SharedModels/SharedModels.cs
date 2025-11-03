namespace SharedModels;

public class Schedule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastOptimizedAt { get; set; }
    public ScheduleStatus Status { get; set; }
    public List<ScheduleEntry> Entries { get; set; } = new();
}

public class ScheduleEntry
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public enum ScheduleStatus
{
    Draft,
    Optimizing,
    Optimized,
    Published,
    Archived
}

// Catalog entities
public class Teacher
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public int StudentsCount { get; set; }
}

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class Subject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Credits { get; set; }
    public string Description { get; set; } = string.Empty;
}

// Analytics entities
public class SystemStatistics
{
    public int TotalSchedules { get; set; }
    public int TotalOptimizations { get; set; }
    public int TotalConflictsDetected { get; set; }
    public int TotalUpdates { get; set; }
    public double AverageOptimizationTime { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ScheduleMetrics
{
    public int ScheduleId { get; set; }
    public int TotalWindows { get; set; }
    public int TotalConflicts { get; set; }
    public double AverageLoadBalance { get; set; }
    public int OptimizationCount { get; set; }
    public DateTime LastCalculated { get; set; }
}

public class AnalyticsEvent
{
    public string Type { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

// Optimization
public class OptimizationRequest
{
    public int ScheduleId { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public OptimizationCriteria Criteria { get; set; } = new();
}

public class OptimizationCriteria
{
    public bool MinimizeWindows { get; set; } = true;
    public bool BalanceLoad { get; set; } = true;
    public bool ResolveConflicts { get; set; } = true;
    public int MaxIterations { get; set; } = 1000;
}

public class OptimizationResult
{
    public int ScheduleId { get; set; }
    public bool Success { get; set; }
    public int WindowsReduced { get; set; }
    public double LoadBalanceImprovement { get; set; }
    public int ConflictsResolved { get; set; }
    public DateTime CompletedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

// Events
public class ScheduleOptimizedEvent
{
    public int ScheduleId { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public OptimizationStatus Status { get; set; }
    public int WindowsReduced { get; set; }
    public double LoadBalanceImprovement { get; set; }
    public int ConflictsResolved { get; set; }
    public DateTime OptimizedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ScheduleUpdatedEvent
{
    public int ScheduleId { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class ConflictDetectedEvent
{
    public int ScheduleId { get; set; }
    public string ConflictType { get; set; } = string.Empty;
    public List<string> AffectedEntities { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
}

public enum OptimizationStatus
{
    Started,
    InProgress,
    Completed,
    Failed
}

public static class RabbitMqSettings
{
    // For Docker: use service name
    public const string HostName = "rabbitmq";
    
    // For local development: use "localhost"
    // public const string HostName = "localhost";
    
    public const string ExchangeName = "schedule_exchange";
    public const string QueueName = "schedule_queue";
    public const string RoutingKeyOptimized = "schedule.optimized";
    public const string RoutingKeyUpdated = "schedule.updated";
    public const string RoutingKeyConflict = "schedule.conflict";
}