using System.ComponentModel.DataAnnotations;

namespace NotificationService.Models;

public class NotificationTemplate
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;
    
    [MaxLength(255)]
    public string? Subject { get; set; }
    
    public string? BodyTemplate { get; set; }
}