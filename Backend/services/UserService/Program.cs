using UserService.Services;
using UserService.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to support both gRPC (HTTP/2) and REST (HTTP/1.1) on different ports
builder.WebHost.ConfigureKestrel(options =>
{
    // gRPC endpoint on port 5004 with HTTP/2
    options.ListenAnyIP(5004, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
    
    // REST endpoint on port 5005 with HTTP/1.1
    options.ListenAnyIP(5005, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddControllers();

// Configure Entity Framework
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var telemetryServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "user-service";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(telemetryServiceName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    dbContext.Database.Migrate();
    dbContext.Database.ExecuteSqlRaw(@"
        ALTER TABLE IF EXISTS computers
            ALTER COLUMN user_id DROP NOT NULL;

        ALTER TABLE IF EXISTS computers
            DROP CONSTRAINT IF EXISTS computers_user_id_fkey;
        ALTER TABLE IF EXISTS computers
            ADD CONSTRAINT computers_user_id_fkey
            FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL;

        DROP INDEX IF EXISTS uq_computers_user_id;
        ALTER TABLE IF EXISTS computers
            DROP CONSTRAINT IF EXISTS computers_user_id_key;

        CREATE INDEX IF NOT EXISTS idx_computers_user_id
            ON computers(user_id);

        CREATE TABLE IF NOT EXISTS computer_sessions (
            id BIGSERIAL PRIMARY KEY,
            user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            auth_user_id INTEGER NOT NULL,
            computer_id INTEGER NOT NULL REFERENCES computers(id) ON DELETE CASCADE,
            started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            expires_at TIMESTAMP NULL,
            ended_at TIMESTAMP NULL,
            last_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            status VARCHAR(20) NOT NULL DEFAULT 'active'
        );

        ALTER TABLE IF EXISTS computer_sessions
            ADD COLUMN IF NOT EXISTS expires_at TIMESTAMP NULL;

        UPDATE computer_sessions
        SET expires_at = COALESCE(expires_at, COALESCE(started_at, CURRENT_TIMESTAMP) + INTERVAL '1 day')
        WHERE expires_at IS NULL;

        ALTER TABLE IF EXISTS computer_sessions
            ALTER COLUMN expires_at SET NOT NULL;

        CREATE INDEX IF NOT EXISTS idx_computer_sessions_user_id ON computer_sessions(user_id);
        CREATE INDEX IF NOT EXISTS idx_computer_sessions_computer_id ON computer_sessions(computer_id);

        INSERT INTO computer_sessions (user_id, auth_user_id, computer_id, started_at, expires_at, last_seen, status)
        SELECT c.user_id,
               COALESCE(u.auth_user_id, 0),
               c.id,
               COALESCE(c.last_seen, CURRENT_TIMESTAMP),
               COALESCE(c.last_seen, CURRENT_TIMESTAMP) + INTERVAL '1 day',
               COALESCE(c.last_seen, CURRENT_TIMESTAMP),
               'active'
        FROM computers c
        JOIN users u ON u.id = c.user_id
        WHERE c.user_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1
              FROM computer_sessions s
              WHERE s.computer_id = c.id
                AND s.ended_at IS NULL
          )
          AND NOT EXISTS (
              SELECT 1
              FROM computer_sessions s
              WHERE s.user_id = c.user_id
                AND s.ended_at IS NULL
          );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_computer_sessions_active_user
            ON computer_sessions(user_id) WHERE ended_at IS NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS uq_computer_sessions_active_computer
            ON computer_sessions(computer_id) WHERE ended_at IS NULL;
    ");
}

// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();
app.MapGrpcService<UserServiceImpl>();
app.MapControllers();
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "UserService", timestamp = DateTime.UtcNow }));

app.Run();
