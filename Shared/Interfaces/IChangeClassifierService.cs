namespace Exoplanet.Services;

public interface IChangeClassifierService
{
    Task<Dictionary<string, (string Classification, string Reasoning)>> ClassifyAsync(int ingestRunId);
}
