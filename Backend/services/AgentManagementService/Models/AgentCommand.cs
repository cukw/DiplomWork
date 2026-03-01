using System.ComponentModel.DataAnnotations;

namespace AgentManagementService.Models;

public class AgentCommand
{
    public int Id { get; set; }

    public int AgentId { get; set; }

    [Required]
    [MaxLength(100)]
    public string CommandKey { get; set; } = string.Empty;

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

    public int DeliveryAttempts { get; set; } = 0;
    public int MaxDeliveryAttempts { get; set; } = 5;
    public DateTime? LastDispatchAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? TimeoutAt { get; set; }

    [MaxLength(500)]
    public string DeadLetterReason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
}
