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
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AuthUserId).HasColumnName("auth_user_id");
            entity.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(255);
            entity.Property(e => e.Department).HasColumnName("department").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.AuthUserId).IsUnique().HasDatabaseName("users_auth_user_id_key");
        });

        // Configure Computer entity
        modelBuilder.Entity<Computer>(entity =>
        {
            entity.ToTable("computers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.Hostname).HasColumnName("hostname").IsRequired().HasMaxLength(255);
            entity.Property(e => e.OsVersion).HasColumnName("os_version").HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(15);
            entity.Property(e => e.MacAddress).HasColumnName("mac_address").HasMaxLength(17);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");
            entity.Property(e => e.LastSeen).HasColumnName("last_seen");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("uq_computers_user_id");
            entity.HasIndex(e => e.Hostname).HasDatabaseName("idx_computers_hostname");
            entity.HasIndex(e => e.MacAddress).IsUnique().HasDatabaseName("computers_mac_address_key");
            
            // One-to-one relationship with User
            entity.HasOne(e => e.User)
                .WithOne(u => u.Computer)
                .HasForeignKey<Computer>(c => c.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
