using ActivityAgent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActivityAgent;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Activity Agent Starting...");

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                services.AddSingleton<IActivityCollector, ActivityCollector>();
                services.AddSingleton<IActivitySender, ActivitySender>();
                
                services.AddHostedService<ActivityWorker>();
            })
            .Build();

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application terminated unexpectedly: {ex.Message}");
        }
    }
}

public class ActivityWorker : BackgroundService
{
    private readonly ILogger<ActivityWorker> _logger;
    private readonly IActivityCollector _activityCollector;
    private readonly IActivitySender _activitySender;
    private readonly IConfiguration _configuration;

    public ActivityWorker(
        ILogger<ActivityWorker> logger,
        IActivityCollector activityCollector,
        IActivitySender activitySender,
        IConfiguration configuration)
    {
        _logger = logger;
        _activityCollector = activityCollector;
        _activitySender = activitySender;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isEnabled = _configuration.GetValue<bool>("Agent:Enabled", true);
        if (!isEnabled)
        {
            _logger.LogInformation("Activity Agent is disabled in configuration");
            return;
        }

        var collectionInterval = _configuration.GetValue<int>("Agent:CollectionInterval", 5000);
        _logger.LogInformation("Activity Agent started with collection interval: {Interval}ms", collectionInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Collecting activity...");
                
                var activity = await _activityCollector.CollectActivityAsync();
                
                var success = await _activitySender.SendActivityAsync(activity);
                
                if (success)
                {
                    _logger.LogDebug("Activity sent successfully: {ActivityType}", activity.ActivityType);
                }
                else
                {
                    _logger.LogWarning("Failed to send activity: {ActivityType}", activity.ActivityType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in activity collection loop");
            }

            await Task.Delay(collectionInterval, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Activity Agent is stopping");
        await base.StopAsync(cancellationToken);
    }
}