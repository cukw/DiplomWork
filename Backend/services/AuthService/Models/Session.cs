using System.ComponentModel.DataAnnotations;

namespace AuthService.Models;

public class Session
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string TokenHash { get; set; } = string.Empty;
    
    public DateTime ExpiresAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual AuthUser User { get; set; } = null!;
}