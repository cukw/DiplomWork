using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ActivityService.Services.Models
{
    public class Activity
    {
        [Key, Column("id")]
        public long Id { get; set; }
        
        [Column("computer_id"), Required]
        public int ComputerId { get; set; }
        
        [Column("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        [Column("activity_type"), Required, MaxLength(50)]
        public string ActivityType { get; set; } = "";
        
        [Column("details")]  // JSONB
        public string? Details { get; set; }
        
        [Column("duration_ms")]
        public int? DurationMs { get; set; }
        
        [Column("url"), MaxLength(500)]
        public string? Url { get; set; }
        
        [Column("process_name"), MaxLength(255)]
        public string? ProcessName { get; set; }
        
        [Column("is_blocked")]
        public bool IsBlocked { get; set; }
        
        [Column("risk_score")]
        public decimal? RiskScore { get; set; }
        
        [Column("synced")]
        public bool Synced { get; set; }
        
        // Navigation property
        public virtual ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();
    }
}
