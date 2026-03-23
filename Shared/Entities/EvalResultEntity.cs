namespace Exoplanet.Shared.Entities;

public class EvalResultEntity
{
    public int Id { get; set; }
    public int IngestRunId { get; set; }
    public string EvalType { get; set; } = null!;       // CLASSIFICATION, HABITABILITY
    public string PlanetName { get; set; } = null!;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public int? Score { get; set; }
    public string? Dimension { get; set; }              // accuracy, completeness, consistency
    public string? PassFail { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }

    public IngestRunEntity IngestRun { get; set; } = null!;
}
