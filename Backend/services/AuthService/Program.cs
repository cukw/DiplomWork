using AuthService.Services;
using AuthService.Data;
using AuthService.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Text;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// gRPC (HTTP/2) на порту 5003, REST (HTTP/1.1) на порту 5007
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5003, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    options.ListenAnyIP(5007, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddControllers();

var telemetryServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "auth-service";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(telemetryServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

// Configure Entity Framework
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register custom services
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();

// Configure JWT
var jwtSettings = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtSettings>(jwtSettings);

var jwtKeyRing = BuildJwtKeyRing(builder.Configuration);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = builder.Configuration.GetValue<bool?>("Jwt:RequireHttpsMetadata")
        ?? !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKeyResolver = (_, _, kid, _) => ResolveSigningKeys(jwtKeyRing, kid),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// Add CORS
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
    options.AddPolicy("Frontend", builder =>
    {
        builder.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCors("Frontend");

app.MapGrpcService<GreeterService>();
app.MapGrpcService<AuthServiceImpl>();
app.MapControllers();
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "AuthService", timestamp = DateTime.UtcNow }));

app.Run();

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

static IEnumerable<Microsoft.IdentityModel.Tokens.SecurityKey> ResolveSigningKeys(
    IReadOnlyDictionary<string, string> keyRing,
    string? keyId)
{
    if (!string.IsNullOrWhiteSpace(keyId) && keyRing.TryGetValue(keyId, out var secret))
        return [new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret))];

    return keyRing.Values
        .Select(secret => new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret)))
        .ToArray();
}
