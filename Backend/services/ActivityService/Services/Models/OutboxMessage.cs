using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityService.Services.Models;

public class OutboxMessage
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [MaxLength(128)]
    [Column("event_type")]
    public string EventType { get; set; } = string.Empty;

    [Column("activity_id")]
    public long? ActivityId { get; set; }

    [Required]
    [Column("payload")]
    public string Payload { get; set; } = string.Empty;

    [Column("headers")]
    public string? Headers { get; set; }

    [Column("attempt_count")]
    public int AttemptCount { get; set; }

    [Column("available_at")]
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [Column("last_error")]
    public string? LastError { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
