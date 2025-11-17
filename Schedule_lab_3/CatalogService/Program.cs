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
            .AddSource("CatalogService")
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("CatalogService"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

builder.Services.AddSingleton<ICatalogRepository, InMemoryCatalogRepository>();
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

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

// Teachers endpoints
app.MapGet("/api/catalog/teachers", (ICatalogRepository repo) =>
{
    return Results.Ok(repo.GetAllTeachers());
})
.WithName("GetTeachers")
.WithOpenApi();

app.MapPost("/api/catalog/teachers", async (Teacher teacher, ICatalogRepository repo, IRabbitMqPublisher publisher) =>
{
    repo.AddTeacher(teacher);
    
    var updateEvent = new ScheduleUpdatedEvent
    {
        ScheduleId = 0,
        UpdatedBy = "CatalogService",
        ChangeType = "Teacher Added",
        Details = $"Teacher '{teacher.Name}' added to catalog",
        UpdatedAt = DateTime.Now
    };
    
    await publisher.PublishAsync(updateEvent, RabbitMqSettings.RoutingKeyUpdated);
    
    return Results.Created($"/api/catalog/teachers/{teacher.Id}", teacher);
})
.WithName("AddTeacher")
.WithOpenApi();

// Groups endpoints
app.MapGet("/api/catalog/groups", (ICatalogRepository repo) =>
{
    return Results.Ok(repo.GetAllGroups());
})
.WithName("GetGroups")
.WithOpenApi();

app.MapPost("/api/catalog/groups", async (Group group, ICatalogRepository repo, IRabbitMqPublisher publisher) =>
{
    repo.AddGroup(group);
    
    var updateEvent = new ScheduleUpdatedEvent
    {
        ScheduleId = 0,
        UpdatedBy = "CatalogService",
        ChangeType = "Group Added",
        Details = $"Group '{group.Name}' added to catalog",
        UpdatedAt = DateTime.Now
    };
    
    await publisher.PublishAsync(updateEvent, RabbitMqSettings.RoutingKeyUpdated);
    
    return Results.Created($"/api/catalog/groups/{group.Id}", group);
})
.WithName("AddGroup")
.WithOpenApi();

// Rooms endpoints
app.MapGet("/api/catalog/rooms", (ICatalogRepository repo) =>
{
    return Results.Ok(repo.GetAllRooms());
})
.WithName("GetRooms")
.WithOpenApi();

app.MapPost("/api/catalog/rooms", async (Room room, ICatalogRepository repo, IRabbitMqPublisher publisher) =>
{
    repo.AddRoom(room);
    
    var updateEvent = new ScheduleUpdatedEvent
    {
        ScheduleId = 0,
        UpdatedBy = "CatalogService",
        ChangeType = "Room Added",
        Details = $"Room '{room.Name}' added to catalog",
        UpdatedAt = DateTime.Now
    };
    
    await publisher.PublishAsync(updateEvent, RabbitMqSettings.RoutingKeyUpdated);
    
    return Results.Created($"/api/catalog/rooms/{room.Id}", room);
})
.WithName("AddRoom")
.WithOpenApi();

// Subjects endpoints
app.MapGet("/api/catalog/subjects", (ICatalogRepository repo) =>
{
    return Results.Ok(repo.GetAllSubjects());
})
.WithName("GetSubjects")
.WithOpenApi();

app.MapPost("/api/catalog/subjects", async (Subject subject, ICatalogRepository repo, IRabbitMqPublisher publisher) =>
{
    repo.AddSubject(subject);
    
    var updateEvent = new ScheduleUpdatedEvent
    {
        ScheduleId = 0,
        UpdatedBy = "CatalogService",
        ChangeType = "Subject Added",
        Details = $"Subject '{subject.Name}' added to catalog",
        UpdatedAt = DateTime.Now
    };
    
    await publisher.PublishAsync(updateEvent, RabbitMqSettings.RoutingKeyUpdated);
    
    return Results.Created($"/api/catalog/subjects/{subject.Id}", subject);
})
.WithName("AddSubject")
.WithOpenApi();

app.Run();

public interface ICatalogRepository
{
    List<Teacher> GetAllTeachers();
    List<Group> GetAllGroups();
    List<Room> GetAllRooms();
    List<Subject> GetAllSubjects();
    void AddTeacher(Teacher teacher);
    void AddGroup(Group group);
    void AddRoom(Room room);
    void AddSubject(Subject subject);
}

public class InMemoryCatalogRepository : ICatalogRepository
{
    private readonly List<Teacher> _teachers = new();
    private readonly List<Group> _groups = new();
    private readonly List<Room> _rooms = new();
    private readonly List<Subject> _subjects = new();
    private int _nextTeacherId = 1;
    private int _nextGroupId = 1;
    private int _nextRoomId = 1;
    private int _nextSubjectId = 1;

    public InMemoryCatalogRepository()
    {
        // Seed initial data
        _teachers.Add(new Teacher { Id = _nextTeacherId++, Name = "Dr. Lutsyk", Department = "Software Engineering" });
        _teachers.Add(new Teacher { Id = _nextTeacherId++, Name = "Prof. Kliushta", Department = "Software Engineering" });
        
        _groups.Add(new Group { Id = _nextGroupId++, Name = "PZ-46", Year = 4, StudentsCount = 25 });
        _groups.Add(new Group { Id = _nextGroupId++, Name = "PZ-45", Year = 4, StudentsCount = 28 });
        
        _rooms.Add(new Room { Id = _nextRoomId++, Name = "Room 301", Capacity = 30, Type = "Lecture Hall" });
        _rooms.Add(new Room { Id = _nextRoomId++, Name = "Room 302", Capacity = 25, Type = "Computer Lab" });
        
        _subjects.Add(new Subject { Id = _nextSubjectId++, Name = "Software Architecture", Credits = 5 });
        _subjects.Add(new Subject { Id = _nextSubjectId++, Name = "Algorithms", Credits = 6 });
    }

    public List<Teacher> GetAllTeachers() => _teachers;
    public List<Group> GetAllGroups() => _groups;
    public List<Room> GetAllRooms() => _rooms;
    public List<Subject> GetAllSubjects() => _subjects;

    public void AddTeacher(Teacher teacher)
    {
        teacher.Id = _nextTeacherId++;
        _teachers.Add(teacher);
    }

    public void AddGroup(Group group)
    {
        group.Id = _nextGroupId++;
        _groups.Add(group);
    }

    public void AddRoom(Room room)
    {
        room.Id = _nextRoomId++;
        _rooms.Add(room);
    }

    public void AddSubject(Subject subject)
    {
        subject.Id = _nextSubjectId++;
        _subjects.Add(subject);
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