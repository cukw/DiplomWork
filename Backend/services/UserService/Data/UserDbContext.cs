using System.Net;
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
    public DbSet<ComputerSession> ComputerSessions { get; set; }

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
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired(false);
            entity.Property(e => e.Hostname).HasColumnName("hostname").IsRequired().HasMaxLength(255);
            entity.Property(e => e.OsVersion).HasColumnName("os_version").HasMaxLength(100);
            entity.Property(e => e.IpAddress)
                .HasColumnName("ip_address")
                .HasColumnType("inet")
                .HasConversion(
                    value => ToNullableIpAddress(value),
                    value => value == null ? null : value.ToString());
            entity.Property(e => e.MacAddress).HasColumnName("mac_address").HasMaxLength(17);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");
            entity.Property(e => e.LastSeen).HasColumnName("last_seen");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_computers_user_id");
            entity.HasIndex(e => e.Hostname).HasDatabaseName("idx_computers_hostname");
            entity.HasIndex(e => e.MacAddress).IsUnique().HasDatabaseName("computers_mac_address_key");
            
            // user_id stores the current active user while a computer session is open.
            entity.HasOne(e => e.User)
                .WithMany(u => u.Computers)
                .HasForeignKey(c => c.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ComputerSession>(entity =>
        {
            entity.ToTable("computer_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.AuthUserId).HasColumnName("auth_user_id").IsRequired();
            entity.Property(e => e.ComputerId).HasColumnName("computer_id").IsRequired();
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(e => e.EndedAt).HasColumnName("ended_at");
            entity.Property(e => e.LastSeen).HasColumnName("last_seen").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_computer_sessions_user_id");
            entity.HasIndex(e => e.ComputerId).HasDatabaseName("idx_computer_sessions_computer_id");
            entity.HasIndex(e => new { e.UserId, e.EndedAt }).HasDatabaseName("idx_computer_sessions_user_active");
            entity.HasIndex(e => new { e.ComputerId, e.EndedAt }).HasDatabaseName("idx_computer_sessions_computer_active");

            entity.HasOne(e => e.User)
                .WithMany(u => u.ComputerSessions)
                .HasForeignKey(e => e.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Computer)
                .WithMany(c => c.Sessions)
                .HasForeignKey(e => e.ComputerId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static IPAddress? ToNullableIpAddress(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return IPAddress.TryParse(normalized, out var parsed) ? parsed : null;
    }
}
