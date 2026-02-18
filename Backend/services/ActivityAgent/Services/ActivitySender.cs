using Grpc.Net.Client;
using ActivityService;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ActivityAgent.Services;

public interface IActivitySender
{
    Task<bool> SendActivityAsync(ActivityReply activity);
}

public class ActivitySender : IActivitySender
{
    private readonly ILogger<ActivitySender> _logger;
    private readonly IConfiguration _configuration;
    private readonly GrpcChannel _channel;

    public ActivitySender(ILogger<ActivitySender> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        var activityServiceUrl = _configuration.GetValue<string>("ActivityService:Url") ?? "http://localhost:5001";
        _channel = GrpcChannel.ForAddress(activityServiceUrl);
    }

    public async Task<bool> SendActivityAsync(ActivityReply activity)
    {
        try
        {
            var client = new ActivityService.ActivityService.ActivityServiceClient(_channel);
            
            var request = new CreateActivityRequest
            {
                Activity = activity
            };
            
            var response = await client.CreateActivityAsync(request);
            
            _logger.LogInformation("Activity sent successfully: {ActivityId}, Type: {ActivityType}", 
                response.Id, activity.ActivityType);
                
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending activity to ActivityService");
            return false;
        }
    }
}