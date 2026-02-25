using NotificationService.Services;
using NotificationService.Data;
using NotificationService.Events;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using ActivityService.Services.Events;

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

builder.Services.AddOptions<MassTransitHostOptions>().Configure(options =>
{
    options.WaitUntilStarted = true;
    options.StartTimeout = TimeSpan.FromSeconds(30);
    options.StopTimeout = TimeSpan.FromSeconds(30);
});

// MassTransit + RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ActivityCreatedEventHandler>();
    x.AddConsumer<AnomalyDetectedEventHandler>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
        var user = builder.Configuration["RabbitMQ:User"] ?? "guest";
        var password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        var vhost = builder.Configuration["RabbitMQ:VHost"] ?? "/";
        var prefetchCount = ushort.TryParse(builder.Configuration["RabbitMQ:PrefetchCount"], out var parsedPrefetch) ? parsedPrefetch : (ushort)16;
        var retryLimit = int.TryParse(builder.Configuration["RabbitMQ:RetryLimit"], out var parsedRetryLimit) ? Math.Max(parsedRetryLimit, 1) : 5;

        cfg.Host(host, vhost, h => {
            h.Username(user);
            h.Password(password);
        });

        cfg.PrefetchCount = prefetchCount;

        // Match publisher topology explicitly to avoid namespace drift breaking subscriptions.
        cfg.Message<ActivityCreatedEvent>(x => x.SetEntityName("activity.created"));
        cfg.Message<AnomalyDetectedEvent>(x => x.SetEntityName("activity.anomaly-detected"));

        cfg.ReceiveEndpoint("notifications.activity-created", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = prefetchCount;
            e.UseMessageRetry(r => r.Exponential(retryLimit, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2)));
            e.UseInMemoryOutbox(context);
            e.ConfigureConsumer<ActivityCreatedEventHandler>(context);
        });

        cfg.ReceiveEndpoint("notifications.anomaly-detected", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = prefetchCount;
            e.UseMessageRetry(r => r.Exponential(retryLimit, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2)));
            e.UseInMemoryOutbox(context);
            e.ConfigureConsumer<AnomalyDetectedEventHandler>(context);
        });
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
    dbContext.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS processed_event_inbox (
            id BIGSERIAL PRIMARY KEY,
            consumer VARCHAR(128) NOT NULL,
            event_key VARCHAR(256) NOT NULL,
            message_id VARCHAR(128),
            processed_at TIMESTAMP NOT NULL DEFAULT NOW()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_processed_event_inbox_consumer_event_key
            ON processed_event_inbox(consumer, event_key);

        CREATE INDEX IF NOT EXISTS idx_processed_event_inbox_processed_at
            ON processed_event_inbox(processed_at);
    ");
}

app.UseCors("AllowAll");

app.MapGrpcService<GreeterService>();
app.MapGrpcService<NotificationServiceImpl>();
app.MapControllers();
app.MapGet("/", () => "gRPC NotificationService");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "NotificationService", timestamp = DateTime.UtcNow }));

app.Run();
