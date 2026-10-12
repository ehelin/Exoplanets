using Exoplanet.DAL;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Exoplanet.Services.Services;

public sealed class AgentBoundary : IAgentBoundary
{
    private readonly ExoplanetDbContext _db;
    private readonly IPipelineLogger _plog;

    public AgentBoundary(ExoplanetDbContext db, IPipelineLogger plog)
    {
        _db = db;
        _plog = plog;
    }

    public async Task RecordHandoffAsync(
        int ingestId,
        string agentName,
        string inputSummary,
        string outputSummary)
    {
        var handoff = new AgentHandoff
        {
            IngestId = ingestId,
            AgentName = agentName,
            InputSummary = inputSummary,
            OutputSummary = outputSummary,
            HandoffTimestamp = DateTime.UtcNow
        };

        _db.AgentHandoffs.Add(handoff);
        await _db.SaveChangesAsync();

        await _plog.Info($"Handoff recorded: agent={agentName}, ingestId={ingestId}");
    }

    public async Task<AgentHandoff?> ReadPreviousHandoffAsync(
        int ingestId,
        string previousAgentName)
    {
        return await _db.AgentHandoffs
            .Where(h => h.IngestId == ingestId && h.AgentName == previousAgentName)
            .OrderByDescending(h => h.HandoffTimestamp)
            .FirstOrDefaultAsync();
    }

    // TEMP stub so this compiles. Real impl comes when we add
    // AgentIteration entity + agent_iteration table.
    // For now, iterations are visible in pipeline_log instead of their own table.
    public async Task RecordIterationAsync(
        int ingestId,
        string agentName,
        int iterationNumber,
        string approach,
        string resultSummary,
        string checkResult)
    {
        await _plog.Info(
            $"Iteration: agent={agentName}, ingest={ingestId}, iter={iterationNumber}, " +
            $"approach={approach}, result={resultSummary}, check={checkResult}");
    }
}