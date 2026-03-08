namespace Exoplanet.Shared.Entities;

public class IngestRunEntity
{
    public int Id { get; set; }
    public DateTimeOffset RunTimestamp { get; set; }
    public string Source { get; set; } = null!;
    public string? SourceUrl { get; set; }
    public int RowsFetched { get; set; }
    public int RowsNew { get; set; }
    public int RowsUpdated { get; set; }
    public int RowsDeleted { get; set; }
    public int RowsUnchanged { get; set; }
    public string Status { get; set; } = "RUNNING";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
