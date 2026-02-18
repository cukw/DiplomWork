using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace ReportService.Models;

public class UserStats
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public DateTime PeriodStart { get; set; }
    
    public DateTime PeriodEnd { get; set; }
    
    public long? TotalTimeMs { get; set; }
    
    [Column(TypeName = "jsonb")]
    public string? RiskySites { get; set; }
    
    public int Violations { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Helper property to work with RiskySites as JSON array
    public List<string> RiskySitesList
    {
        get
        {
            if (string.IsNullOrEmpty(RiskySites))
                return new List<string>();
            
            try
            {
                return JsonSerializer.Deserialize<List<string>>(RiskySites) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }
        set
        {
            RiskySites = JsonSerializer.Serialize(value);
        }
    }
}