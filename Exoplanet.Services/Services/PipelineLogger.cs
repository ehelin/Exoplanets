using Exoplanet.DAL;
using Exoplanet.Shared.Entities;
using Exoplanet.Shared.Interfaces;

public sealed class PipelineLogger : IPipelineLogger
{
    private readonly ExoplanetDbContext _db;

    public PipelineLogger(ExoplanetDbContext db)
    {
        _db = db;
    }

    public Task Info(string message, int? runId = null)
        => Write("INFO", message, runId);

    public Task Warning(string message, int? runId = null)
        => Write("WARNING", message, runId);

    public Task Error(string message, int? runId = null, Exception? ex = null)
        => Write("ERROR", message, runId, ex?.ToString());

    private async Task Write(string level, string message, int? runId, string? exception = null)
    {
        _db.PipelineLogs.Add(new PipelineLogEntity
        {
            IngestRunId = runId,
            LogLevel = level,
            Message = message,
            Exception = exception,
            LoggedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}