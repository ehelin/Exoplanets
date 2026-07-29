using Exoplanet.Shared.Interfaces;
using Shared.Interfaces;

namespace Exoplanet.Services.Services;

public sealed class NarratorAgent : IAgent
{
    private readonly IChangeReportService _reportService;
    private readonly IAgentBoundary _boundary;
    private readonly IPipelineLogger _plog;

    public string AgentName => "narrator";

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
        // Read the classifier's handoff — this is the only input the narrator sees
        var classifierHandoff = await _boundary.ReadPreviousHandoffAsync(ingestRunId, "classifier");

        if (classifierHandoff == null)
        {
            await _plog.Warning($"{AgentName} agent: no classifier handoff for run {ingestRunId}, skipping.", ingestRunId);
            return;
        }

        // Do the job (service is doing the thinking)
        var reportText = await _reportService.GenerateReportAsync(ingestRunId);

        // Log the boundary crossing — input is what the classifier passed us
        await _boundary.RecordHandoffAsync(
            ingestRunId,
            AgentName,
            classifierHandoff.OutputSummary,
            reportText);

        await _plog.Info($"{AgentName} agent complete.", ingestRunId);
    }
}
