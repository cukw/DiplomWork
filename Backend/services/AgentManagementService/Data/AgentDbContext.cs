using Microsoft.EntityFrameworkCore;
using AgentManagementService.Models;
using Npgsql;

namespace AgentManagementService.Data;

public class AgentDbContext : DbContext
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options)
    {
    }

    public DbSet<Models.Agent> Agents { get; set; }
    public DbSet<Models.SyncBatch> SyncBatches { get; set; }

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
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=agents;Username=postgres;Password=pass;TrustServerCertificate=true");
        }
    }
}