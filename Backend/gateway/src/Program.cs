using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Primitives;
using Microsoft.EntityFrameworkCore;
using Grpc.Core;
using System.Text;
using System.Threading.RateLimiting;
using System.Security.Cryptography.X509Certificates;
using Gateway.Services;
using Gateway.Data;
using Gateway.Security;
using Backend.Common.Infrastructure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

var jwtKeyRing = BuildJwtKeyRing(builder.Configuration);

// ─── gRPC-клиенты (всё общение через gRPC) ───────────────────────────────────
builder.Services.AddGrpcClient<ActivityClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Activity"] ?? "http://activityservice:5001"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));

builder.Services.AddGrpcClient<AuthClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Auth"] ?? "http://authservice:5003"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));

builder.Services.AddGrpcClient<UserClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:User"] ?? "http://userservice:5004"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));

builder.Services.AddGrpcClient<NotificationClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Notification"] ?? "http://notificationservice:5012"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));

builder.Services.AddGrpcClient<MetricsClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Metrics"] ?? "http://metricservice:5010"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));

builder.Services.AddGrpcClient<ReportClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Report"] ?? "http://reportservice:5013"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));

builder.Services.AddGrpcClient<AgentClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:Agent"] ?? "http://agentmanagementservice:5015"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration))
    .ConfigureChannel(o => o.UnsafeUseInsecureChannelCallCredentials = true)
    .AddCallCredentials((_, metadata, serviceProvider) =>
    {
        AddAgentAuthMetadata(serviceProvider.GetRequiredService<IConfiguration>(), metadata);
        return Task.CompletedTask;
    });

// ─── REST + Auth ──────────────────────────────────────────────────────────────
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ActionPermissionFilter>();
});
builder.Services.AddHttpClient();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var principal = context.User?.Identity?.Name;
        var key = string.IsNullOrWhiteSpace(principal)
            ? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"
            : principal;

        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 120,
            QueueLimit = 0,
            TokensPerPeriod = 120,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            AutoReplenishment = true
        });
    });
    options.AddPolicy("AuthEndpoints", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var telemetryServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "gateway";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(telemetryServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddGrpcClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

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
builder.Services.AddSingleton<RolePermissionStore>();
builder.Services.AddSingleton<DownloadFileStore>();
builder.Services.AddSingleton<IAdminAuditLogger, AdminAuditLogger>();
builder.Services.AddScoped<PolicyAccessListSyncService>();
builder.Services.AddScoped<ActionPermissionFilter>();
builder.Services.AddSingleton<PermissionEvaluator>();
builder.Services.Configure<AuthorizationMatrixOptions>(builder.Configuration.GetSection("AuthorizationMatrix"));

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var requireHttpsMetadata = builder.Configuration.GetValue<bool?>("Jwt:RequireHttpsMetadata")
            ?? !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, kid, _) => ResolveSigningKeys(jwtKeyRing, kid),
            ClockSkew = TimeSpan.Zero
        };
        options.RequireHttpsMetadata = requireHttpsMetadata;
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
{
    var configuredOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()?
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(origin => origin.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray()
        ?? [];

    var defaultOrigins = new[]
    {
        "http://localhost:3000",
        "https://localhost:3443",
        "http://127.0.0.1:3000",
        "https://127.0.0.1:3443"
    };

    var allowedOrigins = configuredOrigins.Length > 0 ? configuredOrigins : defaultOrigins;
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

var app = builder.Build();

await InitializeDatabaseWithRetryAsync(
    app.Services,
    app.Logger,
    async (services, cancellationToken) =>
    {
        var runtimeDbFactory = services.GetRequiredService<IDbContextFactory<GatewayRuntimeDbContext>>();
        await using var runtimeDb = await runtimeDbFactory.CreateDbContextAsync(cancellationToken);
        var migrationsDirectory = ResolveMigrationsDirectory(app.Environment.ContentRootPath);
        await SqlMigrationRunner.ApplyAsync(
            runtimeDb,
            "gateway-runtime",
            migrationsDirectory,
            app.Logger,
            cancellationToken);
    });

app.UseCors("Frontend");
app.UseRateLimiter();

// Correlation-ID логирование
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? context.TraceIdentifier
        ?? Guid.NewGuid().ToString("N");
    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    using (app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["CorrelationId"] = correlationId
    }))
    {
        app.Logger.LogInformation("Request: {Method} {Path}", context.Request.Method, context.Request.Path);
        await next();
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPrometheusScrapingEndpoint("/metrics");

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

static string ResolveMigrationsDirectory(string contentRootPath)
{
    var contentRootCandidate = Path.Combine(contentRootPath, "db", "migrations");
    if (Directory.Exists(contentRootCandidate))
        return contentRootCandidate;

    return Path.Combine(AppContext.BaseDirectory, "db", "migrations");
}

static IReadOnlyDictionary<string, string> BuildJwtKeyRing(IConfiguration configuration)
{
    var keyRing = configuration
        .GetSection("Jwt:Keys")
        .GetChildren()
        .Where(section => !string.IsNullOrWhiteSpace(section.Key) && !string.IsNullOrWhiteSpace(section.Value))
        .ToDictionary(section => section.Key.Trim(), section => section.Value!.Trim(), StringComparer.Ordinal);

    if (keyRing.Count > 0)
        return keyRing;

    var legacyKey = configuration["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(legacyKey))
        throw new InvalidOperationException("JWT keys are not configured. Set Jwt:Keys or Jwt:Key.");

    return new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["legacy"] = legacyKey.Trim()
    };
}

static IEnumerable<SecurityKey> ResolveSigningKeys(
    IReadOnlyDictionary<string, string> keyRing,
    string? keyId)
{
    if (!string.IsNullOrWhiteSpace(keyId) && keyRing.TryGetValue(keyId, out var secret))
        return [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))];

    return keyRing.Values
        .Select(secret => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)))
        .ToArray();
}

static HttpMessageHandler CreateGrpcHttpHandler(IConfiguration configuration)
{
    var mtlsEnabled = configuration.GetValue<bool>("Services:Mtls:Enabled");
    if (!mtlsEnabled)
    {
        return new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        };
    }

    var handler = new HttpClientHandler();
    var certificatePath = configuration["Services:Mtls:ClientCertificate:Path"];
    var certificatePassword = configuration["Services:Mtls:ClientCertificate:Password"];
    if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
    {
        handler.ClientCertificates.Add(LoadClientCertificate(certificatePath, certificatePassword));
    }

    var allowedThumbprints = configuration
        .GetSection("Services:Mtls:ServerThumbprints")
        .Get<string[]>()?
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant())
        .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
    {
        if (errors == System.Net.Security.SslPolicyErrors.None)
            return true;
        if (certificate is null)
            return false;

        var thumbprint = certificate.GetCertHashString().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return allowedThumbprints.Contains(thumbprint);
    };

    return handler;
}

static void AddAgentAuthMetadata(IConfiguration configuration, Metadata metadata)
{
    var token = (configuration["AgentAuth:Token"] ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(token))
        return;

    var headerName = string.IsNullOrWhiteSpace(configuration["AgentAuth:HeaderName"])
        ? "x-agent-token"
        : configuration["AgentAuth:HeaderName"]!.Trim().ToLowerInvariant();

    metadata.Add(headerName, token);
}

static X509Certificate2 LoadClientCertificate(string certificatePath, string? certificatePassword)
{
    return string.IsNullOrWhiteSpace(certificatePassword)
        ? X509CertificateLoader.LoadCertificateFromFile(certificatePath)
        : X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword, X509KeyStorageFlags.DefaultKeySet);
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
