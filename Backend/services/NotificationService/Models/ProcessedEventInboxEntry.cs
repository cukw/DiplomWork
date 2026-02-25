using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NotificationService.Models;

public class ProcessedEventInboxEntry
{
    [Column("id")]
    public long Id { get; set; }

    [Column("consumer"), MaxLength(128)]
    public string Consumer { get; set; } = string.Empty;

    [Column("event_key"), MaxLength(256)]
    public string EventKey { get; set; } = string.Empty;

    [Column("message_id"), MaxLength(128)]
    public string? MessageId { get; set; }

    [Column("processed_at")]
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
