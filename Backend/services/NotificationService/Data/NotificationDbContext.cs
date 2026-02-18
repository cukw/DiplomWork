using Microsoft.EntityFrameworkCore;
using NotificationService.Models;

namespace NotificationService.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options)
    {
    }

    public DbSet<Notification> Notifications { get; set; }
    public DbSet<NotificationTemplate> NotificationTemplates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Notification entity
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.Channel).HasMaxLength(20).HasDefaultValue("email");
            entity.Property(e => e.IsRead).HasDefaultValue(false);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsRead);
        });

        // Configure NotificationTemplate entity
        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Subject).HasMaxLength(255);
            entity.HasIndex(e => e.Type).IsUnique();
        });

        // Seed default notification templates
        modelBuilder.Entity<NotificationTemplate>().HasData(
            new NotificationTemplate 
            { 
                Id = 1, 
                Type = "anomaly", 
                Subject = "Activity Anomaly Detected", 
                BodyTemplate = "An anomaly has been detected in user activity. Please review the details and take appropriate action." 
            },
            new NotificationTemplate 
            { 
                Id = 2, 
                Type = "report_ready", 
                Subject = "Activity Report Ready", 
                BodyTemplate = "Your activity report is ready for download. Please check your dashboard to access the report." 
            },
            new NotificationTemplate 
            { 
                Id = 3, 
                Type = "system_alert", 
                Subject = "System Alert", 
                BodyTemplate = "A system alert has been generated. Please review the system status and take necessary actions." 
            }
        );
    }
}