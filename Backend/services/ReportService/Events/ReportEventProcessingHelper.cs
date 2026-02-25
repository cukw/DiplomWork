using System.Security.Cryptography;
using System.Text;

namespace ReportService.Events;

internal static class ReportEventProcessingHelper
{
    public static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static string ActivityKey(ActivityService.Services.Events.ActivityCreatedEvent @event) =>
        $"activity-created:{@event.ActivityId}:{Normalize(@event.ActivityType)}";

    public static string AnomalyKey(ActivityService.Services.Events.AnomalyDetectedEvent @event)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(@event.Description ?? string.Empty)));
        return $"anomaly-detected:{@event.ActivityId}:{Normalize(@event.AnomalyType)}:{hash}";
    }

    private static string Normalize(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
