using Microsoft.EntityFrameworkCore;
using Exoplanet.Shared.Entities;

namespace Exoplanet.DAL;

public sealed class ExoplanetDbContext : DbContext
{
    public ExoplanetDbContext(DbContextOptions<ExoplanetDbContext> options)
        : base(options) { }

    // Domain
    public DbSet<SolarSystemEntity> SolarSystems => Set<SolarSystemEntity>();
    public DbSet<StarEntity> Stars => Set<StarEntity>();
    public DbSet<PlanetEntity> Planets => Set<PlanetEntity>();
    public DbSet<PlanetStarEntity> PlanetStars => Set<PlanetStarEntity>();
    public DbSet<AtmosphereEntity> Atmospheres => Set<AtmosphereEntity>();

    // Audit & Evidence
    public DbSet<IngestRunEntity> IngestRuns => Set<IngestRunEntity>();
    public DbSet<ChangeLogEntity> ChangeLogs => Set<ChangeLogEntity>();
    public DbSet<ChangeReportEntity> ChangeReports => Set<ChangeReportEntity>();
    public DbSet<PipelineLogEntity> PipelineLogs => Set<PipelineLogEntity>();
    public DbSet<EvalResultEntity> EvalResults => Set<EvalResultEntity>();

    public DbSet<RetrievalLogEntity> RetrievalLogs => Set<RetrievalLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        const string schema = "exoplanet";

