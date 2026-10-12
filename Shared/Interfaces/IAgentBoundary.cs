using Exoplanet.Shared.Entities;

namespace Exoplanet.Shared.Interfaces;

public interface IAgentBoundary
{
    Task RecordHandoffAsync(
        int ingestId,
        string agentName,
        string inputSummary,
        string outputSummary);

    Task<AgentHandoff?> ReadPreviousHandoffAsync(
        int ingestId,
        string previousAgentName);

    // Post 1 (Real Thinking Agents): logs one iteration of an agent's thinking loop.
    // Writes to agent_iteration.
    Task RecordIterationAsync(
        int ingestId,
        string agentName,
        int iterationNumber,
        string approach,
        string resultSummary,
        string checkResult);
}
