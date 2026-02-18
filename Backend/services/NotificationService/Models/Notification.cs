using System.ComponentModel.DataAnnotations;

namespace NotificationService.Models;

public class Notification
{
    public int Id { get; set; }
    
    public int? UserId { get; set; }
    
    [MaxLength(50)]
    public string? Type { get; set; }
    
    [MaxLength(255)]
    public string? Title { get; set; }
    
    public string? Message { get; set; }
    
    public bool IsRead { get; set; } = false;
    
    public DateTime? SentAt { get; set; }
    
    [MaxLength(20)]
    public string Channel { get; set; } = "email"; // email / ui / telegram etc.
}