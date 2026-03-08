namespace Exoplanet.Shared.Entities;

public class ChangeLogEntity
{
    public long Id { get; set; }
    public int IngestRunId { get; set; }
    public string PlanetName { get; set; } = null!;
    public string ChangeType { get; set; } = null!;  // INSERT, UPDATE, DELETE
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTimeOffset DetectedAt { get; set; }

    // Navigation
    public IngestRunEntity IngestRun { get; set; } = null!;
}
