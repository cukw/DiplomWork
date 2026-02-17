using Microsoft.EntityFrameworkCore;
using MassTransit;
using Grpc.Reflection;
using ActivityService.Services.Data;
using ActivityService.Services.Events;
using ActivityService.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq://guest:guest@rabbitmq:5672");
        
        // Publisher для событий
        cfg.Publish<ActivityCreatedEvent>();
    });
});

var app = builder.Build();
// Middleware
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGrpcService<ActivityServiceImpl>();
app.MapGet("/health", () => "ActivityService OK").WithName("Health");

app.Run();
