namespace Exoplanet.Shared.Entities;

public class RetrievalLogEntity
{
    public int Id { get; set; }
    public int? IngestRunId { get; set; }
    public string PlanetName { get; set; } = null!;
    public int ReferenceId { get; set; }
    public string? ReferenceName { get; set; }
    public double? SimilarityScore { get; set; }
    public bool WasReferenced { get; set; }
    public DateTimeOffset RetrievedAt { get; set; }

    public IngestRunEntity? IngestRun { get; set; }
}
