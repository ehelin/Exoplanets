namespace Exoplanet.Shared.Entities;

public class AtmosphereEntity
{
    public int Id { get; set; }
    public int PlanetId { get; set; }
    public int? IngestRunId { get; set; }
    public string Molecule { get; set; } = null!;
    public string? DetectionType { get; set; }
    public string? SpectralReference { get; set; }
    public double? HabitabilityScore { get; set; }
    public string? HabitabilityReasoning { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public PlanetEntity Planet { get; set; } = null!;
    public IngestRunEntity? IngestRun { get; set; }
}
