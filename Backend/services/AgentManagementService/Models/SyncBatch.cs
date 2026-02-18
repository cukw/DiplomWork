using System.ComponentModel.DataAnnotations;

namespace AgentManagementService.Models;

public class SyncBatch
{
    public int Id { get; set; }
    
    public int AgentId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string BatchId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending"; // pending / success / failed
    
    public DateTime? SyncedAt { get; set; }
    
    public int RecordsCount { get; set; } = 0;
}