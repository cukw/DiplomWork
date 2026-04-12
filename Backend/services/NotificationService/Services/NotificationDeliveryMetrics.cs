using System.Diagnostics.Metrics;
using System.Threading;

namespace NotificationService.Services;

public static class NotificationDeliveryMetrics
{
    public const string MeterName = "NotificationService.Delivery";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static long _queueDepth;
    private static long _retryDueDepth;
    private static long _dlqDepth;

    static NotificationDeliveryMetrics()
    {
        Meter.CreateObservableGauge(
            name: "notification_delivery_queue_depth",
            observeValue: () => Interlocked.Read(ref _queueDepth),
            unit: "items",
            description: "Current count of notifications pending or failed for delivery.");

        Meter.CreateObservableGauge(
            name: "notification_delivery_retry_due_depth",
            observeValue: () => Interlocked.Read(ref _retryDueDepth),
            unit: "items",
            description: "Current count of notifications that are ready for retry now.");

        Meter.CreateObservableGauge(
            name: "notification_delivery_dlq_depth",
            observeValue: () => Interlocked.Read(ref _dlqDepth),
            unit: "items",
            description: "Current count of notifications in dead letter queue.");
    }

    public static void Update(long queueDepth, long retryDueDepth, long dlqDepth)
    {
        Interlocked.Exchange(ref _queueDepth, Math.Max(0, queueDepth));
        Interlocked.Exchange(ref _retryDueDepth, Math.Max(0, retryDueDepth));
        Interlocked.Exchange(ref _dlqDepth, Math.Max(0, dlqDepth));
    }
}
