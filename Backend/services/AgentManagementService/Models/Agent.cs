using System.ComponentModel.DataAnnotations;

namespace AgentManagementService.Models;

public class Agent
{
    public int Id { get; set; }
    
    public int ComputerId { get; set; }
    
    [Required]
    [MaxLength(20)]
    public string Version { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "online"; // online / offline / updating
    
    public DateTime? LastHeartbeat { get; set; }
    
    [MaxLength(20)]
    public string? ConfigVersion { get; set; }
    
    public DateTime? OfflineSince { get; set; }

    [MaxLength(20)]
    public string? DesiredVersion { get; set; }

    public DateTime? DesiredVersionSetAt { get; set; }

    public string HealthJson { get; set; } = "{}";

    public int QueueSize { get; set; }

    public DateTime? LastCollectedAt { get; set; }

    public DateTime? LastSentAt { get; set; }

    [MaxLength(500)]
    public string LastError { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? PolicyVersion { get; set; }

    public string CapabilitiesJson { get; set; } = "{}";

    public string CollectorStatusesJson { get; set; } = "{}";

    [MaxLength(50)]
    public string? SourcePlatform { get; set; }
}
