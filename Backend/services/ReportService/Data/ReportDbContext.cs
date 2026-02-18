using Microsoft.EntityFrameworkCore;
using ReportService.Models;
using Npgsql;

namespace ReportService.Data;

public class ReportDbContext : DbContext
{
    public ReportDbContext(DbContextOptions<ReportDbContext> options) : base(options)
    {
    }

    public DbSet<Models.DailyReport> DailyReports { get; set; }
    public DbSet<Models.UserStats> UserStats { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure DailyReport entity
        modelBuilder.Entity<Models.DailyReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReportDate).HasColumnType("date");
            entity.Property(e => e.TotalActivities).HasDefaultValue(0);
            entity.Property(e => e.BlockedActions).HasDefaultValue(0);
            entity.Property(e => e.AvgRiskScore).HasColumnType("numeric(5,2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.ReportDate);
            entity.HasIndex(e => e.ComputerId);
        });

        // Configure UserStats entity
        modelBuilder.Entity<Models.UserStats>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TotalTimeMs);
            entity.Property(e => e.RiskySites).HasColumnType("jsonb");
            entity.Property(e => e.Violations).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.UserId);
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=reports;Username=postgres;Password=pass;TrustServerCertificate=true");
        }
    }
}