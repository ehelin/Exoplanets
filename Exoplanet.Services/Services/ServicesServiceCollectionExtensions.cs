using Exoplanet.DAL;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shared.Interfaces;

namespace Exoplanet.Services;

public static class ServicesServiceCollectionExtensions
{
    public static IServiceCollection AddExoplanetServices(this IServiceCollection services, string connectionString)
    {
        services.AddExoplanetDal(connectionString);

        services.AddHttpClient<IExoplanetApiClient, ExoplanetApiClient>(http =>
        {
            http.BaseAddress = new Uri("https://exoplanetarchive.ipac.caltech.edu/");
            http.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddScoped<IExoplanetService, ExoplanetService>();
        services.AddScoped<IExoplanetRepository, ExoplanetRepository>();
        return services;
    }
}
