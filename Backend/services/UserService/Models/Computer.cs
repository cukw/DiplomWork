using System.ComponentModel.DataAnnotations;
using System.Net;

namespace UserService.Models;

public class Computer
{
    public int Id { get; set; }
    
    public int? UserId { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Hostname { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string? OsVersion { get; set; }
    
    [MaxLength(15)]
    public string? IpAddress { get; set; }
    
    [MaxLength(17)]
    public string? MacAddress { get; set; }
    
    [MaxLength(20)]
    public string Status { get; set; } = "active"; // active / disabled / retired
    
    public DateTime? LastSeen { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual User? User { get; set; }
}