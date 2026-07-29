namespace Exoplanet.Shared.Interfaces;

public interface IAgent
{
    string AgentName { get; }
    Task ExecuteAsync(int ingestRunId);
}
