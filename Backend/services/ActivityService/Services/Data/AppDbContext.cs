using Microsoft.EntityFrameworkCore;
using ActivityService.Services.Models;

namespace ActivityService.Services.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Activity> Activities => Set<Activity>();
        public DbSet<Anomaly> Anomalies => Set<Anomaly>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Activity>(entity =>
            {
                entity.ToTable("activities");
                entity.Property(e => e.Details).HasColumnType("jsonb");
                entity.HasIndex(e => e.ComputerId).HasDatabaseName("idx_activities_computer_id");
                entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_activities_timestamp");
                entity.HasIndex(e => e.ActivityType).HasDatabaseName("idx_activities_activity_type");
                entity.HasIndex(e => e.IsBlocked).HasDatabaseName("idx_activities_is_blocked");
            });
            
            modelBuilder.Entity<Anomaly>(entity =>
            {
                entity.ToTable("anomalies");
                entity.HasIndex(e => e.ActivityId).HasDatabaseName("idx_anomalies_activity_id");
                entity.HasOne(e => e.Activity)
                      .WithMany(a => a.Anomalies)
                      .HasForeignKey(e => e.ActivityId)
                      .OnDelete(DeleteBehavior.Cascade)
                      .HasConstraintName("FK_anomalies_activities");
            });

            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.ToTable("activity_outbox");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).HasMaxLength(128).IsRequired();
                entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
                entity.Property(e => e.Headers).HasColumnType("jsonb");
                entity.Property(e => e.LastError).HasColumnType("text");
                entity.HasIndex(e => new { e.ProcessedAt, e.AvailableAt }).HasDatabaseName("idx_activity_outbox_pending");
                entity.HasIndex(e => e.ActivityId).HasDatabaseName("idx_activity_outbox_activity_id");
            });
        }
    }
}
