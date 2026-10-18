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

    // Post 1 (Real Thinking Agents): logs one iteration of an agent's thinking loop to agent_iteration.
    public async Task RecordIterationAsync(
        int ingestRunId,
        string agentName,
        int iterationNumber,
        string approach,
        string resultSummary,
        string checkResult)
    {
        var iteration = new AgentIteration
        {
            IngestRunId = ingestRunId,
            AgentName = agentName,
            IterationNumber = iterationNumber,
            Approach = approach,
            ResultSummary = resultSummary,
            CheckResult = checkResult,
            IteratedAt = DateTime.UtcNow
        };

        _db.AgentIterations.Add(iteration);
        await _db.SaveChangesAsync();

        await _plog.Info(
            $"Iteration recorded: agent={agentName}, ingest={ingestRunId}, iter={iterationNumber}, check={checkResult}");
    }
}
