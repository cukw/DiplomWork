namespace ActivityService;

public class ActivityCreatedEvent
{
    public record ActivityCreatedEvent(long ActivityId, int ComputerId, string ActivityType);
}
