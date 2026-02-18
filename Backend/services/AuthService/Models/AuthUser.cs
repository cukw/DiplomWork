using System.ComponentModel.DataAnnotations;

namespace AuthService.Models;

public class AuthUser
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;
    
    [MaxLength(255)]
    public string? Email { get; set; }
    
    public int? RoleId { get; set; }
    
    public DateTime? LastLogin { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual Role? Role { get; set; }
    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();
}