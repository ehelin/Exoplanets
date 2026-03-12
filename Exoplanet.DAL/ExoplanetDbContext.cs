using Microsoft.EntityFrameworkCore;
using Exoplanet.Shared.Entities;

namespace Exoplanet.DAL;

public sealed class ExoplanetDbContext : DbContext
{
    public ExoplanetDbContext(DbContextOptions<ExoplanetDbContext> options)
        : base(options) { }

    public DbSet<ExoplanetEntity> Exoplanets => Set<ExoplanetEntity>();
    public DbSet<IngestRunEntity> IngestRuns => Set<IngestRunEntity>();
    public DbSet<ChangeLogEntity> ChangeLogs => Set<ChangeLogEntity>();
    public DbSet<ChangeReportEntity> ChangeReports => Set<ChangeReportEntity>();
    public DbSet<PipelineLogEntity> PipelineLogs => Set<PipelineLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        const string schema = "exoplanet";

        // ── Exoplanets (existing) ──────────────────────────────
        modelBuilder.Entity<ExoplanetEntity>(e =>
        {
            e.ToTable("exoplanets", schema);

            e.HasKey(x => x.ExoplanetId);

            e.Property(x => x.ExoplanetId)
                .HasColumnName("exoplanet_id");

            e.Property(x => x.PlanetName)
                .HasColumnName("planet_name")
                .HasMaxLength(256)
                .IsRequired();

            e.Property(x => x.HostStar)
                .HasColumnName("host_star")
                .HasMaxLength(256)
                .IsRequired();

            e.Property(x => x.DiscoveryYear)
                .HasColumnName("discovery_year");

            e.Property(x => x.CreatedUtc)
                .HasColumnName("created_utc");

            e.Property(x => x.UpdatedUtc)
                .HasColumnName("updated_utc");

            e.Property(x => x.Classification)
                .HasColumnName("classification")
                .HasMaxLength(50);


            // Natural key = dedup definition
            e.HasIndex(x => new { x.PlanetName, x.HostStar })
                .IsUnique();
        });

        // ── IngestRun (Phase 2) ────────────────────────────────
        modelBuilder.Entity<IngestRunEntity>(e =>
        {
            e.ToTable("ingest_run", schema);

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.RunTimestamp)
                .HasColumnName("run_timestamp")
                .HasDefaultValueSql("NOW()");

            e.Property(x => x.Source)
                .HasColumnName("source")
                .HasMaxLength(255)
                .IsRequired();

            e.Property(x => x.SourceUrl)
                .HasColumnName("source_url");

            e.Property(x => x.RowsFetched)
                .HasColumnName("rows_fetched");

            e.Property(x => x.RowsNew)
                .HasColumnName("rows_new");

            e.Property(x => x.RowsUpdated)
                .HasColumnName("rows_updated");

            e.Property(x => x.RowsDeleted)
                .HasColumnName("rows_deleted");

            e.Property(x => x.RowsUnchanged)
                .HasColumnName("rows_unchanged");

            e.Property(x => x.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .HasDefaultValue("RUNNING");

            e.Property(x => x.ErrorMessage)
                .HasColumnName("error_message");

            e.Property(x => x.CompletedAt)
                .HasColumnName("completed_at");
        });

        // ── ChangeLog (Phase 2) ────────────────────────────────
        modelBuilder.Entity<ChangeLogEntity>(e =>
        {
            e.ToTable("change_log", schema);

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.IngestRunId)
                .HasColumnName("ingest_run_id");

            e.Property(x => x.PlanetName)
                .HasColumnName("planet_name")
                .HasMaxLength(255)
                .IsRequired();

            e.Property(x => x.ChangeType)
                .HasColumnName("change_type")
                .HasMaxLength(10)
                .IsRequired();

            e.Property(x => x.FieldName)
                .HasColumnName("field_name")
                .HasMaxLength(255);

            e.Property(x => x.OldValue)
                .HasColumnName("old_value");

            e.Property(x => x.NewValue)
                .HasColumnName("new_value");

            e.Property(x => x.DetectedAt)
                .HasColumnName("detected_at")
                .HasDefaultValueSql("NOW()");

            e.Property(x => x.AiClassification)
             .HasColumnName("ai_classification")
             .HasMaxLength(20);

            e.Property(x => x.AiReasoning)
                .HasColumnName("ai_reasoning");

            e.HasOne(x => x.IngestRun)
                .WithMany()
                .HasForeignKey(x => x.IngestRunId);
        });

        // ── ChangeReport (Phase 2.3 — table ready, AI call later) ──
        modelBuilder.Entity<ChangeReportEntity>(e =>
        {
            e.ToTable("change_report", schema);

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.IngestRunId)
                .HasColumnName("ingest_run_id");

            e.Property(x => x.ModelUsed)
                .HasColumnName("model_used")
                .HasMaxLength(100)
                .IsRequired();

            e.Property(x => x.PromptSent)
                .HasColumnName("prompt_sent")
                .IsRequired();

            e.Property(x => x.ReportText)
                .HasColumnName("report_text")
                .IsRequired();

            e.Property(x => x.TokensUsed)
                .HasColumnName("tokens_used");

            e.Property(x => x.GeneratedAt)
                .HasColumnName("generated_at")
                .HasDefaultValueSql("NOW()");

            e.HasOne(x => x.IngestRun)
                .WithMany()
                .HasForeignKey(x => x.IngestRunId);
        });

        modelBuilder.Entity<PipelineLogEntity>(e =>
        {
            e.ToTable("pipeline_log", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.LogLevel).HasColumnName("log_level").HasMaxLength(20).IsRequired();
            e.Property(x => x.Message).HasColumnName("message").IsRequired();
            e.Property(x => x.Exception).HasColumnName("exception");
            e.Property(x => x.LoggedAt).HasColumnName("logged_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId).IsRequired(false);
        });
    }
}
