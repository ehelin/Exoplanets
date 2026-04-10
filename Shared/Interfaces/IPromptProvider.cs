using Exoplanet.Shared.Entities;

namespace Exoplanet.Shared.Interfaces;

public interface IPromptProvider
{
    string GetPrompt(List<PlanetEntity> planets);
}
