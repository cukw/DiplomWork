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
    public DbSet<ProcessedEventInboxEntry> ProcessedEventInboxEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Указываем явные имена таблиц (в нижнем регистре как в SQL)
        modelBuilder.Entity<Notification>().ToTable("notifications");
        modelBuilder.Entity<NotificationTemplate>().ToTable("notification_templates");

        // Configure Notification entity (явное указание всех свойств)
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(e => e.Id);
            
            // Явное указание маппинга колонок
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(50);
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(255);
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.IsRead).HasColumnName("is_read").HasDefaultValue(false);
            entity.Property(e => e.SentAt).HasColumnName("sent_at");
            entity.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20).HasDefaultValue("email");
            
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_notifications_user_id");
            entity.HasIndex(e => e.IsRead).HasDatabaseName("idx_notifications_is_read");
        });

        // Configure NotificationTemplate entity (только индексы)
        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Type).IsUnique().HasDatabaseName("uq_notification_templates_type");
        });

        modelBuilder.Entity<ProcessedEventInboxEntry>(entity =>
        {
            entity.ToTable("processed_event_inbox");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Consumer).HasColumnName("consumer").HasMaxLength(128).IsRequired();
            entity.Property(e => e.EventKey).HasColumnName("event_key").HasMaxLength(256).IsRequired();
            entity.Property(e => e.MessageId).HasColumnName("message_id").HasMaxLength(128);
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
            entity.HasIndex(e => new { e.Consumer, e.EventKey })
                .IsUnique()
                .HasDatabaseName("uq_processed_event_inbox_consumer_event_key");
            entity.HasIndex(e => e.ProcessedAt)
                .HasDatabaseName("idx_processed_event_inbox_processed_at");
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
