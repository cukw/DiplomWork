namespace AgentManagementService.Services;

public sealed class CommandDeliveryOptions
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int DispatchTimeoutSeconds { get; set; } = 30;
    public int MaxDeliveryAttempts { get; set; } = 5;
    public int RetryBaseDelaySeconds { get; set; } = 5;
    public int RetryMaxDelaySeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 200;
}
