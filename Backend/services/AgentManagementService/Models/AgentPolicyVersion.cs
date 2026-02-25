using System.ComponentModel.DataAnnotations;

namespace AgentManagementService.Models;

public class AgentPolicyVersion
{
    public int Id { get; set; }

    public int AgentId { get; set; }

    [Required]
    [MaxLength(50)]
    public string PolicyVersion { get; set; } = "1";

    [Required]
    [MaxLength(20)]
    public string ChangeType { get; set; } = "update"; // create / update / delete / rollback

    [MaxLength(100)]
    public string ChangedBy { get; set; } = "system";

    [Required]
    public string SnapshotJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
