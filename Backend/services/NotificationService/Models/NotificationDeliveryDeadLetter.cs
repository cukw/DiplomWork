using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NotificationService.Models;

public sealed class NotificationDeliveryDeadLetter
{
    [Column("id")]
    public long Id { get; set; }

    [Column("notification_id")]
    public int NotificationId { get; set; }

    [Column("channel"), MaxLength(20)]
    public string Channel { get; set; } = "in_app";

    [Column("recipient_email"), MaxLength(320)]
    public string? RecipientEmail { get; set; }

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("failed_at")]
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
}
