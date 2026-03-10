using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Primitives;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Gateway.Services;
using Gateway.Data;

// Алиасы для gRPC-клиентов
using ActivityClient     = Gateway.Protos.Activity.ActivityGrpcService.ActivityGrpcServiceClient;
using AuthClient         = Gateway.Protos.Auth.AuthService.AuthServiceClient;
using UserClient         = Gateway.Protos.User.UserService.UserServiceClient;
using NotificationClient = Gateway.Protos.Notification.NotificationService.NotificationServiceClient;
using MetricsClient      = Gateway.Protos.Metrics.MetricsService.MetricsServiceClient;
using ReportClient       = Gateway.Protos.Report.ReportService.ReportServiceClient;
using AgentClient        = Gateway.Protos.Agent.AgentManagementService.AgentManagementServiceClient;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ─── gRPC-клиенты (всё общение через gRPC) ───────────────────────────────────
builder.Services.AddGrpcClient<ActivityClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Activity"] ?? "http://activityservice:5001"));

builder.Services.AddGrpcClient<AuthClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Auth"] ?? "http://authservice:5003"));

builder.Services.AddGrpcClient<UserClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:User"] ?? "http://userservice:5004"));

builder.Services.AddGrpcClient<NotificationClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Notification"] ?? "http://notificationservice:5012"));

builder.Services.AddGrpcClient<MetricsClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Metrics"] ?? "http://metricservice:5010"));

builder.Services.AddGrpcClient<ReportClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Report"] ?? "http://reportservice:5013"));

builder.Services.AddGrpcClient<AgentClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Agent"] ?? "http://agentmanagementservice:5015"));

// ─── REST + Auth ──────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddHttpClient();

var gatewayRuntimeConnection = builder.Configuration.GetConnectionString("GatewayRuntime")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(gatewayRuntimeConnection))
{
    throw new InvalidOperationException("ConnectionStrings:GatewayRuntime is not configured");
}
gatewayRuntimeConnection = NormalizeGatewayRuntimeConnectionString(gatewayRuntimeConnection);

builder.Services.AddDbContextFactory<GatewayRuntimeDbContext>(options =>
    options.UseNpgsql(gatewayRuntimeConnection));

