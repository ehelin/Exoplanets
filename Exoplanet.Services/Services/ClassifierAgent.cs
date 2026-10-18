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

        Dictionary<string, (string Classification, string Reasoning)> classifications = new();
        string finalReason = "";

        // The self-directed thinking loop
        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            // TRY: pick an approach for this iteration
            var strategy = ChooseStrategy(iteration);
            await _plog.Info($"{AgentName} iteration {iteration}: trying {strategy}", ingestRunId);

            // Do the job (service is doing the thinking)
            classifications = await _classifier.ClassifyAsync(ingestRunId, strategy);

            // CHECK: examine own work
            var check = CheckWork(classifications, changes.Count);

            // Post 1 debug: pause here to inspect the classifier's just-made decision
            if (System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Break();

            // LOG: record this iteration in agent_iteration
            await _boundary.RecordIterationAsync(
                ingestRunId,
                AgentName,
                iteration,
                strategy.ToString(),
                $"got {classifications.Count} classifications",
                check.Confident ? $"confident: {check.Reason}" : $"not confident: {check.Reason}");

            finalReason = check.Reason;

            // DECIDE: stop or continue?
            if (check.Confident)
            {
                await _plog.Info($"{AgentName} exit at iteration {iteration}: {check.Reason}", ingestRunId);
                break;
            }

            if (iteration == MaxIterations)
            {
                await _plog.Warning($"{AgentName} hit safety cap at {MaxIterations} iterations: {check.Reason}", ingestRunId);
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

        await _plog.Info($"{AgentName} agent complete: {finalReason}", ingestRunId);
    }

    // Iteration 1: fast pass, no RAG.
    // Iteration 2: add RAG context.
    // Iteration 3+: RAG plus a more detailed prompt.
    private static ClassificationStrategy ChooseStrategy(int iteration) => iteration switch
    {
        1 => ClassificationStrategy.Default,
        2 => ClassificationStrategy.WithRag,
        _ => ClassificationStrategy.WithRagAndDetailedPrompt
    };

    // The agent's self-check on its own output. Deterministic and cheap.
    // (Independent second opinion is the Reviewer's job.)
    private static (bool Confident, string Reason) CheckWork(
        Dictionary<string, (string Classification, string Reasoning)> result,
        int expectedCount)
    {
        // Did we cover everything?
        if (result.Count < expectedCount)
            return (false, $"missed {expectedCount - result.Count} of {expectedCount}");

        // Are the Plavalova codes well-formed? (CONFIRMED|XXXX, XXXX = 4 chars)
        var badFormat = result.Count(r =>
        {
            var parts = r.Value.Classification.Split('|');
            return parts.Length != 2 || parts[1].Length != 4;
        });
        if (badFormat > 0)
            return (false, $"{badFormat} malformed Plavalova codes");

        // Is the reasoning substantive?
        var thinReasoning = result.Count(r => (r.Value.Reasoning ?? "").Length < 20);
        if (thinReasoning > 0)
            return (false, $"{thinReasoning} classifications with thin reasoning");

        return (true, "all checks passed");
    }
}
