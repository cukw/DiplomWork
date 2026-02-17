using ActivityService.Services.Data;
using ActivityService.Services.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// gRPC
builder.Services.AddGrpc(options => 
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Publish events (без consumer)
        cfg.Publish<ActivityCreatedEvent>();
    });
});

// EF Core + Postgres
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string missing")));

builder.Services.AddGrpcReflection();  // Для grpcurl/tools (dev only)

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("RabbitMQ") 
            ?? "rabbitmq://guest:guest@rabbitmq/activity_events");
        
        // Publisher для событий
        cfg.Publish<ActivityCreatedEvent>();
    });
});

app.Services.GetRequiredService<IMassTransitHost>();

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGrpcService<ActivityServiceImpl>();
app.MapGet("/health", () => "ActivityService OK").WithName("Health");

app.Run();
