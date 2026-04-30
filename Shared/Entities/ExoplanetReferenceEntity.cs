namespace Exoplanet.Shared.Entities;

public class ExoplanetReferenceEntity
{
    public int Id { get; set; }
    public string PlanetName { get; set; } = null!;
    public string ReferenceName { get; set; } = null!;
    public string? PubDate { get; set; }
    public string Content { get; set; } = null!;
    public float[]? Embedding { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
