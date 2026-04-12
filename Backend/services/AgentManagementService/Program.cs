using AgentManagementService.Services;
using AgentManagementService.Data;
using Backend.Common.Infrastructure;
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
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<AgentAuthInterceptor>();
});
builder.Services.AddControllers();
builder.Services.AddSingleton<ControlPlaneSigningService>();
builder.Services.AddSingleton<AgentAuthInterceptor>();
builder.Services.Configure<CommandDeliveryOptions>(builder.Configuration.GetSection("CommandDelivery"));
builder.Services.AddHostedService<AgentCommandRetryWorker>();
builder.Services.AddGrpcClient<UserLookupClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:User"] ?? "http://localhost:5004"));

// Configure Entity Framework
builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
// Lightweight schema bootstrap for control-plane tables (works without EF migrations).
await InitializeDatabaseWithRetryAsync(
    app.Services,
    app.Logger,
    async (services, cancellationToken) =>
    {
        var db = services.GetRequiredService<AgentDbContext>();
        var migrationsDirectory = ResolveMigrationsDirectory(app.Environment.ContentRootPath);
        await SqlMigrationRunner.ApplyAsync(
            db,
            "agent-control-plane",
            migrationsDirectory,
            app.Logger,
            cancellationToken);
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

static string ResolveMigrationsDirectory(string contentRootPath)
{
    var contentRootCandidate = Path.Combine(contentRootPath, "db", "migrations");
    if (Directory.Exists(contentRootCandidate))
        return contentRootCandidate;

    return Path.Combine(AppContext.BaseDirectory, "db", "migrations");
}
