using System.ComponentModel.DataAnnotations;

namespace AgentManagementService.Models;

public class AgentCommand
{
    public int Id { get; set; }

    public int AgentId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending"; // pending / success / failed / ignored / running

    [MaxLength(100)]
    public string RequestedBy { get; set; } = "system";

    [MaxLength(500)]
    public string ResultMessage { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
}
