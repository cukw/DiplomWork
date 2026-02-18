using Microsoft.EntityFrameworkCore;
using MetricsService.Models;
using Npgsql;

namespace MetricsService.Data;

public class MetricsDbContext : DbContext
{
    public MetricsDbContext(DbContextOptions<MetricsDbContext> options) : base(options)
    {
    }

    public DbSet<Metric> Metrics { get; set; }
    public DbSet<WhitelistEntry> WhitelistEntries { get; set; }
    public DbSet<BlacklistEntry> BlacklistEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Metric entity
        modelBuilder.Entity<Metric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Config).IsRequired();
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.UserId);
            
            // Configure JSON column for PostgreSQL
            entity.Property(e => e.Config)
                .HasColumnType("jsonb");
        });

        // Configure WhitelistEntry entity
        modelBuilder.Entity<WhitelistEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Pattern).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Action).HasMaxLength(20).HasDefaultValue("allow");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.MetricId);
            
            entity.HasOne(e => e.Metric)
                .WithMany(m => m.WhitelistEntries)
                .HasForeignKey(e => e.MetricId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure BlacklistEntry entity
        modelBuilder.Entity<BlacklistEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Pattern).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Action).HasMaxLength(20).HasDefaultValue("block");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.MetricId);
            
            entity.HasOne(e => e.Metric)
                .WithMany(m => m.BlacklistEntries)
                .HasForeignKey(e => e.MetricId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=metrics;Username=postgres;Password=pass;TrustServerCertificate=true");
        }
    }
}