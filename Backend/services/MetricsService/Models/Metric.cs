using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace MetricsService.Models;

public class Metric
{
    public int Id { get; set; }
    
    public int? UserId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;
    
    public string Config { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual ICollection<WhitelistEntry> WhitelistEntries { get; set; } = new List<WhitelistEntry>();
    public virtual ICollection<BlacklistEntry> BlacklistEntries { get; set; } = new List<BlacklistEntry>();
    
    // Helper property to work with config as JSON object
    public Dictionary<string, object> ConfigDictionary
    {
        get
        {
            if (string.IsNullOrEmpty(Config))
                return new Dictionary<string, object>();
            
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(Config) ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }
        set
        {
            Config = JsonSerializer.Serialize(value);
        }
    }
}