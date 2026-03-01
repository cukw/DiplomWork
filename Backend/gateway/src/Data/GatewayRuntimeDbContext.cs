using Gateway.Models;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Data;

public sealed class GatewayRuntimeDbContext : DbContext
{
    public GatewayRuntimeDbContext(DbContextOptions<GatewayRuntimeDbContext> options) : base(options)
    {
    }

    public DbSet<AppSettingsDocumentEntity> AppSettingsDocuments => Set<AppSettingsDocumentEntity>();

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
    }
}
