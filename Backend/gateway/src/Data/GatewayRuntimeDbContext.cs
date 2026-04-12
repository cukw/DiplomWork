using Gateway.Models;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Data;

public sealed class GatewayRuntimeDbContext : DbContext
{
    public GatewayRuntimeDbContext(DbContextOptions<GatewayRuntimeDbContext> options) : base(options)
    {
    }

    public DbSet<AppSettingsDocumentEntity> AppSettingsDocuments => Set<AppSettingsDocumentEntity>();
    public DbSet<AlertRuleEntity> AlertRules => Set<AlertRuleEntity>();
    public DbSet<AdminAuditEventEntity> AdminAuditEvents => Set<AdminAuditEventEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppSettingsDocumentEntity>(entity =>
        {
            entity.ToTable("app_settings_documents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<AlertRuleEntity>(entity =>
        {
            entity.ToTable("alert_rules");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
            entity.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(32).IsRequired();
            entity.Property(x => x.Metric).HasColumnName("metric").HasMaxLength(64).IsRequired();
            entity.Property(x => x.Operator).HasColumnName("operator").HasMaxLength(16).IsRequired();
            entity.Property(x => x.Threshold).HasColumnName("threshold").HasPrecision(18, 4).IsRequired();
            entity.Property(x => x.WindowMinutes).HasColumnName("window_minutes").IsRequired();
            entity.Property(x => x.ActivityType).HasColumnName("activity_type").HasMaxLength(64);
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ComputerId).HasColumnName("computer_id");
            entity.Property(x => x.NotifyInApp).HasColumnName("notify_in_app").IsRequired();
            entity.Property(x => x.NotifyEmail).HasColumnName("notify_email").IsRequired();
            entity.Property(x => x.CooldownMinutes).HasColumnName("cooldown_minutes").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<AdminAuditEventEntity>(entity =>
        {
            entity.ToTable("admin_audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(128).IsRequired();
            entity.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetId).HasColumnName("target_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Success).HasColumnName("success").IsRequired();
            entity.Property(x => x.StatusCode).HasColumnName("status_code");
            entity.Property(x => x.DetailsJson).HasColumnName("details_json").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_admin_audit_events_created_at");
            entity.HasIndex(x => x.Action).HasDatabaseName("idx_admin_audit_events_action");
            entity.HasIndex(x => x.Actor).HasDatabaseName("idx_admin_audit_events_actor");
            entity.HasIndex(x => new { x.TargetType, x.TargetId }).HasDatabaseName("idx_admin_audit_events_target");
        });

        modelBuilder.Entity<RolePermissionEntity>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RoleName).HasColumnName("role_name").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Permission).HasColumnName("permission").HasMaxLength(256).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(x => x.RoleName).HasDatabaseName("idx_role_permissions_role_name");
            entity.HasIndex(x => x.Permission).HasDatabaseName("idx_role_permissions_permission");
            entity.HasIndex(x => new { x.RoleName, x.Permission })
                .IsUnique()
                .HasDatabaseName("uq_role_permissions_role_permission");
        });
    }
}
