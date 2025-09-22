using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// Service collection extensions for WebFlux integration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add WebFlux integration services to the service collection
    /// </summary>
    public static IServiceCollection AddWebFluxIntegration(this IServiceCollection services)
    {
        // Register FluxIndex WebFlux integration
        // Note: Actual WebFlux services would be registered here when API is verified
        services.AddScoped<WebFluxIntegration>();

        return services;
    }

    /// <summary>
    /// Add WebFlux integration with custom configuration
    /// </summary>
    public static IServiceCollection AddWebFluxIntegration(
        this IServiceCollection services,
        Action<WebFluxOptions> configureOptions)
    {
        // Register FluxIndex WebFlux integration
        // Note: Actual WebFlux services would be registered here when API is verified
        services.AddScoped<WebFluxIntegration>();

        // Configure options if provided
        if (configureOptions != null)
        {
            var options = new WebFluxOptions();
            configureOptions(options);
            services.AddSingleton(options);
        }

        return services;
    }
}