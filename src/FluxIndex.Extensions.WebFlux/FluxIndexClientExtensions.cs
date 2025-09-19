using FluxIndex.SDK;
using FluxIndex.Extensions.WebFlux.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// FluxIndexClient extensions for WebFlux operations
/// </summary>
public static class FluxIndexClientExtensions
{
    /// <summary>
    /// Get WebFlux document processor from the client's service provider
    /// </summary>
    public static IDocumentProcessor GetWebDocumentProcessor(this IFluxIndexClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Note: This would require access to the service provider from FluxIndexClient
        // For now, we'll return a basic implementation
        throw new NotImplementedException("Service provider access needed from FluxIndexClient");
    }

    /// <summary>
    /// Get WebFlux indexer from the client's service provider
    /// </summary>
    public static WebFluxIndexer GetWebIndexer(this IFluxIndexClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Note: This would require access to the service provider from FluxIndexClient
        // For now, we'll return a basic implementation
        throw new NotImplementedException("Service provider access needed from FluxIndexClient");
    }

    /// <summary>
    /// Index a website directly through the client
    /// </summary>
    public static async Task<string> IndexWebsiteAsync(
        this IFluxIndexClient client,
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(url);

        var webIndexer = client.GetWebIndexer();
        return await webIndexer.IndexWebsiteAsync(url, crawlOptions, chunkingOptions, cancellationToken);
    }

    /// <summary>
    /// Index multiple websites through the client
    /// </summary>
    public static async Task<IEnumerable<string>> IndexWebsitesAsync(
        this IFluxIndexClient client,
        IEnumerable<string> urls,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(urls);

        var webIndexer = client.GetWebIndexer();
        return await webIndexer.IndexWebsitesAsync(urls, crawlOptions, chunkingOptions, maxConcurrency, cancellationToken);
    }

    /// <summary>
    /// Index website with progress reporting through the client
    /// </summary>
    public static IAsyncEnumerable<WebIndexingProgress> IndexWebsiteWithProgressAsync(
        this IFluxIndexClient client,
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(url);

        var webIndexer = client.GetWebIndexer();
        return webIndexer.IndexWebsiteWithProgressAsync(url, crawlOptions, chunkingOptions, cancellationToken);
    }
}