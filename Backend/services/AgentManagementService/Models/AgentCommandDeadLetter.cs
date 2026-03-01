using System.ComponentModel.DataAnnotations;

namespace AgentManagementService.Models;

public class AgentCommandDeadLetter
{
    public int Id { get; set; }
    public int AgentCommandId { get; set; }
    public int AgentId { get; set; }

    [Required]
    [MaxLength(100)]
    public string CommandKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public int DeliveryAttempts { get; set; } = 0;
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
}
