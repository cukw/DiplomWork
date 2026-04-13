using Microsoft.EntityFrameworkCore;
using ReportService.Models;

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
            entity.ToTable("daily_reports");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ReportDate).HasColumnName("report_date").HasColumnType("date");
            entity.Property(e => e.ComputerId).HasColumnName("computer_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.TotalActivities).HasColumnName("total_activities").HasDefaultValue(0);
            entity.Property(e => e.BlockedActions).HasColumnName("blocked_actions").HasDefaultValue(0);
            entity.Property(e => e.AvgRiskScore).HasColumnName("avg_risk_score").HasColumnType("numeric(5,2)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.ReportDate).HasDatabaseName("idx_daily_reports_date");
            entity.HasIndex(e => e.ComputerId).HasDatabaseName("idx_daily_reports_computer_id");
            entity.HasIndex(e => new { e.ReportDate, e.ComputerId })
                .IsUnique()
                .HasDatabaseName("uq_daily_reports_report_date_computer_id");
        });

        // Configure UserStats entity
        modelBuilder.Entity<Models.UserStats>(entity =>
        {
            entity.ToTable("user_stats");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.TotalTimeMs).HasColumnName("total_time_ms");
            entity.Property(e => e.RiskySites).HasColumnName("risky_sites").HasColumnType("jsonb");
            entity.Property(e => e.Violations).HasColumnName("violations").HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_user_stats_user_id");
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings__DefaultConnection is required for ReportDbContext.");
            }

            optionsBuilder.UseNpgsql(connectionString);
        }
    }
}
