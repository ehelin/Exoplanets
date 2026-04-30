using Exoplanet.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace Exoplanet.DAL;

public sealed class VectorDbContext : DbContext
{
    public VectorDbContext(DbContextOptions<VectorDbContext> options)
        : base(options) { }

    public DbSet<ExoplanetReferenceEntity> ExoplanetReferences => Set<ExoplanetReferenceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExoplanetReferenceEntity>(e =>
        {
            e.ToTable("exoplanet_reference");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetName).HasColumnName("planet_name").HasMaxLength(255).IsRequired();
            e.Property(x => x.ReferenceName).HasColumnName("reference_name").IsRequired();
            e.Property(x => x.PubDate).HasColumnName("pub_date").HasMaxLength(50);
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.CreatedUtc).HasColumnName("created_utc");

            // pgvector column — stored as float[] in C#, mapped via raw SQL for queries
            e.Ignore(x => x.Embedding);
        });
    }
}
