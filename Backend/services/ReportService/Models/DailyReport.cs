using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ReportService.Models;

public class DailyReport
{
    public int Id { get; set; }
    
    [Column(TypeName = "date")]
    public DateTime ReportDate { get; set; }
    
    public int ComputerId { get; set; }
    
    public int? UserId { get; set; }
    
    public long TotalActivities { get; set; } = 0;
    
    public long BlockedActions { get; set; } = 0;
    
    [Column(TypeName = "numeric(5,2)")]
    public decimal? AvgRiskScore { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}