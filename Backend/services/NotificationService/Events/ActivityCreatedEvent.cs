namespace NotificationService.Events;

public record ActivityCreatedEvent(long ActivityId, int ComputerId, string ActivityType);