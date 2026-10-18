using Exoplanet.Shared.Models;

namespace Exoplanet.Services;

public interface IChangeClassifierService
{
    // Post 1 (Real Thinking Agents): strategy parameter lets the ClassifierAgent
    // escalate approach per iteration. Default keeps existing behavior for callers
    // that haven't been updated.
    Task<Dictionary<string, (string Classification, string Reasoning)>> ClassifyAsync(
        int ingestRunId,
        ClassificationStrategy strategy = ClassificationStrategy.Default);
}
