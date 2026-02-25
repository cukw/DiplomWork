using System.ComponentModel.DataAnnotations;

namespace AgentManagementService.Models;

public class AgentPolicy
{
    public int Id { get; set; }

    public int AgentId { get; set; }
    public int ComputerId { get; set; }

    [Required]
    [MaxLength(50)]
    public string PolicyVersion { get; set; } = "1";

    public int CollectionIntervalSec { get; set; } = 5;
    public int HeartbeatIntervalSec { get; set; } = 15;
    public int FlushIntervalSec { get; set; } = 5;

    public bool EnableProcessCollection { get; set; } = true;
    public bool EnableBrowserCollection { get; set; } = true;
    public bool EnableActiveWindowCollection { get; set; } = true;
    public bool EnableIdleCollection { get; set; } = true;

    public int IdleThresholdSec { get; set; } = 120;
    public int BrowserPollIntervalSec { get; set; } = 10;
    public int ProcessSnapshotLimit { get; set; } = 50;

    public float HighRiskThreshold { get; set; } = 85f;
    public bool AutoLockEnabled { get; set; } = true;
    public bool AdminBlocked { get; set; }

    [MaxLength(500)]
    public string? BlockedReason { get; set; }

    // JSON array of browser names.
    public string BrowsersJson { get; set; } = "[\"chrome\",\"edge\",\"firefox\"]";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
