using Grpc.Net.Client;
using ActivityService;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ActivityAgent.Services;

public interface IActivitySender
{
    Task<int> SendActivitiesAsync(IEnumerable<ActivityReply> activities, CancellationToken cancellationToken = default);
}

public class ActivitySender : IActivitySender
{
    private readonly ILogger<ActivitySender> _logger;
    private readonly GrpcChannel _channel;
    private readonly ActivityGrpcService.ActivityGrpcServiceClient _client;

    public ActivitySender(ILogger<ActivitySender> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        var activityServiceUrl = configuration.GetValue<string>("ActivityService:Url") ?? "http://activityservice:5001";
        _channel = GrpcChannel.ForAddress(activityServiceUrl);
        _client = new ActivityGrpcService.ActivityGrpcServiceClient(_channel);
    }

    public async Task<int> SendActivitiesAsync(IEnumerable<ActivityReply> activities, CancellationToken cancellationToken = default)
    {
        var sentCount = 0;
        foreach (var activity in activities)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await _client.CreateActivityAsync(
                    new CreateActivityRequest { Activity = activity },
                    cancellationToken: cancellationToken);

                sentCount++;
                _logger.LogDebug("Activity sent successfully: {ActivityId}, Type: {ActivityType}", response.Id, activity.ActivityType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send activity batch item. Type: {ActivityType}", activity.ActivityType);
            }
        }

        return sentCount;
    }
}
