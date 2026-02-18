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
}