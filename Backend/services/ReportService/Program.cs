using ReportService.Services;
using ReportService.Data;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using ReportService.Events;
using ActivityService.Services.Events;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to support both gRPC (HTTP/2) and REST (HTTP/1.1) on different ports
builder.WebHost.ConfigureKestrel(options =>
{
    // gRPC endpoint on port 5013 with HTTP/2
    options.ListenAnyIP(5013, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
    
    // REST endpoint on port 5014 with HTTP/1.1
    options.ListenAnyIP(5014, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddControllers();

// Configure Entity Framework
builder.Services.AddDbContext<ReportDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOptions<MassTransitHostOptions>().Configure(options =>
{
    options.WaitUntilStarted = true;
    options.StartTimeout = TimeSpan.FromSeconds(30);
    options.StopTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ActivityCreatedReportProjectionConsumer>();
    x.AddConsumer<AnomalyDetectedReportProjectionConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var host = builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq";
        var user = builder.Configuration["RabbitMQ:User"] ?? "guest";
        var password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        var vhost = builder.Configuration["RabbitMQ:VHost"] ?? "/";
        var prefetchCount = ushort.TryParse(builder.Configuration["RabbitMQ:PrefetchCount"], out var parsedPrefetch) ? parsedPrefetch : (ushort)16;
        var retryLimit = int.TryParse(builder.Configuration["RabbitMQ:RetryLimit"], out var parsedRetryLimit) ? Math.Max(parsedRetryLimit, 1) : 5;

        cfg.Host(host, vhost, h =>
        {
            h.Username(user);
            h.Password(password);
        });

        cfg.PrefetchCount = prefetchCount;
        cfg.Message<ActivityCreatedEvent>(m => m.SetEntityName("activity.created"));
        cfg.Message<AnomalyDetectedEvent>(m => m.SetEntityName("activity.anomaly-detected"));

        cfg.ReceiveEndpoint("reports.activity-created", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = prefetchCount;
            e.UseMessageRetry(r => r.Exponential(retryLimit, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2)));
            e.UseInMemoryOutbox(context);
            e.ConfigureConsumer<ActivityCreatedReportProjectionConsumer>(context);
        });

        cfg.ReceiveEndpoint("reports.anomaly-detected", e =>
        {
            e.Durable = true;
            e.AutoDelete = false;
            e.PrefetchCount = prefetchCount;
            e.UseMessageRetry(r => r.Exponential(retryLimit, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2)));
            e.UseInMemoryOutbox(context);
            e.ConfigureConsumer<AnomalyDetectedReportProjectionConsumer>(context);
        });
    });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE daily_reports ADD COLUMN IF NOT EXISTS anomaly_count BIGINT NOT NULL DEFAULT 0;
        ALTER TABLE daily_reports ADD COLUMN IF NOT EXISTS risk_score_samples INTEGER NOT NULL DEFAULT 0;

        CREATE UNIQUE INDEX IF NOT EXISTS uq_daily_reports_report_date_computer_id
            ON daily_reports(report_date, computer_id);

        CREATE TABLE IF NOT EXISTS processed_event_inbox (
            id BIGSERIAL PRIMARY KEY,
            consumer VARCHAR(128) NOT NULL,
            event_key VARCHAR(256) NOT NULL,
            message_id VARCHAR(128),
            processed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS uq_report_processed_event_inbox_consumer_event_key
            ON processed_event_inbox(consumer, event_key);

        CREATE TABLE IF NOT EXISTS report_daily_anomaly_rollups (
            id BIGSERIAL PRIMARY KEY,
            bucket_date DATE NOT NULL,
            computer_id INTEGER NOT NULL,
            anomaly_type VARCHAR(100) NOT NULL,
            total_count BIGINT NOT NULL DEFAULT 0,
            last_event_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS uq_report_daily_anomaly_rollups_bucket_computer_type
            ON report_daily_anomaly_rollups(bucket_date, computer_id, anomaly_type);
    ");
}

// Configure the HTTP request pipeline.
app.UseCors("AllowAll");

app.MapGrpcService<ReportServiceImpl>();
app.MapControllers();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ReportService", timestamp = DateTime.UtcNow }));

app.Run();
