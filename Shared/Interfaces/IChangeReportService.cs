namespace Exoplanet.Services;

public interface IChangeReportService
{
    Task GenerateReportAsync(int ingestRunId);
}
