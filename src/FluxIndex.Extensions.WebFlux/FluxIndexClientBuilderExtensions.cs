using FluxIndex.SDK;
using FluxIndex.Extensions.WebFlux.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// FluxIndexClientBuilder extensions for WebFlux integration
/// </summary>
public static class FluxIndexClientBuilderExtensions
{
    /// <summary>
    /// Add WebFlux web content processing capabilities
    /// Requires AI services to be configured separately
    /// </summary>
    public static FluxIndexClientBuilder UseWebFlux(this FluxIndexClientBuilder builder)
    {
        // Add WebFlux services
        builder.ConfigureServices(services =>
        {
            services.AddWebFluxIntegration();
        });

        return builder;
    }

    /// <summary>
    /// Add WebFlux with OpenAI services for complete web processing
    /// </summary>
    public static FluxIndexClientBuilder UseWebFluxWithOpenAI(this FluxIndexClientBuilder builder)
    {
        // Add WebFlux with OpenAI services
        builder.ConfigureServices(services =>
        {
            services.AddWebFluxWithOpenAI();
        });

        return builder;
    }

    /// <summary>
    /// Add WebFlux with mock AI services for testing
    /// </summary>
    public static FluxIndexClientBuilder UseWebFluxWithMockAI(this FluxIndexClientBuilder builder)
    {
        // Add WebFlux with mock AI services
        builder.ConfigureServices(services =>
        {
            services.AddWebFluxWithMockAI();
        });

        return builder;
    }

    /// <summary>
    /// Configure WebFlux crawling options
    /// </summary>
    public static FluxIndexClientBuilder WithWebCrawlingOptions(
        this FluxIndexClientBuilder builder,
        Action<CrawlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var crawlOptions = new CrawlOptions();
        configure(crawlOptions);

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(crawlOptions);
        });

        return builder;
    }

    /// <summary>
    /// Configure WebFlux chunking options
    /// </summary>
    public static FluxIndexClientBuilder WithWebChunkingOptions(
        this FluxIndexClientBuilder builder,
        Action<ChunkingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var chunkingOptions = new ChunkingOptions();
        configure(chunkingOptions);

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(chunkingOptions);
        });

        return builder;
    }

    /// <summary>
    /// Add custom WebFlux content extractor
    /// </summary>
    public static FluxIndexClientBuilder AddWebContentExtractor<T>(this FluxIndexClientBuilder builder)
        where T : class, IContentExtractor
    {
        builder.ConfigureServices(services =>
        {
            services.AddTransient<IContentExtractor, T>();
        });

        return builder;
    }

}