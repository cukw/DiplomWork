using System.ComponentModel.DataAnnotations;

namespace UserService.Models;

public class User
{
    public int Id { get; set; }
    
    public int? AuthUserId { get; set; }
    
    [MaxLength(255)]
    public string? FullName { get; set; }
    
    [MaxLength(100)]
    public string? Department { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual Computer? Computer { get; set; }
}