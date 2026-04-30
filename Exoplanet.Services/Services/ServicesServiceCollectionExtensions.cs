using Exoplanet.DAL;
using Exoplanet.Shared.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shared.Interfaces;

namespace Exoplanet.Services;

public static class ServicesServiceCollectionExtensions
{
    public static IServiceCollection AddExoplanetServices(
        this IServiceCollection services, string connectionString, string? vectorConnectionString = null)
    {
        services.AddExoplanetDal(connectionString, vectorConnectionString);

        services.AddHttpClient<IExoplanetApiClient, ExoplanetApiClient>(http =>
        {
            http.BaseAddress = new Uri("https://exoplanetarchive.ipac.caltech.edu/");
            http.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddHttpClient<IChangeReportService, ChangeReportService>();
        services.AddHttpClient<IChangeClassifierService, ChangeClassifierService>(http =>
        {
            http.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddScoped<IPipelineLogger, PipelineLogger>();
        services.AddScoped<IExoplanetService, ExoplanetService>();
        services.AddScoped<IExoplanetRepository, ExoplanetRepository>();
        services.AddHttpClient<IEvalRunner, EvalRunner>();
        services.AddHttpClient<IRagIngestionService, RagIngestionService>();
        services.AddHttpClient<IRagRetrievalService, RagRetrievalService>();

        return services;
    }
}