namespace Exoplanet.Shared.Entities;

// Post 1 (Real Thinking Agents): one row per iteration of an agent's thinking loop.
// Maps to exoplanet.agent_iteration.
public class AgentIteration
{
    public int Id { get; set; }
    public int IngestRunId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public int IterationNumber { get; set; }
    public string Approach { get; set; } = string.Empty;
    public string ResultSummary { get; set; } = string.Empty;
    public string CheckResult { get; set; } = string.Empty;
    public DateTime IteratedAt { get; set; }
}
