using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NotificationService.Models;

public class NotificationTemplate
{
    [Column("id")]
    public int Id { get; set; }
    
    [Required]
    [Column("type"),MaxLength(50)]
    public string Type { get; set; } = string.Empty;
    
    [Column("subject"), MaxLength(255)]
    public string? Subject { get; set; }
    [Column("body_template")]
    public string? BodyTemplate { get; set; }
}