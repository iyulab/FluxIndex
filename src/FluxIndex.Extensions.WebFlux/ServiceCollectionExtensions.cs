using Microsoft.Extensions.DependencyInjection;
using WebFlux.Core.Interfaces;
using WebFlux.Extensions;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// Service collection extensions for WebFlux integration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add WebFlux integration services to the service collection
    /// </summary>
    public static IServiceCollection AddWebFluxIntegration(this IServiceCollection services, Action<WebFluxOptions>? configureOptions = null)
    {
        // Register WebFlux services (uses WebFlux 0.1.2 API)
        services.AddWebFluxCore();

        // Configure FluxIndex-specific options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register WebFlux integration service for FluxIndex
        services.AddScoped<WebFluxIntegration>();

        return services;
    }
}