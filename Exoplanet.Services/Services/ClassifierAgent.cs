using System.Text.Json;
using Exoplanet.Shared.Interfaces;
using Shared.Interfaces;

namespace Exoplanet.Services.Services;

public sealed class ClassifierAgent : IAgent
{
    private readonly IChangeClassifierService _classifier;
    private readonly IAgentBoundary _boundary;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;

    public string AgentName => "classifier";

    public ClassifierAgent(
        IChangeClassifierService classifier,
        IAgentBoundary boundary,
        IExoplanetRepository repo,
        IPipelineLogger plog)
    {
        _classifier = classifier;
        _boundary = boundary;
        _repo = repo;
        _plog = plog;
    }

    public async Task ExecuteAsync(int ingestRunId)
    {
        // What the agent received
        var changes = await _repo.GetChangeLogByRunAsync(ingestRunId);
        var inputSummary = $"{changes.Count} change_log rows for run {ingestRunId}";

        // Do the job (service is doing the thinking)
        var classifications = await _classifier.ClassifyAsync(ingestRunId);

        // What the agent produced
        var outputSummary = JsonSerializer.Serialize(
            classifications.Select(c => new
            {
                planet = c.Key,
                classification = c.Value.Classification,
                reasoning = c.Value.Reasoning
            }));

        // Log the boundary crossing
        await _boundary.RecordHandoffAsync(
            ingestRunId,
            AgentName,
            inputSummary,
            outputSummary);

        await _plog.Info($"{AgentName} agent complete.", ingestRunId);
    }
}
