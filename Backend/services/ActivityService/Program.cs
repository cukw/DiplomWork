using Microsoft.EntityFrameworkCore;
using MassTransit;
using Grpc.Reflection;
using ActivityService.Services.Data;
using ActivityService.Services.Events;
using ActivityService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// gRPC
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// EF Core + Postgres
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string missing")));

builder.Services.AddGrpcReflection();  // Для grpcurl/tools (dev only)

// Register anomaly detection service
builder.Services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
        var user = builder.Configuration["RabbitMQ:User"] ?? "guest";
        var password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        
        cfg.Host($"rabbitmq://{user}:{password}@{host}:5672");
        
        // Publisher для событий
        cfg.Publish<ActivityCreatedEvent>();
        cfg.Publish<AnomalyDetectedEvent>();
    });
});

var app = builder.Build();

// Apply EF Core migrations and create database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Middleware
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGrpcService<ActivityServiceImpl>();
app.MapControllers();
app.MapGet("/health", () => "ActivityService OK").WithName("Health");

app.Run();
