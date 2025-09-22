using FluxIndex.Extensions.WebFlux;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// Extension methods for integrating WebFlux with FluxIndex
/// </summary>
public static class WebFluxServiceCollectionExtensions
{
    /// <summary>
    /// Adds WebFlux web content processing services to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Optional configuration action for WebFlux options</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddWebFlux(this IServiceCollection services, Action<WebFluxOptions>? configureOptions = null)
    {
        // Configure options if provided
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register WebFlux web content processor
        services.AddTransient<IWebContentProcessor, DefaultWebContentProcessor>();

        // Register WebFlux integration service
        services.AddScoped<WebFluxIntegration>();

        return services;
    }
}

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add WebFlux integration
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Adds WebFlux integration to FluxIndex context
    /// </summary>
    /// <param name="builder">FluxIndex context builder</param>
    /// <param name="configureOptions">Optional configuration action for WebFlux options</param>
    /// <returns>FluxIndex context builder for chaining</returns>
    public static FluxIndexContextBuilder UseWebFlux(this FluxIndexContextBuilder builder, Action<WebFluxOptions>? configureOptions = null)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddWebFlux(configureOptions);
        });
    }
}