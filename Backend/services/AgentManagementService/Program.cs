using AgentManagementService.Services;
using AgentManagementService.Data;
using Microsoft.EntityFrameworkCore;
using UserLookupClient = AgentManagementService.UserLookup.UserService.UserServiceClient;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to support both gRPC (HTTP/2) and REST (HTTP/1.1) on different ports
builder.WebHost.ConfigureKestrel(options =>
{
    // gRPC endpoint on port 5015 with HTTP/2
    options.ListenAnyIP(5015, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
    
    // REST endpoint on port 5016 with HTTP/1.1
    options.ListenAnyIP(5016, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddControllers();
builder.Services.AddSingleton<ControlPlaneSigningService>();
builder.Services.Configure<CommandDeliveryOptions>(builder.Configuration.GetSection("CommandDelivery"));
builder.Services.AddHostedService<AgentCommandRetryWorker>();
builder.Services.AddGrpcClient<UserLookupClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:User"] ?? "http://localhost:5004"));

// Configure Entity Framework
builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// Configure the HTTP request pipeline.
app.UseCors("AllowAll");

// Lightweight schema bootstrap for control-plane tables (works without EF migrations).
await InitializeDatabaseWithRetryAsync(
    app.Services,
    app.Logger,
    async (services, cancellationToken) =>
    {
        var db = services.GetRequiredService<AgentDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS agent_policies (
                id SERIAL PRIMARY KEY,
                agent_id INTEGER NOT NULL UNIQUE REFERENCES agents(id) ON DELETE CASCADE,
                computer_id INTEGER NOT NULL,
                policy_version VARCHAR(50) NOT NULL DEFAULT '1',
                collection_interval_sec INTEGER NOT NULL DEFAULT 5,
                heartbeat_interval_sec INTEGER NOT NULL DEFAULT 15,
                flush_interval_sec INTEGER NOT NULL DEFAULT 5,
                enable_process_collection BOOLEAN NOT NULL DEFAULT TRUE,
                enable_browser_collection BOOLEAN NOT NULL DEFAULT TRUE,
                enable_active_window_collection BOOLEAN NOT NULL DEFAULT TRUE,
                enable_idle_collection BOOLEAN NOT NULL DEFAULT TRUE,
                idle_threshold_sec INTEGER NOT NULL DEFAULT 120,
                browser_poll_interval_sec INTEGER NOT NULL DEFAULT 10,
                process_snapshot_limit INTEGER NOT NULL DEFAULT 50,
                high_risk_threshold REAL NOT NULL DEFAULT 85,
                auto_lock_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                admin_blocked BOOLEAN NOT NULL DEFAULT FALSE,
                blocked_reason VARCHAR(500) NULL,
                browsers_json TEXT NOT NULL DEFAULT '["chrome","edge","firefox"]',
                enable_whitelist BOOLEAN NOT NULL DEFAULT TRUE,
                enable_blacklist BOOLEAN NOT NULL DEFAULT TRUE,
                whitelist_json TEXT NOT NULL DEFAULT '[]',
                blacklist_json TEXT NOT NULL DEFAULT '[]',
                updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            ALTER TABLE IF EXISTS agent_policies ADD COLUMN IF NOT EXISTS enable_whitelist BOOLEAN NOT NULL DEFAULT TRUE;
            ALTER TABLE IF EXISTS agent_policies ADD COLUMN IF NOT EXISTS enable_blacklist BOOLEAN NOT NULL DEFAULT TRUE;
            ALTER TABLE IF EXISTS agent_policies ADD COLUMN IF NOT EXISTS whitelist_json TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE IF EXISTS agent_policies ADD COLUMN IF NOT EXISTS blacklist_json TEXT NOT NULL DEFAULT '[]';
            CREATE INDEX IF NOT EXISTS idx_agent_policies_computer_id ON agent_policies(computer_id);
            CREATE TABLE IF NOT EXISTS agent_policy_versions (
                id SERIAL PRIMARY KEY,
                agent_id INTEGER NOT NULL REFERENCES agents(id) ON DELETE CASCADE,
                policy_version VARCHAR(50) NOT NULL,
                change_type VARCHAR(20) NOT NULL DEFAULT 'update',
                changed_by VARCHAR(100) NOT NULL DEFAULT 'system',
                snapshot_json TEXT NOT NULL DEFAULT '{{}}',
                created_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_agent_policy_versions_agent_id ON agent_policy_versions(agent_id);
            CREATE INDEX IF NOT EXISTS idx_agent_policy_versions_agent_created_at ON agent_policy_versions(agent_id, created_at);
            CREATE TABLE IF NOT EXISTS agent_commands (
                id SERIAL PRIMARY KEY,
                agent_id INTEGER NOT NULL REFERENCES agents(id) ON DELETE CASCADE,
                command_key VARCHAR(100) NOT NULL,
                type VARCHAR(50) NOT NULL,
                payload_json TEXT NOT NULL DEFAULT '{{}}',
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                requested_by VARCHAR(100) NOT NULL DEFAULT 'system',
                result_message VARCHAR(500) NOT NULL DEFAULT '',
                delivery_attempts INTEGER NOT NULL DEFAULT 0,
                max_delivery_attempts INTEGER NOT NULL DEFAULT 5,
                last_dispatch_at TIMESTAMP NULL,
                next_retry_at TIMESTAMP NULL,
                timeout_at TIMESTAMP NULL,
                dead_letter_reason VARCHAR(500) NOT NULL DEFAULT '',
                created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                acknowledged_at TIMESTAMP NULL
            );
            ALTER TABLE IF EXISTS agent_commands ADD COLUMN IF NOT EXISTS command_key VARCHAR(100);
            UPDATE agent_commands
               SET command_key = CONCAT('legacy-', id)
             WHERE command_key IS NULL OR command_key = '';
            ALTER TABLE IF EXISTS agent_commands ALTER COLUMN command_key SET NOT NULL;
            ALTER TABLE IF EXISTS agent_commands ADD COLUMN IF NOT EXISTS delivery_attempts INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE IF EXISTS agent_commands ADD COLUMN IF NOT EXISTS max_delivery_attempts INTEGER NOT NULL DEFAULT 5;
            ALTER TABLE IF EXISTS agent_commands ADD COLUMN IF NOT EXISTS last_dispatch_at TIMESTAMP NULL;
            ALTER TABLE IF EXISTS agent_commands ADD COLUMN IF NOT EXISTS next_retry_at TIMESTAMP NULL;
            ALTER TABLE IF EXISTS agent_commands ADD COLUMN IF NOT EXISTS timeout_at TIMESTAMP NULL;
            ALTER TABLE IF EXISTS agent_commands ADD COLUMN IF NOT EXISTS dead_letter_reason VARCHAR(500) NOT NULL DEFAULT '';
            CREATE INDEX IF NOT EXISTS idx_agent_commands_agent_id ON agent_commands(agent_id);
            CREATE INDEX IF NOT EXISTS idx_agent_commands_status ON agent_commands(status);
            CREATE INDEX IF NOT EXISTS idx_agent_commands_agent_status ON agent_commands(agent_id, status);
            CREATE UNIQUE INDEX IF NOT EXISTS uq_agent_commands_agent_command_key ON agent_commands(agent_id, command_key);
            CREATE INDEX IF NOT EXISTS idx_agent_commands_timeout_at ON agent_commands(timeout_at);
            CREATE INDEX IF NOT EXISTS idx_agent_commands_next_retry_at ON agent_commands(next_retry_at);
            CREATE TABLE IF NOT EXISTS agent_command_dlq (
                id SERIAL PRIMARY KEY,
                agent_command_id INTEGER NOT NULL UNIQUE REFERENCES agent_commands(id) ON DELETE CASCADE,
                agent_id INTEGER NOT NULL,
                command_key VARCHAR(100) NOT NULL,
                type VARCHAR(50) NOT NULL,
                payload_json TEXT NOT NULL DEFAULT '{{}}',
                reason VARCHAR(500) NOT NULL DEFAULT '',
                delivery_attempts INTEGER NOT NULL DEFAULT 0,
                failed_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_agent_command_dlq_agent_id ON agent_command_dlq(agent_id);
            CREATE INDEX IF NOT EXISTS idx_agent_command_dlq_failed_at ON agent_command_dlq(failed_at);
            """, cancellationToken);
    });

app.MapGrpcService<GreeterService>();
app.MapGrpcService<AgentManagementServiceImpl>();
app.MapControllers();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "AgentManagementService", timestamp = DateTime.UtcNow }));

app.Run();

static async Task InitializeDatabaseWithRetryAsync(
    IServiceProvider rootServices,
    ILogger logger,
    Func<IServiceProvider, CancellationToken, Task> migrationStep,
    CancellationToken cancellationToken = default)
{
    const int maxAttempts = 20;
    var delay = TimeSpan.FromSeconds(2);

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var scope = rootServices.CreateScope();
            await migrationStep(scope.ServiceProvider, cancellationToken);
            logger.LogInformation("AgentManagementService database bootstrap completed on attempt {Attempt}.", attempt);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                ex,
                "AgentManagementService database bootstrap attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s.",
                attempt,
                maxAttempts,
                delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 15));
        }
    }
}
