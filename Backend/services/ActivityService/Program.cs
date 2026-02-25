using Microsoft.EntityFrameworkCore;
using MassTransit;
using Grpc.Reflection;
using ActivityService.Services.Data;
using ActivityService.Services.Events;
using ActivityService.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// gRPC (HTTP/2) на порту 5001, REST (HTTP/1.1) на порту 5002
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    options.ListenAnyIP(5002, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

builder.Services.AddControllers();

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string missing")));

builder.Services.AddGrpcReflection();

builder.Services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();
builder.Services.AddHostedService<ActivityOutboxDispatcher>();

builder.Services.AddOptions<MassTransitHostOptions>().Configure(options =>
{
    options.WaitUntilStarted = true;
    options.StartTimeout = TimeSpan.FromSeconds(30);
    options.StopTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
        var user = builder.Configuration["RabbitMQ:User"] ?? "guest";
        var password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        var vhost = builder.Configuration["RabbitMQ:VHost"] ?? "/";

        cfg.Host(host, vhost, h =>
        {
            h.Username(user);
            h.Password(password);
        });

        // Use stable exchange names so publisher/consumer topology does not depend on project namespace.
        cfg.Message<ActivityCreatedEvent>(x => x.SetEntityName("activity.created"));
        cfg.Message<AnomalyDetectedEvent>(x => x.SetEntityName("activity.anomaly-detected"));
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS activity_outbox (
            id BIGSERIAL PRIMARY KEY,
            event_type VARCHAR(128) NOT NULL,
            activity_id BIGINT NULL,
            payload JSONB NOT NULL,
            headers JSONB NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0,
            available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            processed_at TIMESTAMPTZ NULL,
            last_error TEXT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_activity_outbox_pending
            ON activity_outbox(processed_at, available_at);
        CREATE INDEX IF NOT EXISTS idx_activity_outbox_activity_id
            ON activity_outbox(activity_id);
    ");
}

if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();

app.MapGrpcService<ActivityServiceImpl>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ActivityService", timestamp = DateTime.UtcNow }));

app.Run();
