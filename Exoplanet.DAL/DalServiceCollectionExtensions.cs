using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Exoplanet.DAL;

public static class DalServiceCollectionExtensions
{
    public static IServiceCollection AddExoplanetDal(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ExoplanetDbContext>(opt =>
        {
            opt.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure(5));
        });

        return services;
    }
}
