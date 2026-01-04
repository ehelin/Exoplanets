using Microsoft.EntityFrameworkCore;

namespace Exoplanet.DAL;

public sealed class ExoplanetDbContext : DbContext
{
    public ExoplanetDbContext(DbContextOptions<ExoplanetDbContext> options) : base(options) { }

    public DbSet<ExoplanetRaw> ExoplanetRaw => Set<ExoplanetRaw>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExoplanetRaw>(e =>
        {
            e.ToTable("exoplanet_raw");

            e.HasKey(x => x.Id);

            e.Property(x => x.PlName).HasColumnName("pl_name").HasMaxLength(256);
            e.Property(x => x.Hostname).HasColumnName("hostname").HasMaxLength(256);
            e.Property(x => x.DiscYear).HasColumnName("disc_year");
            e.Property(x => x.RowHash).HasColumnName("row_hash").HasMaxLength(64);
            e.Property(x => x.IngestedAtUtc).HasColumnName("ingested_at_utc");

            e.Property(x => x.PayloadJson)
                .HasColumnName("payload_json")
                .HasColumnType("jsonb");

            // opinionated: idempotency per planet+host (adjust later if you want a different key)
            e.HasIndex(x => new { x.PlName, x.Hostname }).IsUnique();
        });
    }
}
