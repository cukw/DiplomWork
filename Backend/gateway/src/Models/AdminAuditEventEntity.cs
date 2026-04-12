using System.ComponentModel.DataAnnotations;

namespace Gateway.Models;

public sealed class AdminAuditEventEntity
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Actor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TargetType { get; set; } = string.Empty;

    [MaxLength(128)]
    public string TargetId { get; set; } = string.Empty;

    public bool Success { get; set; }

    public int? StatusCode { get; set; }

    public string DetailsJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
