namespace Exoplanet.Shared.Interfaces;

/// <summary>
/// A self-directed thinking loop.
/// Produces an answer, checks its own work, and tries again with a different
/// approach if unsatisfied — stops when it decides it's done or hits MaxIterations.
/// Every iteration is logged in agent_iteration.
/// </summary>
public interface IAgent
{
    string AgentName { get; }

    /// <summary>
    /// Safety cap. The loop will stop after this many attempts even if
    /// the agent is not satisfied with its own answer.
    /// </summary>
    int MaxIterations { get; }

    Task ExecuteAsync(int ingestRunId);
}
