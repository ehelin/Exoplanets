using Exoplanet.Shared.Interfaces;
using Shared.Interfaces;

namespace Exoplanet.Services.Services;

public sealed class NarratorAgent : IAgent
{
    private readonly IChangeReportService _reportService;
    private readonly IAgentBoundary _boundary;
    private readonly IPipelineLogger _plog;

    public string AgentName => "narrator";
    public int MaxIterations => 3;

    public NarratorAgent(
        IChangeReportService reportService,
        IAgentBoundary boundary,
        IPipelineLogger plog)
    {
        _reportService = reportService;
        _boundary = boundary;
        _plog = plog;
    }

    public async Task ExecuteAsync(int ingestRunId)
    {
        // Read the classifier's handoff — this is the primary input the narrator sees
        var classifierHandoff = await _boundary.ReadPreviousHandoffAsync(ingestRunId, "classifier");

        if (classifierHandoff == null)
        {
            await _plog.Warning(
                $"{AgentName} agent: no classifier handoff for run {ingestRunId}, skipping.",
                ingestRunId);
            return;
        }

        // Post 1 (Real Thinking Agents): respect the Reviewer's verdict.
        // If reviewer rejected, don't ship a narrative at all.
        var reviewerHandoff = await _boundary.ReadPreviousHandoffAsync(ingestRunId, "reviewer");
        if (reviewerHandoff != null && reviewerHandoff.OutputSummary.Contains("\"verdict\":\"reject\""))
        {
            await _plog.Warning(
                $"{AgentName} agent: reviewer rejected classifications for run {ingestRunId}, skipping narrative.",
                ingestRunId);
            return;
        }

        // Post 1 (Real Thinking Agents): self-directed loop. Generate the draft,
        // critique it, revise if not satisfied, stop when confident or MaxIterations hit.
        string reportText = string.Empty;
        string checkResult = "no_attempt";

        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var framing = ChooseFraming(iteration);
            await _plog.Info(
                $"{AgentName} iteration {iteration}/{MaxIterations} using framing={framing}",
                ingestRunId);

            reportText = await _reportService.GenerateReportAsync(ingestRunId);
            checkResult = CritiqueDraft(reportText);

            // Post 1 debug: pause here to inspect the narrator's just-made critique
            if (System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Break();

            await _boundary.RecordIterationAsync(
                ingestRunId,
                AgentName,
                iteration,
                framing,
                $"{reportText.Length} chars generated",
                checkResult);

            if (checkResult == "satisfied")
            {
                await _plog.Info($"{AgentName} satisfied at iteration {iteration}.", ingestRunId);
                break;
            }

            if (iteration == MaxIterations)
            {
                await _plog.Warning(
                    $"{AgentName} hit safety cap of {MaxIterations} iterations, shipping best-effort draft.",
                    ingestRunId);
            }
        }

        // Log the boundary crossing — input is what the classifier passed us
        await _boundary.RecordHandoffAsync(
            ingestRunId,
            AgentName,
            classifierHandoff.OutputSummary,
            reportText);

        await _plog.Info($"{AgentName} agent complete.", ingestRunId);
    }

    // Post 1: framing label per iteration. Heuristic critique for now;
    // a later post will replace with an LLM-based critic.
    private static string ChooseFraming(int iteration) => iteration switch
    {
        1 => "Standard",
        2 => "RevisedForDetail",
        _ => "RevisedForClarity"
    };

    private static string CritiqueDraft(string draft)
    {
        if (string.IsNullOrWhiteSpace(draft)) return "retry_empty";
        if (draft.Length < 100) return "retry_too_short";
        return "satisfied";
    }
}
