using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityService.Services.Models
{
    public class Anomaly
    {
        [Column("id"), Key]
        public int Id { get; set; }
        
        [Column("activity_id"), Required]
        public long ActivityId { get; set; }
        
        [Column("type"),Required, MaxLength(100)]
        public string Type { get; set; } = "";
        
        [Column("description")]
        public string? Description { get; set; }
        
        [Column("detected_at")]
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
        
        // Foreign key
        [ForeignKey("ActivityId")]
        public virtual Activity Activity { get; set; } = null!;
    }
}
