using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Exoplanet.DAL;

public static class DalServiceCollectionExtensions
{
    public static IServiceCollection AddExoplanetDal(
        this IServiceCollection services, string connectionString, string? vectorConnectionString = null)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        services.AddDbContext<ExoplanetDbContext>(opt =>
        {
            opt.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure(5));
        });

        if (!string.IsNullOrEmpty(vectorConnectionString))
        {
            services.AddDbContext<VectorDbContext>(opt =>
            {
                opt.UseNpgsql(vectorConnectionString, npg => npg.EnableRetryOnFailure(5));
            });
        }

        return services;
    }
}