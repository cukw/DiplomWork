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
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Version).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20).HasDefaultValue("online");
            entity.Property(e => e.ConfigVersion).HasMaxLength(20);
            entity.Property(e => e.DesiredVersion).HasMaxLength(20);
            entity.HasIndex(e => e.ComputerId).IsUnique();
        });

        // Configure SyncBatch entity
        modelBuilder.Entity<Models.SyncBatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BatchId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20).HasDefaultValue("pending");
            entity.Property(e => e.RecordsCount).HasDefaultValue(0);
            entity.HasIndex(e => e.AgentId);
            entity.HasIndex(e => e.BatchId);
        });

        modelBuilder.Entity<Models.AgentPolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyVersion).IsRequired().HasMaxLength(50);
            entity.Property(e => e.BlockedReason).HasMaxLength(500);
            entity.Property(e => e.BrowsersJson).HasDefaultValue("[\"chrome\",\"edge\",\"firefox\"]");
            entity.Property(e => e.WhitelistJson).HasDefaultValue("[]");
            entity.Property(e => e.BlacklistJson).HasDefaultValue("[]");
            entity.Property(e => e.EnableWhitelist).HasDefaultValue(true);
            entity.Property(e => e.EnableBlacklist).HasDefaultValue(true);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");
            entity.HasIndex(e => e.AgentId).IsUnique();
            entity.HasIndex(e => e.ComputerId);
        });

        modelBuilder.Entity<Models.AgentPolicyVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyVersion).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ChangeType).IsRequired().HasMaxLength(20).HasDefaultValue("update");
            entity.Property(e => e.ChangedBy).HasMaxLength(100).HasDefaultValue("system");
            entity.Property(e => e.SnapshotJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
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
