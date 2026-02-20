using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Добавляем логирование
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

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
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured"))),
            ClockSkew = TimeSpan.Zero
        };
        options.RequireHttpsMetadata = false;
    });

builder.Services.AddAuthorization();
builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

// Добавляем middleware для обработки ошибок
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        
        var error = new { error = "An unexpected error occurred", timestamp = DateTime.UtcNow };
        await context.Response.WriteAsJsonAsync(error);
    });
});

// Добавляем middleware для логирования запросов
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    
    app.Logger.LogInformation("Request: {Method} {Path} - CorrelationId: {CorrelationId}",
        context.Request.Method, context.Request.Path, correlationId);
    
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Request failed: {Method} {Path} - CorrelationId: {CorrelationId}",
            context.Request.Method, context.Request.Path, correlationId);
        throw;
    }
});

app.UseAuthentication();
app.UseAuthorization();

// Добавляем health check эндпоинт
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

// Добавляем информационный эндпоинт
app.MapGet("/gateway/info", () => Results.Ok(new {
    service = "API Gateway",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
}));

await app.UseOcelot();
