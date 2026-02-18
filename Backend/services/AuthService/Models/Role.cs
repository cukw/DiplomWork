using System.ComponentModel.DataAnnotations;

namespace AuthService.Models;

public class Role
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public virtual ICollection<AuthUser> AuthUsers { get; set; } = new List<AuthUser>();
}