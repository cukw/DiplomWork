using System.Security.Cryptography;
using System.Text;

namespace MetricsService.Events;

internal static class MetricsEventProcessingHelper
{
    public static string ActivityKey(ActivityService.Services.Events.ActivityCreatedEvent @event) =>
        $"activity-created:{@event.ActivityId}:{Normalize(@event.ActivityType)}";

    public static string AnomalyKey(ActivityService.Services.Events.AnomalyDetectedEvent @event)
    {
        var descriptionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(@event.Description ?? string.Empty)));
        return $"anomaly-detected:{@event.ActivityId}:{Normalize(@event.AnomalyType)}:{descriptionHash}";
    }

    public static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string Normalize(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
