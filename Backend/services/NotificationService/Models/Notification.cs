using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NotificationService.Models;

public class Notification
{
    [Column ("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }
    
    [Column("type"), MaxLength(50)]
    public string? Type { get; set; }
    
    [Column("title"),MaxLength(255)]
    public string? Title { get; set; }
    [Column("message")]
    public string? Message { get; set; }
    [Column("is_read")]
    public bool IsRead { get; set; } = false;
    [Column("sent_at")]
    public DateTime? SentAt { get; set; }

    [Column("recipient_email"), MaxLength(320)]
    public string? RecipientEmail { get; set; }

    [Column("channel"),MaxLength(20)]
    public string Channel { get; set; } = "email"; // email / ui / telegram etc.

    [Column("delivery_status"), MaxLength(32)]
    public string DeliveryStatus { get; set; } = "pending";

    [Column("delivery_attempts")]
    public int DeliveryAttempts { get; set; } = 0;

    [Column("max_delivery_attempts")]
    public int MaxDeliveryAttempts { get; set; } = 3;

    [Column("last_delivery_error")]
    public string? LastDeliveryError { get; set; }

    [Column("next_retry_at")]
    public DateTime? NextRetryAt { get; set; }

    [Column("delivered_at")]
    public DateTime? DeliveredAt { get; set; }
}
