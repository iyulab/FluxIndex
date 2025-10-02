using FluxIndex.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// Extension methods for FluxIndexContext to add WebFlux integration
/// </summary>
public static class FluxIndexContextExtensions
{
    /// <summary>
    /// Index web content directly from IFluxIndexContext
    /// </summary>
    /// <param name="context">FluxIndex context</param>
    /// <param name="url">URL to process</param>
    /// <param name="options">WebFlux options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Document ID</returns>
    public static async Task<string> IndexWebContentAsync(
        this IFluxIndexContext context,
        string url,
        WebFluxProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var webFluxIntegration = context.GetWebFluxIntegration();
        return await webFluxIntegration.IndexWebContentAsync(url, options, cancellationToken);
    }

    /// <summary>
    /// Get WebFlux integration service from FluxIndex context
    /// </summary>
    /// <param name="context">FluxIndex context</param>
    /// <returns>WebFlux integration service</returns>
    public static WebFluxIntegration GetWebFluxIntegration(this IFluxIndexContext context)
    {
        if (context.ServiceProvider == null)
        {
            throw new InvalidOperationException("ServiceProvider is not available in FluxIndex context");
        }

        var webFluxIntegration = context.ServiceProvider.GetService(typeof(WebFluxIntegration)) as WebFluxIntegration;
        if (webFluxIntegration == null)
        {
            throw new InvalidOperationException(
                "WebFlux integration is not registered. Make sure to call UseWebFlux() on FluxIndexContextBuilder");
        }

        return webFluxIntegration;
    }

    /// <summary>
    /// Process web content to document without indexing
    /// </summary>
    /// <param name="context">FluxIndex context</param>
    /// <param name="url">URL to process</param>
    /// <param name="options">WebFlux options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>FluxIndex Document</returns>
    public static async Task<Document> ProcessWebContentToDocumentAsync(
        this IFluxIndexContext context,
        string url,
        WebFluxProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var webFluxIntegration = context.GetWebFluxIntegration();
        return await webFluxIntegration.ProcessWebContentToDocumentAsync(url, options, cancellationToken);
    }
}