using AgentManagementService.Services;
using AgentManagementService.Data;
using Microsoft.EntityFrameworkCore;

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
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
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
            updated_at TIMESTAMP NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_agent_policies_computer_id ON agent_policies(computer_id);
        CREATE TABLE IF NOT EXISTS agent_commands (
            id SERIAL PRIMARY KEY,
            agent_id INTEGER NOT NULL REFERENCES agents(id) ON DELETE CASCADE,
            type VARCHAR(50) NOT NULL,
            payload_json TEXT NOT NULL DEFAULT '{{}}',
            status VARCHAR(20) NOT NULL DEFAULT 'pending',
            requested_by VARCHAR(100) NOT NULL DEFAULT 'system',
            result_message VARCHAR(500) NOT NULL DEFAULT '',
            created_at TIMESTAMP NOT NULL DEFAULT NOW(),
            acknowledged_at TIMESTAMP NULL
        );
        CREATE INDEX IF NOT EXISTS idx_agent_commands_agent_id ON agent_commands(agent_id);
        CREATE INDEX IF NOT EXISTS idx_agent_commands_status ON agent_commands(status);
        CREATE INDEX IF NOT EXISTS idx_agent_commands_agent_status ON agent_commands(agent_id, status);
        """);
}

app.MapGrpcService<GreeterService>();
app.MapGrpcService<AgentManagementServiceImpl>();
app.MapControllers();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "AgentManagementService", timestamp = DateTime.UtcNow }));

app.Run();
