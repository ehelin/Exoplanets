namespace Exoplanet.Services;

public interface IChangeReportService
{
    Task<string> GenerateReportAsync(int ingestRunId);
}
