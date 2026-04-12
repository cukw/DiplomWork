using Microsoft.EntityFrameworkCore;
using NotificationEntity = NotificationService.Models.Notification;
using NotificationTemplateEntity = NotificationService.Models.NotificationTemplate;
using ProcessedEventInboxEntryEntity = NotificationService.Models.ProcessedEventInboxEntry;
using NotificationDeliveryDeadLetterEntity = NotificationService.Models.NotificationDeliveryDeadLetter;

namespace NotificationService.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options)
    {
    }

    public DbSet<NotificationEntity> Notifications { get; set; }
    public DbSet<NotificationTemplateEntity> NotificationTemplates { get; set; }
    public DbSet<ProcessedEventInboxEntryEntity> ProcessedEventInboxEntries { get; set; }
    public DbSet<NotificationDeliveryDeadLetterEntity> NotificationDeliveryDeadLetters { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Указываем явные имена таблиц (в нижнем регистре как в SQL)
        modelBuilder.Entity<NotificationEntity>().ToTable("notifications");
        modelBuilder.Entity<NotificationTemplateEntity>().ToTable("notification_templates");

        // Configure Notification entity (явное указание всех свойств)
        modelBuilder.Entity<NotificationEntity>(entity =>
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
            entity.Property(e => e.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320);
            entity.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20).HasDefaultValue("email");
            entity.Property(e => e.DeliveryStatus).HasColumnName("delivery_status").HasMaxLength(32).HasDefaultValue("pending");
            entity.Property(e => e.DeliveryAttempts).HasColumnName("delivery_attempts").HasDefaultValue(0);
            entity.Property(e => e.MaxDeliveryAttempts).HasColumnName("max_delivery_attempts").HasDefaultValue(3);
            entity.Property(e => e.LastDeliveryError).HasColumnName("last_delivery_error");
            entity.Property(e => e.NextRetryAt).HasColumnName("next_retry_at");
            entity.Property(e => e.DeliveredAt).HasColumnName("delivered_at");
            
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_notifications_user_id");
            entity.HasIndex(e => e.IsRead).HasDatabaseName("idx_notifications_is_read");
            entity.HasIndex(e => e.RecipientEmail).HasDatabaseName("idx_notifications_recipient_email");
            entity.HasIndex(e => e.DeliveryStatus).HasDatabaseName("idx_notifications_delivery_status");
            entity.HasIndex(e => e.NextRetryAt).HasDatabaseName("idx_notifications_next_retry_at");
        });

        // Configure NotificationTemplate entity (только индексы)
        modelBuilder.Entity<NotificationTemplateEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Type).IsUnique().HasDatabaseName("uq_notification_templates_type");
        });

        modelBuilder.Entity<ProcessedEventInboxEntryEntity>(entity =>
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

        modelBuilder.Entity<NotificationDeliveryDeadLetterEntity>(entity =>
        {
            entity.ToTable("notification_delivery_dlq");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.Channel).HasColumnName("channel").HasMaxLength(20).HasDefaultValue("in_app");
            entity.Property(e => e.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320);
            entity.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entity.Property(e => e.Reason).HasColumnName("reason").HasDefaultValue(string.Empty);
            entity.Property(e => e.FailedAt).HasColumnName("failed_at");
            entity.HasIndex(e => e.NotificationId).IsUnique().HasDatabaseName("uq_notification_delivery_dlq_notification_id");
            entity.HasIndex(e => e.FailedAt).HasDatabaseName("idx_notification_delivery_dlq_failed_at");
        });

    }
}
