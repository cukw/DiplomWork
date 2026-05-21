using Microsoft.EntityFrameworkCore;
using AgentManagementService.Models;

namespace AgentManagementService.Data;

public class AgentDbContext : DbContext
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options)
    {
    }

    public DbSet<Models.Agent> Agents { get; set; }
    public DbSet<Models.SyncBatch> SyncBatches { get; set; }
    public DbSet<Models.AgentPolicy> AgentPolicies { get; set; }
    public DbSet<Models.AgentPolicyVersion> AgentPolicyVersions { get; set; }
    public DbSet<Models.AgentCommand> AgentCommands { get; set; }
    public DbSet<Models.AgentCommandDeadLetter> AgentCommandDeadLetters { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Agent entity
        modelBuilder.Entity<Models.Agent>(entity =>
        {
            entity.ToTable("agents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ComputerId).HasColumnName("computer_id");
            entity.Property(e => e.Version).HasColumnName("version").IsRequired().HasMaxLength(20);
            entity.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(20).HasDefaultValue("online");
            entity.Property(e => e.LastHeartbeat).HasColumnName("last_heartbeat");
            entity.Property(e => e.ConfigVersion).HasColumnName("config_version").HasMaxLength(20);
            entity.Property(e => e.OfflineSince).HasColumnName("offline_since");
            entity.Property(e => e.DesiredVersion).HasColumnName("desired_version").HasMaxLength(20);
            entity.Property(e => e.DesiredVersionSetAt).HasColumnName("desired_version_set_at");
            entity.Property(e => e.LastError).HasColumnName("last_error").HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(e => e.PolicyVersion).HasColumnName("policy_version").HasMaxLength(50);
            entity.Property(e => e.SourcePlatform).HasColumnName("source_platform").HasMaxLength(50);
            entity.Property(e => e.HealthJson).HasColumnName("health_json").HasColumnType("text").HasDefaultValue("{}");
            entity.Property(e => e.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("text").HasDefaultValue("{}");
            entity.Property(e => e.CollectorStatusesJson).HasColumnName("collector_statuses_json").HasColumnType("text").HasDefaultValue("{}");
            entity.Property(e => e.QueueSize).HasColumnName("queue_size").HasDefaultValue(0);
            entity.Property(e => e.LastCollectedAt).HasColumnName("last_collected_at");
            entity.Property(e => e.LastSentAt).HasColumnName("last_sent_at");
            entity.HasIndex(e => e.ComputerId).IsUnique();
        });

        // Configure SyncBatch entity
        modelBuilder.Entity<Models.SyncBatch>(entity =>
        {
            entity.ToTable("sync_batches");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.BatchId).HasColumnName("batch_id").IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(20).HasDefaultValue("pending");
            entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
            entity.Property(e => e.RecordsCount).HasColumnName("records_count").HasDefaultValue(0);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.BatchId);
        });

        modelBuilder.Entity<Models.AgentPolicy>(entity =>
        {
            entity.ToTable("agent_policies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.ComputerId).HasColumnName("computer_id");
            entity.Property(e => e.PolicyVersion).HasColumnName("policy_version").IsRequired().HasMaxLength(50);
            entity.Property(e => e.CollectionIntervalSec).HasColumnName("collection_interval_sec");
            entity.Property(e => e.HeartbeatIntervalSec).HasColumnName("heartbeat_interval_sec");
            entity.Property(e => e.FlushIntervalSec).HasColumnName("flush_interval_sec");
            entity.Property(e => e.EnableProcessCollection).HasColumnName("enable_process_collection");
            entity.Property(e => e.EnableBrowserCollection).HasColumnName("enable_browser_collection");
            entity.Property(e => e.EnableActiveWindowCollection).HasColumnName("enable_active_window_collection");
            entity.Property(e => e.EnableIdleCollection).HasColumnName("enable_idle_collection");
            entity.Property(e => e.IdleThresholdSec).HasColumnName("idle_threshold_sec");
            entity.Property(e => e.BrowserPollIntervalSec).HasColumnName("browser_poll_interval_sec");
            entity.Property(e => e.ProcessSnapshotLimit).HasColumnName("process_snapshot_limit");
            entity.Property(e => e.HighRiskThreshold).HasColumnName("high_risk_threshold");
            entity.Property(e => e.AutoLockEnabled).HasColumnName("auto_lock_enabled");
            entity.Property(e => e.AdminBlocked).HasColumnName("admin_blocked");
            entity.Property(e => e.BlockedReason).HasColumnName("blocked_reason").HasMaxLength(500);
            entity.Property(e => e.BrowsersJson).HasColumnName("browsers_json").HasDefaultValue("[\"chrome\",\"edge\",\"firefox\"]");
            entity.Property(e => e.EnableWhitelist).HasColumnName("enable_whitelist").HasDefaultValue(true);
            entity.Property(e => e.EnableBlacklist).HasColumnName("enable_blacklist").HasDefaultValue(true);
            entity.Property(e => e.WhitelistJson).HasColumnName("whitelist_json").HasDefaultValue("[]");
            entity.Property(e => e.BlacklistJson).HasColumnName("blacklist_json").HasDefaultValue("[]");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => e.AgentId).IsUnique();
            entity.HasIndex(e => e.ComputerId);
        });

        modelBuilder.Entity<Models.AgentPolicyVersion>(entity =>
        {
            entity.ToTable("agent_policy_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.PolicyVersion).HasColumnName("policy_version").IsRequired().HasMaxLength(50);
            entity.Property(e => e.ChangeType).HasColumnName("change_type").IsRequired().HasMaxLength(20).HasDefaultValue("update");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by").HasMaxLength(100).HasDefaultValue("system");
            entity.Property(e => e.SnapshotJson).HasColumnName("snapshot_json").IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => new { e.AgentId, e.CreatedAt });
        });

        modelBuilder.Entity<Models.AgentCommand>(entity =>
        {
            entity.ToTable("agent_commands");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.CommandKey).HasColumnName("command_key").IsRequired().HasMaxLength(100);
            entity.Property(e => e.Type).HasColumnName("type").IsRequired().HasMaxLength(50);
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json").HasDefaultValue("{}");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(20).HasDefaultValue("pending");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by").HasMaxLength(100).HasDefaultValue("system");
            entity.Property(e => e.ResultMessage).HasColumnName("result_message").HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(e => e.DeliveryAttempts).HasColumnName("delivery_attempts").HasDefaultValue(0);
            entity.Property(e => e.MaxDeliveryAttempts).HasColumnName("max_delivery_attempts").HasDefaultValue(5);
            entity.Property(e => e.LastDispatchAt).HasColumnName("last_dispatch_at");
            entity.Property(e => e.NextRetryAt).HasColumnName("next_retry_at");
            entity.Property(e => e.TimeoutAt).HasColumnName("timeout_at");
            entity.Property(e => e.DeadLetterReason).HasColumnName("dead_letter_reason").HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            entity.Property(e => e.AcknowledgedAt).HasColumnName("acknowledged_at");
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.AgentId, e.Status });
            entity.HasIndex(e => new { e.AgentId, e.CommandKey }).IsUnique();
            entity.HasIndex(e => e.TimeoutAt);
            entity.HasIndex(e => e.NextRetryAt);
        });

        modelBuilder.Entity<Models.AgentCommandDeadLetter>(entity =>
        {
            entity.ToTable("agent_command_dlq");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AgentCommandId).HasColumnName("agent_command_id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.CommandKey).HasColumnName("command_key").IsRequired().HasMaxLength(100);
            entity.Property(e => e.Type).HasColumnName("type").IsRequired().HasMaxLength(50);
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json").HasDefaultValue("{}");
            entity.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(e => e.DeliveryAttempts).HasColumnName("delivery_attempts").HasDefaultValue(0);
            entity.Property(e => e.FailedAt).HasColumnName("failed_at").HasDefaultValueSql("NOW()");
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.AgentCommandId).IsUnique();
            entity.HasIndex(e => e.FailedAt);
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings__DefaultConnection is required for AgentDbContext.");
            }

            optionsBuilder.UseNpgsql(connectionString);
        }
    }
}
