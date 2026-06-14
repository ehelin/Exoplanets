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
}
