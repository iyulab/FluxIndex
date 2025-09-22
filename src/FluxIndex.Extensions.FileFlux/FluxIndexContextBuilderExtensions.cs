using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.FileFlux;

/// <summary>
/// Extension methods for FluxIndexContextBuilder to add FileFlux integration
/// </summary>
public static class FluxIndexContextBuilderExtensions
{
    /// <summary>
    /// Adds FileFlux integration to FluxIndex context
    /// </summary>
    /// <param name="builder">FluxIndex context builder</param>
    /// <param name="configureOptions">Optional configuration action for FileFlux options</param>
    /// <returns>FluxIndex context builder for chaining</returns>
    public static FluxIndexContextBuilder UseFileFlux(this FluxIndexContextBuilder builder, Action<FileFluxOptions>? configureOptions = null)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddFileFlux(configureOptions);
        });
    }
}