namespace Exoplanet.Shared.Entities;

public class PipelineLogEntity
{
    public long Id { get; set; }
    public int? IngestRunId { get; set; }
    public string LogLevel { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? Exception { get; set; }
    public DateTimeOffset LoggedAt { get; set; }

    public IngestRunEntity? IngestRun { get; set; }
}