builder.Services.AddSingleton<AlertRuleStore>();
builder.Services.AddSingleton<AppSettingsStore>();
builder.Services.AddSingleton<DownloadFileStore>();
builder.Services.AddScoped<PolicyAccessListSyncService>();

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    builder.Configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("JWT Key not configured"))),
            ClockSkew = TimeSpan.Zero
        };
        options.RequireHttpsMetadata = false;
        options.UseSecurityTokenValidators = true;
        
        // Configure token retrieval from Authorization header
        options.SaveToken = true;
        options.IncludeErrorDetails = true;

        // Диагностика JWT — покажет точную причину 401
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");

                // Robust extraction for cases where proxies/clients duplicate Authorization values
                // and Kestrel exposes them as multiple values or a comma-joined string.
                StringValues authHeaders = context.HttpContext.Request.Headers.Authorization;
                string? extractedToken = null;

                foreach (var rawHeader in authHeaders)
                {
                    if (string.IsNullOrWhiteSpace(rawHeader))
                        continue;

                    // Split on commas to tolerate merged duplicate Authorization headers.
                    foreach (var part in rawHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!part.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var candidate = part["Bearer ".Length..].Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(candidate))
                            continue;

                        if (string.Equals(candidate, "null", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(candidate, "undefined", StringComparison.OrdinalIgnoreCase))
                            continue;

                        extractedToken = candidate;
                        break;
                    }

                    if (extractedToken is not null)
                        break;
                }

                if (extractedToken is null &&
                    context.HttpContext.Request.Path.StartsWithSegments("/api/live/stream"))
                {
                    var queryToken = context.Request.Query["access_token"].FirstOrDefault()?.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(queryToken) &&
                        !string.Equals(queryToken, "null", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(queryToken, "undefined", StringComparison.OrdinalIgnoreCase))
                    {
                        extractedToken = queryToken;
                        logger.LogInformation(
                            "JWT token extracted from query for live stream - Length={Length}, DotCount={DotCount}",
                            queryToken.Length,
                            queryToken.Count(c => c == '.'));
                    }
                }

                if (!string.IsNullOrWhiteSpace(extractedToken))
                {
                    context.Token = extractedToken;
                    logger.LogInformation(
                        "JWT token extracted - HeaderValues={HeaderValues}, Length={Length}, DotCount={DotCount}",
                        authHeaders.Count,
                        extractedToken.Length,
                        extractedToken.Count(c => c == '.'));
                }
                else
                {
                    logger.LogInformation("JWT MessageReceived - HeaderValues={HeaderValues}, AuthHeader={AuthHeader}, Token={Token}",
                        authHeaders.Count,
                        authHeaders.FirstOrDefault(),
                        context.Token);
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");
                logger.LogWarning("JWT Auth FAILED: {Error} | Path: {Path}",
                    context.Exception.Message, context.Request.Path);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");
                var hasAuth = context.Request.Headers.ContainsKey("Authorization");
                var authHeaders = context.Request.Headers.Authorization;
                logger.LogWarning(
                    "JWT Challenge: HasAuthHeader={HasAuth}, HeaderValues={HeaderValues}, AuthHeader={AuthHeader}, Error={Error}, Path={Path}",
                    hasAuth,
                    authHeaders.Count,
                    authHeaders.FirstOrDefault(),
                    context.AuthenticateFailure?.Message ?? "no token",
                    context.Request.Path);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

await InitializeDatabaseWithRetryAsync(
    app.Services,
    app.Logger,
    async (services, cancellationToken) =>
    {
        var runtimeDbFactory = services.GetRequiredService<IDbContextFactory<GatewayRuntimeDbContext>>();
        await using var runtimeDb = await runtimeDbFactory.CreateDbContextAsync(cancellationToken);
        await runtimeDb.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS app_settings_documents (
                id           INTEGER PRIMARY KEY,
                payload_json TEXT NOT NULL,
                updated_at   TIMESTAMP NOT NULL DEFAULT NOW()
            );
        ", cancellationToken);
    });

app.UseCors("AllowAll");

// Correlation-ID логирование
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString();
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    app.Logger.LogInformation("Request: {Method} {Path} - CorrelationId: {CorrelationId}",
        context.Request.Method, context.Request.Path, correlationId);
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));
app.MapGet("/gateway/info", () => Results.Ok(new
{
    service = "API Gateway (gRPC)",
    version = "2.0.0",
    timestamp = DateTime.UtcNow
}));

app.Run();

static string NormalizeGatewayRuntimeConnectionString(string connectionString)
{
    var runningInContainer = string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    var runtimeConnectionFromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__GatewayRuntime");

    if (!runningInContainer || !string.IsNullOrWhiteSpace(runtimeConnectionFromEnv))
        return connectionString;

    if (connectionString.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("GatewayRuntime connection string uses localhost inside container. Falling back to postgres-user host.");
        return connectionString.Replace("Host=localhost", "Host=postgres-user", StringComparison.OrdinalIgnoreCase);
    }

    if (connectionString.Contains("Host=127.0.0.1", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("GatewayRuntime connection string uses 127.0.0.1 inside container. Falling back to postgres-user host.");
        return connectionString.Replace("Host=127.0.0.1", "Host=postgres-user", StringComparison.OrdinalIgnoreCase);
    }

    return connectionString;
}

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
            logger.LogInformation("Gateway runtime database bootstrap completed on attempt {Attempt}.", attempt);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                ex,
                "Gateway runtime database bootstrap attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s.",
                attempt,
                maxAttempts,
                delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 15));
        }
    }
}
