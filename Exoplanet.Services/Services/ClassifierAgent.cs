using System.Text.Json;
using Exoplanet.Shared.Interfaces;
using Exoplanet.Shared.Models;
using Shared.Interfaces;

namespace Exoplanet.Services.Services;

public sealed class ClassifierAgent : IAgent
{
    private readonly IChangeClassifierService _classifier;
    private readonly IAgentBoundary _boundary;
    private readonly IExoplanetRepository _repo;
    private readonly IPipelineLogger _plog;

    public string AgentName => "classifier";
    public int MaxIterations => 3;

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
        // Post 1 (Real Thinking Agents): self-directed loop. Try, check own work,
        // escalate the strategy and retry if not satisfied, stop when confident
        // or MaxIterations hit.
        Dictionary<string, (string Classification, string Reasoning)> classifications = new();
        string checkResult = "no_attempt";

        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var strategy = ChooseStrategy(iteration);
            await _plog.Info(
                $"{AgentName} iteration {iteration}/{MaxIterations} using strategy={strategy}",
                ingestRunId);

            classifications = await _classifier.ClassifyAsync(ingestRunId, strategy);
            checkResult = CheckWork(classifications);

            await _boundary.RecordIterationAsync(
                ingestRunId,
                AgentName,
                iteration,
                strategy.ToString(),
                $"{classifications.Count} classifications produced",
                checkResult);

            if (checkResult == "satisfied")
            {
                await _plog.Info($"{AgentName} satisfied at iteration {iteration}.", ingestRunId);
                break;
            }

            if (iteration == MaxIterations)
            {
                await _plog.Warning(
                    $"{AgentName} hit safety cap of {MaxIterations} iterations, shipping best-effort result.",
                    ingestRunId);
            }
        }

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

    // Post 1: escalate the strategy as we iterate — faster first, more context on retry.
    private static ClassificationStrategy ChooseStrategy(int iteration) => iteration switch
    {
        1 => ClassificationStrategy.Default,
        2 => ClassificationStrategy.WithRag,
        _ => ClassificationStrategy.WithRagAndDetailedPrompt
    };

    // Post 1: simple confidence check. "Satisfied" if at least 80% of classifications
    // came back with substantive reasoning (more than 30 chars). Retry otherwise.
    private static string CheckWork(
        Dictionary<string, (string Classification, string Reasoning)> classifications)
    {
        if (classifications.Count == 0)
            return "retry_empty";

        int withReasoning = classifications.Count(c => (c.Value.Reasoning?.Length ?? 0) > 30);
        double ratio = (double)withReasoning / classifications.Count;

        return ratio >= 0.8 ? "satisfied" : "retry_thin_reasoning";
    }
}
