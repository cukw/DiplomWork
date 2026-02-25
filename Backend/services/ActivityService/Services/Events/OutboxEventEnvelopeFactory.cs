using System.Text.Json;
using ActivityService.Services.Models;

namespace ActivityService.Services.Events;

internal static class OutboxEventEnvelopeFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string ActivityCreatedType = "activity.created.v1";
    public const string AnomalyDetectedType = "activity.anomaly-detected.v1";

    public static OutboxMessage CreateActivityCreated(ActivityService.Services.Models.Activity activity) =>
        Create(ActivityCreatedType, activity.Id, new ActivityCreatedEvent(
            activity.Id,
            activity.ComputerId,
            activity.ActivityType,
            activity.IsBlocked,
            activity.RiskScore,
            DateTime.SpecifyKind(activity.Timestamp, DateTimeKind.Utc)));

    public static OutboxMessage CreateAnomalyDetected(
        ActivityService.Services.Models.Activity activity,
        ActivityService.Services.Models.Anomaly anomaly) =>
        Create(AnomalyDetectedType, activity.Id, new AnomalyDetectedEvent(
            activity.Id,
            activity.ComputerId,
            activity.ActivityType,
            anomaly.Type,
            anomaly.Description ?? string.Empty,
            activity.IsBlocked,
            activity.RiskScore,
            DateTime.SpecifyKind(activity.Timestamp, DateTimeKind.Utc)));

    private static OutboxMessage Create<T>(string eventType, long activityId, T payload)
    {
        return new OutboxMessage
        {
            EventType = eventType,
            ActivityId = activityId,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = DateTime.UtcNow,
            AvailableAt = DateTime.UtcNow,
            Headers = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["x-source-service"] = "ActivityService",
                ["x-event-type"] = eventType,
                ["x-activity-id"] = activityId.ToString(),
                ["x-created-at-utc"] = DateTime.UtcNow.ToString("O")
            }, JsonOptions)
        };
    }

    public static object? DeserializePayload(string eventType, string payload) =>
        eventType switch
        {
            ActivityCreatedType => JsonSerializer.Deserialize<ActivityCreatedEvent>(payload, JsonOptions),
            AnomalyDetectedType => JsonSerializer.Deserialize<AnomalyDetectedEvent>(payload, JsonOptions),
            _ => null
        };

    public static IReadOnlyDictionary<string, string> DeserializeHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(headers, JsonOptions)
               ?? new Dictionary<string, string>();
    }
}
