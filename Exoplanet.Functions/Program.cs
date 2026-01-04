using Exoplanet.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration(cfg => cfg.AddEnvironmentVariables())
    .ConfigureServices((ctx, services) =>
    {
        var cs = ctx.Configuration["POSTGRES_CONNECTION_STRING"];
        //if (string.IsNullOrWhiteSpace(cs))
        //    throw new InvalidOperationException("Missing POSTGRES_CONNECTION_STRING.");

        services.AddExoplanetServices(cs);
    })
    .Build();

host.Run();
