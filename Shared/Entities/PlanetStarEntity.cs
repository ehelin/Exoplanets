namespace Exoplanet.Shared.Entities;

public class PlanetStarEntity
{
    public int Id { get; set; }
    public int PlanetId { get; set; }
    public int StarId { get; set; }

    public PlanetEntity Planet { get; set; } = null!;
    public StarEntity Star { get; set; } = null!;
}
