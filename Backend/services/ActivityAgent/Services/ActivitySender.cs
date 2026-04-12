using Grpc.Net.Client;
using Grpc.Core;
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
    private readonly string _agentAuthToken;
    private readonly string _agentAuthHeader;

    public ActivitySender(ILogger<ActivitySender> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        var activityServiceUrl = configuration.GetValue<string>("ActivityService:Url") ?? "http://activityservice:5001";
        _agentAuthToken = (configuration["AgentAuth:Token"] ?? string.Empty).Trim();
        _agentAuthHeader = string.IsNullOrWhiteSpace(configuration["AgentAuth:HeaderName"])
            ? "x-agent-token"
            : configuration["AgentAuth:HeaderName"]!.Trim().ToLowerInvariant();
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
                    headers: BuildAgentMetadata(),
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

    private Metadata? BuildAgentMetadata()
    {
        if (string.IsNullOrWhiteSpace(_agentAuthToken))
            return null;

        return new Metadata
        {
            { _agentAuthHeader, _agentAuthToken }
        };
    }
}
