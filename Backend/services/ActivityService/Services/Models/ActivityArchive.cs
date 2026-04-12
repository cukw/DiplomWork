using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActivityService.Services.Models
{
    public class ActivityArchive
    {
        [Key, Column("id")]
        public long Id { get; set; }

        [Column("original_activity_id")]
        public long OriginalActivityId { get; set; }

        [Column("computer_id"), Required]
        public int ComputerId { get; set; }

        [Column("timestamp")]
        public DateTime Timestamp { get; set; }

        [Column("activity_type"), Required, MaxLength(50)]
        public string ActivityType { get; set; } = "";

        [Column("details")]
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

        [Column("user_id")]
        public long? UserId { get; set; }

        [Column("agent_id")]
        public long? AgentId { get; set; }

        [Column("agent_version"), MaxLength(50)]
        public string? AgentVersion { get; set; }

        [Column("device_name"), MaxLength(255)]
        public string? DeviceName { get; set; }

        [Column("collector"), MaxLength(100)]
        public string? Collector { get; set; }

        [Column("event_id"), MaxLength(100)]
        public string? EventId { get; set; }

        [Column("sequence")]
        public long? Sequence { get; set; }

        [Column("batch_id"), MaxLength(100)]
        public string? BatchId { get; set; }

        [Column("source_platform"), MaxLength(50)]
        public string? SourcePlatform { get; set; }

        [Column("archived_at")]
        public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    }
}
