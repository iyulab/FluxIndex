using FluxIndex.Extensions.WebFlux;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.SDK;

/// <summary>
/// Extension methods for integrating WebFlux with FluxIndex SDK
/// </summary>
public static class WebFluxExtensions
{
    /// <summary>
    /// Adds WebFlux web content processing to FluxIndex
    /// </summary>
    /// <param name="builder">FluxIndex context builder</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseWebFlux(this FluxIndexContextBuilder builder)
    {
        return builder.ConfigureServices(services => services.AddWebFluxIntegration());
    }

    /// <summary>
    /// Adds WebFlux with custom processing options
    /// </summary>
    /// <param name="builder">FluxIndex context builder</param>
    /// <param name="configureOptions">Configuration action for WebFlux options</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseWebFlux(
        this FluxIndexContextBuilder builder,
        Action<WebFluxOptions> configureOptions)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddWebFluxIntegration(configureOptions);
        });
    }

    /// <summary>
    /// Get WebFlux integration service from FluxIndex context
    /// </summary>
    /// <param name="context">FluxIndex context</param>
    /// <returns>WebFlux integration service</returns>
    public static WebFluxIntegration GetWebFluxIntegration(this IFluxIndexContext context)
    {
        return context.ServiceProvider.GetRequiredService<WebFluxIntegration>();
    }

    /// <summary>
    /// Index web content directly from FluxIndex context
    /// </summary>
    /// <param name="context">FluxIndex context</param>
    /// <param name="url">URL to process</param>
    /// <param name="options">WebFlux processing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document ID of indexed content</returns>
    public static async Task<string> IndexWebContentAsync(
        this IFluxIndexContext context,
        string url,
        WebFluxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var webFlux = context.GetWebFluxIntegration();
        return await webFlux.IndexWebContentAsync(url, options, cancellationToken);
    }

    /// <summary>
    /// Index multiple URLs directly from FluxIndex context
    /// </summary>
    /// <param name="context">FluxIndex context</param>
    /// <param name="urls">URLs to process</param>
    /// <param name="options">WebFlux processing options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document IDs of indexed content</returns>
    public static async Task<IEnumerable<string>> IndexMultipleUrlsAsync(
        this IFluxIndexContext context,
        IEnumerable<string> urls,
        WebFluxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var webFlux = context.GetWebFluxIntegration();
        return await webFlux.IndexMultipleUrlsAsync(urls, options, cancellationToken);
    }
}