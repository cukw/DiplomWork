using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityService.Services.Models
{
    public class Anomaly
    {
        [Key]
        public int Id { get; set; }
        
        [Column("activity_id"), Required]
        public long ActivityId { get; set; }
        
        [Required, MaxLength(100)]
        public string Type { get; set; } = "";
        
        public string? Description { get; set; }
        
        [Column("detected_at")]
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
        
        // Foreign key
        [ForeignKey("ActivityId")]
        public virtual Activity Activity { get; set; } = null!;
    }
}
