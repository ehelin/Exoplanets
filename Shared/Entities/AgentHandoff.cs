namespace Exoplanet.Shared.Entities;

public sealed class AgentHandoff
{
    public int Id { get; set; }
    public int IngestId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string InputSummary { get; set; } = string.Empty;
    public string OutputSummary { get; set; } = string.Empty;
    public DateTime HandoffTimestamp { get; set; }
}