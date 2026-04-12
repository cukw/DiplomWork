using UserService.Services;
using UserService.Data;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    dbContext.Database.Migrate();
    dbContext.Database.ExecuteSqlRaw(@"
        -- Clean orphan computers before enforcing NOT NULL relationship.
        DELETE FROM computers WHERE user_id IS NULL;

        ALTER TABLE IF EXISTS computers
            ALTER COLUMN user_id SET NOT NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS uq_computers_user_id
            ON computers(user_id);
    ");
}

// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();
app.MapGrpcService<UserServiceImpl>();
app.MapControllers();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "UserService", timestamp = DateTime.UtcNow }));

app.Run();
