using System.ComponentModel.DataAnnotations;

namespace MetricsService.Models;

public class WhitelistEntry
{
    public int Id { get; set; }
    
    public int MetricId { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string Pattern { get; set; } = string.Empty;
    
    [MaxLength(20)]
    public string Action { get; set; } = "allow";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual Metric Metric { get; set; } = null!;
}