namespace ActivityService.Services.Events;

public record AnomalyDetectedEvent(
    long ActivityId,
    int ComputerId,
    string ActivityType,
    string AnomalyType,
    string Description,
    bool IsBlocked,
    decimal? RiskScore,
    DateTime OccurredAtUtc);
