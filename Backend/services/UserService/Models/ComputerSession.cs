using System.ComponentModel.DataAnnotations;

namespace UserService.Models;

public class ComputerSession
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public int AuthUserId { get; set; }

    public int ComputerId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    [MaxLength(20)]
    public string Status { get; set; } = "active";

    public virtual User? User { get; set; }

    public virtual Computer? Computer { get; set; }
}
