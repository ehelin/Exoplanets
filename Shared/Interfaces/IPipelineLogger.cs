using Exoplanet.Shared.Entities;

namespace Exoplanet.Shared.Interfaces;

public interface IPipelineLogger
{
    Task Info(string message, int? runId = null);
    Task Warning(string message, int? runId = null);
    Task Error(string message, int? runId = null, Exception? ex = null);
}