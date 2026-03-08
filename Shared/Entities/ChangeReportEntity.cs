namespace Exoplanet.Shared.Entities;

public class ChangeReportEntity
{
    public int Id { get; set; }
    public int IngestRunId { get; set; }
    public string ModelUsed { get; set; } = null!;
    public string PromptSent { get; set; } = null!;
    public string ReportText { get; set; } = null!;
    public int? TokensUsed { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }

    // Navigation
    public IngestRunEntity IngestRun { get; set; } = null!;
}
