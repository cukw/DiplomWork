using NotificationService.Services;
using NotificationService.Data;
using NotificationService.Events;
using Microsoft.EntityFrameworkCore;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// gRPC (HTTP/2) на порту 5012, REST (HTTP/1.1) на порту 5017
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5012, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    options.ListenAnyIP(5017, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

builder.Services.AddControllers();
builder.Services.AddGrpc();

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// MassTransit + RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ActivityCreatedEventHandler>();
    x.AddConsumer<AnomalyDetectedEventHandler>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h => {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    dbContext.Database.Migrate();
}

app.UseCors("AllowAll");

app.MapGrpcService<GreeterService>();
app.MapGrpcService<NotificationServiceImpl>();
app.MapControllers();
app.MapGet("/", () => "gRPC NotificationService");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "NotificationService", timestamp = DateTime.UtcNow }));

app.Run();
