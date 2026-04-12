using Microsoft.EntityFrameworkCore;
using MassTransit;
using Grpc.Reflection;
using ActivityService.Services.Data;
using ActivityService.Services.Events;
using ActivityService.Services;
using ActivityService.Services.Security;
using Backend.Common.Infrastructure;
using Microsoft.Extensions.Options;
using UserLookup = UserService;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Security.Cryptography.X509Certificates;

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
    options.Interceptors.Add<AgentAuthInterceptor>();
});

builder.Services.AddGrpcClient<UserLookup.UserService.UserServiceClient>(options =>
    options.Address = new Uri(builder.Configuration["Services:User"] ?? "http://userservice:5004"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string missing")));

var telemetryServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "activity-service";
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

builder.Services.AddGrpcReflection();
builder.Services.AddSingleton<AgentAuthInterceptor>();

builder.Services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();
builder.Services.AddHostedService<ActivityOutboxDispatcher>();
builder.Services.Configure<ActivityRetentionOptions>(builder.Configuration.GetSection("ActivityRetention"));
builder.Services.AddHostedService<ActivityRetentionWorker>();

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

await InitializeDatabaseWithRetryAsync(
    app.Services,
    app.Logger,
    async (services, cancellationToken) =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        var migrationsDirectory = ResolveMigrationsDirectory(app.Environment.ContentRootPath);
        await SqlMigrationRunner.ApplyAsync(
            db,
            "activity-runtime",
            migrationsDirectory,
            app.Logger,
            cancellationToken);
    });

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
        await next();
    }
});

if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();

app.MapGrpcService<ActivityServiceImpl>();
app.MapControllers();
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ActivityService", timestamp = DateTime.UtcNow }));

app.Run();

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

static X509Certificate2 LoadClientCertificate(string certificatePath, string? certificatePassword)
{
    return string.IsNullOrWhiteSpace(certificatePassword)
        ? X509CertificateLoader.LoadCertificateFromFile(certificatePath)
        : X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword, X509KeyStorageFlags.DefaultKeySet);
}

static string ResolveMigrationsDirectory(string contentRootPath)
{
    var contentRootCandidate = Path.Combine(contentRootPath, "db", "migrations");
    if (Directory.Exists(contentRootCandidate))
        return contentRootCandidate;

    return Path.Combine(AppContext.BaseDirectory, "db", "migrations");
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
            logger.LogInformation("ActivityService database bootstrap completed on attempt {Attempt}.", attempt);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                ex,
                "ActivityService database bootstrap attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s.",
                attempt,
                maxAttempts,
                delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 15));
        }
    }
}
