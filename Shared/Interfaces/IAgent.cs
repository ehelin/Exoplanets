namespace Exoplanet.Shared.Interfaces;

public interface IAgent
{
    string AgentName { get; }

    // Post 1 (Real Thinking Agents): safety cap on the self-directed thinking loop.
    int MaxIterations { get; }

    Task ExecuteAsync(int ingestRunId);
}
