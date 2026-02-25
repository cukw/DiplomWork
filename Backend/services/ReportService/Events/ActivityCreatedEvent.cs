namespace ActivityService.Services.Events;

public record ActivityCreatedEvent(
    long ActivityId,
    int ComputerId,
    string ActivityType,
    bool IsBlocked,
    decimal? RiskScore,
    DateTime OccurredAtUtc);
