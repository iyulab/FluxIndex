using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.WebFlux;

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
            services.AddWebFluxIntegration(configureOptions);
        });
    }
}
