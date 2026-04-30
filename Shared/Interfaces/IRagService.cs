namespace Exoplanet.Shared.Interfaces;

public interface IRagIngestionService
{
    Task IngestReferencesAsync();
}

public interface IRagRetrievalService
{
    Task<List<RetrievedReference>> RetrieveAsync(string planetName, string planetDescription, int? ingestRunId = null);
}

public class RetrievedReference
{
    public int ReferenceId { get; set; }
    public string ReferenceName { get; set; } = null!;
    public string Content { get; set; } = null!;
    public double SimilarityScore { get; set; }
}
