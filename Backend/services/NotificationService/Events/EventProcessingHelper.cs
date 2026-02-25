using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NotificationService.Events;

internal static class EventProcessingHelper
{
    public static string ActivityCreatedKey(ActivityService.Services.Events.ActivityCreatedEvent @event) =>
        $"activity-created:{@event.ActivityId}:{Normalize(@event.ActivityType)}";

    public static string AnomalyDetectedKey(ActivityService.Services.Events.AnomalyDetectedEvent @event)
    {
        var descriptionHash = Sha256Hex(@event.Description ?? string.Empty);
        return $"anomaly-detected:{@event.ActivityId}:{Normalize(@event.AnomalyType)}:{descriptionHash}";
    }

    public static bool IsDuplicateProcessing(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
