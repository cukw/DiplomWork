using Microsoft.EntityFrameworkCore;
using UserService.Models;

namespace UserService.Data;

public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Computer> Computers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).HasMaxLength(255);
            entity.Property(e => e.Department).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.AuthUserId).IsUnique();
            
            // One-to-one relationship with Computer
            entity.HasOne(e => e.Computer)
                .WithOne(c => c.User)
                .HasForeignKey<Computer>(c => c.UserId);
        });

        // Configure Computer entity
        modelBuilder.Entity<Computer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Hostname).IsRequired().HasMaxLength(255);
            entity.Property(e => e.OsVersion).HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(15);
            entity.Property(e => e.MacAddress).HasMaxLength(17);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("active");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.Hostname);
            entity.HasIndex(e => e.MacAddress).IsUnique();
            
            // One-to-one relationship with User
            entity.HasOne(e => e.User)
                .WithOne(u => u.Computer)
                .HasForeignKey<Computer>(c => c.UserId);
        });
    }
}