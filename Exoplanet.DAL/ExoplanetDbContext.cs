using Microsoft.EntityFrameworkCore;
using Exoplanet.Shared.Entities;

namespace Exoplanet.DAL;

public sealed class ExoplanetDbContext : DbContext
{
    public ExoplanetDbContext(DbContextOptions<ExoplanetDbContext> options)
        : base(options) { }

    public DbSet<ExoplanetEntity> Exoplanets => Set<ExoplanetEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExoplanetEntity>(e =>
        {
            e.ToTable("exoplanets", schema: "exoplanet");

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

            // Natural key = dedup definition
            e.HasIndex(x => new { x.PlanetName, x.HostStar })
                .IsUnique();
        });
    }
}
