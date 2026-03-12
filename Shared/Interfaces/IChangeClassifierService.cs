namespace Exoplanet.Services;

public interface IChangeClassifierService
{
    Task ClassifyAsync(int ingestRunId);
}