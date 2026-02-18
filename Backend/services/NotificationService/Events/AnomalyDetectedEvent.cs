namespace NotificationService.Events;

public record AnomalyDetectedEvent(long ActivityId, int ComputerId, string ActivityType, string AnomalyType, string Description);