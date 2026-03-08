using Exoplanet.Shared.Entities;

namespace Exoplanet.Shared.Models;

public class DiffResult
{
    public List<ChangeLogEntity> Changes { get; set; } = new();
    public List<ExoplanetEntity> ToInsert { get; set; } = new();
    public List<ExoplanetEntity> ToUpdate { get; set; } = new();
    public int NewCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DeletedCount { get; set; }
    public int UnchangedCount { get; set; }
}
