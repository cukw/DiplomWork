using Microsoft.EntityFrameworkCore;

namespace ActivityService.Services.Events;

public record ActivityCreatedEvent(long ActivityId, int ComputerId, string ActivityType);