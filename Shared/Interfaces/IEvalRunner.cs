using Exoplanet.Shared.Entities;

namespace Exoplanet.Shared.Interfaces;

public interface IEvalRunner
{
    Task EvaluateAsync(int ingestRunId);
}
