using NotificationService.Services;
using NotificationService.Data;
using NotificationService.Events;
using Backend.Common.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using ActivityService.Services.Events;
using AuthLookup = AuthService;
using UserLookup = UserService;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Security.Cryptography.X509Certificates;

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
builder.Services.AddHttpClient("NotificationDelivery");
builder.Services.Configure<NotificationDeliveryOptions>(builder.Configuration.GetSection("Delivery"));
builder.Services.AddGrpcClient<UserLookup.UserService.UserServiceClient>(options =>
    options.Address = new Uri(builder.Configuration["Services:User"] ?? "http://userservice:5004"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));
builder.Services.AddGrpcClient<AuthLookup.AuthService.AuthServiceClient>(options =>
    options.Address = new Uri(builder.Configuration["Services:Auth"] ?? "http://authservice:5003"))
    .ConfigurePrimaryHttpMessageHandler(() => CreateGrpcHttpHandler(builder.Configuration));
builder.Services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
builder.Services.AddScoped<INotificationDeliveryProcessor, NotificationDeliveryProcessor>();
builder.Services.AddHostedService<NotificationDeliveryRetryWorker>();
builder.Services.AddHostedService<NotificationDeliveryMetricsWorker>();

var telemetryServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "notification-service";
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
        .AddMeter(NotificationDeliveryMetrics.MeterName)
        .AddPrometheusExporter());

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

var app = builder.Build();

await InitializeDatabaseWithRetryAsync(
    app.Services,
    app.Logger,
    async (services, cancellationToken) =>
    {
        var dbContext = services.GetRequiredService<NotificationDbContext>();
        var migrationsDirectory = ResolveMigrationsDirectory(app.Environment.ContentRootPath);
        await SqlMigrationRunner.ApplyAsync(
            dbContext,
            "notification-runtime",
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

app.MapGrpcService<GreeterService>();
app.MapGrpcService<NotificationServiceImpl>();
app.MapControllers();
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapGet("/", () => "gRPC NotificationService");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "NotificationService", timestamp = DateTime.UtcNow }));

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
            logger.LogInformation("NotificationService database bootstrap completed on attempt {Attempt}.", attempt);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(
                ex,
                "NotificationService database bootstrap attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelaySeconds}s.",
                attempt,
                maxAttempts,
                delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 15));
        }
    }
}