        // ── SolarSystems ───────────────────────────────────────
        modelBuilder.Entity<SolarSystemEntity>(e =>
        {
            e.ToTable("solar_systems", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DistanceParsecs).HasColumnName("distance_parsecs");
            e.Property(x => x.NumStars).HasColumnName("num_stars");
            e.Property(x => x.NumPlanets).HasColumnName("num_planets");
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        // ── Stars ──────────────────────────────────────────────
        modelBuilder.Entity<StarEntity>(e =>
        {
            e.ToTable("stars", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SolarSystemId).HasColumnName("solar_system_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            e.Property(x => x.TemperatureK).HasColumnName("temperature_k");
            e.Property(x => x.RadiusSolar).HasColumnName("radius_solar");
            e.Property(x => x.MassSolar).HasColumnName("mass_solar");
            e.Property(x => x.SpectralType).HasColumnName("spectral_type").HasMaxLength(50);
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            e.HasOne(x => x.SolarSystem).WithMany().HasForeignKey(x => x.SolarSystemId);
        });

        // ── Planets ────────────────────────────────────────────
        modelBuilder.Entity<PlanetEntity>(e =>
        {
            e.ToTable("planets", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SolarSystemId).HasColumnName("solar_system_id");
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(256).IsRequired();
            e.Property(x => x.DiscoveryYear).HasColumnName("discovery_year");
            e.Property(x => x.DiscoveryMethod).HasColumnName("discovery_method").HasMaxLength(100);
            e.Property(x => x.PlanetRadius).HasColumnName("planet_radius");
            e.Property(x => x.PlanetMass).HasColumnName("planet_mass");
            e.Property(x => x.OrbitalPeriod).HasColumnName("orbital_period");
            e.Property(x => x.SemiMajorAxis).HasColumnName("semi_major_axis");
            e.Property(x => x.Eccentricity).HasColumnName("eccentricity");
            e.Property(x => x.EquilibriumTemp).HasColumnName("equilibrium_temp");
            e.Property(x => x.PlanetDensity).HasColumnName("planet_density");
            e.Property(x => x.InsolationFlux).HasColumnName("insolation_flux");
            e.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(50);
            e.Property(x => x.PlavalovaCode).HasColumnName("plavalova_code").HasMaxLength(20);
            e.Property(x => x.HabitabilityScore).HasColumnName("habitability_score").HasMaxLength(50);
            e.Property(x => x.ScientificNote).HasColumnName("scientific_note");
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            e.HasOne(x => x.SolarSystem).WithMany().HasForeignKey(x => x.SolarSystemId);
            e.HasIndex(x => x.PlanetName).IsUnique();
        });

        // ── PlanetStars (join) ─────────────────────────────────
        modelBuilder.Entity<PlanetStarEntity>(e =>
        {
            e.ToTable("planet_stars", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.StarId).HasColumnName("star_id");
            e.HasOne(x => x.Planet).WithMany().HasForeignKey(x => x.PlanetId);
            e.HasOne(x => x.Star).WithMany().HasForeignKey(x => x.StarId);
            e.HasIndex(x => new { x.PlanetId, x.StarId }).IsUnique();
        });

        // ── Atmospheres ────────────────────────────────────────
        modelBuilder.Entity<AtmosphereEntity>(e =>
        {
            e.ToTable("atmospheres", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.Molecule).HasColumnName("molecule").HasMaxLength(100).IsRequired();
            e.Property(x => x.DetectionType).HasColumnName("detection_type").HasMaxLength(100);
            e.Property(x => x.SpectralReference).HasColumnName("spectral_reference");
            e.Property(x => x.HabitabilityScore).HasColumnName("habitability_score");
            e.Property(x => x.HabitabilityReasoning).HasColumnName("habitability_reasoning");
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            e.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            e.HasOne(x => x.Planet).WithMany().HasForeignKey(x => x.PlanetId);
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId).IsRequired(false);
        });

        // ── IngestRun ──────────────────────────────────────────
        modelBuilder.Entity<IngestRunEntity>(e =>
        {
            e.ToTable("ingest_run", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RunTimestamp).HasColumnName("run_timestamp").HasDefaultValueSql("NOW()");
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(255).IsRequired();
            e.Property(x => x.SourceUrl).HasColumnName("source_url");
            e.Property(x => x.RowsFetched).HasColumnName("rows_fetched");
            e.Property(x => x.RowsNew).HasColumnName("rows_new");
            e.Property(x => x.RowsUpdated).HasColumnName("rows_updated");
            e.Property(x => x.RowsDeleted).HasColumnName("rows_deleted");
            e.Property(x => x.RowsUnchanged).HasColumnName("rows_unchanged");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).HasDefaultValue("RUNNING");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
        });

        // ── ChangeLog ──────────────────────────────────────────
        modelBuilder.Entity<ChangeLogEntity>(e =>
        {
            e.ToTable("change_log", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ChangeType).HasColumnName("change_type").HasMaxLength(10).IsRequired();
            e.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(255);
            e.Property(x => x.OldValue).HasColumnName("old_value");
            e.Property(x => x.NewValue).HasColumnName("new_value");
            e.Property(x => x.AiClassification).HasColumnName("ai_classification").HasMaxLength(20);
            e.Property(x => x.AiReasoning).HasColumnName("ai_reasoning");
            e.Property(x => x.DetectedAt).HasColumnName("detected_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId);
        });

        // ── ChangeReport ───────────────────────────────────────
        modelBuilder.Entity<ChangeReportEntity>(e =>
        {
            e.ToTable("change_report", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.ModelUsed).HasColumnName("model_used").HasMaxLength(100).IsRequired();
            e.Property(x => x.PromptSent).HasColumnName("prompt_sent").IsRequired();
            e.Property(x => x.ReportText).HasColumnName("report_text").IsRequired();
            e.Property(x => x.TokensUsed).HasColumnName("tokens_used");
            e.Property(x => x.GeneratedAt).HasColumnName("generated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId);
        });

        // ── PipelineLog ────────────────────────────────────────
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

        // ── EvalResult ─────────────────────────────────────────
        modelBuilder.Entity<EvalResultEntity>(e =>
        {
            e.ToTable("eval_result", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.EvalType).HasColumnName("eval_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ExpectedValue).HasColumnName("expected_value");
            e.Property(x => x.ActualValue).HasColumnName("actual_value");
            e.Property(x => x.Score).HasColumnName("score");
            e.Property(x => x.Dimension).HasColumnName("dimension").HasMaxLength(50);
            e.Property(x => x.PassFail).HasColumnName("pass_fail").HasMaxLength(10);
            e.Property(x => x.EvaluatedAt).HasColumnName("evaluated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId);
        });

        // ── RetrievalLog ───────────────────────────────────────
        modelBuilder.Entity<RetrievalLogEntity>(e =>
        {
            e.ToTable("retrieval_log", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IngestRunId).HasColumnName("ingest_run_id");
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ReferenceId).HasColumnName("reference_id");
            e.Property(x => x.ReferenceName).HasColumnName("reference_name");
            e.Property(x => x.SimilarityScore).HasColumnName("similarity_score");
            e.Property(x => x.WasReferenced).HasColumnName("was_referenced");
            e.Property(x => x.RetrievedAt).HasColumnName("retrieved_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.IngestRun).WithMany().HasForeignKey(x => x.IngestRunId).IsRequired(false);
        });
    }
}
