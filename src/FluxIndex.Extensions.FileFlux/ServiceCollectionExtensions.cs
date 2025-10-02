using FileFlux;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex.Extensions.FileFlux;

/// <summary>
/// Extension methods for integrating FileFlux with FluxIndex
/// </summary>
public static class FileFluxServiceCollectionExtensions
{
    /// <summary>
    /// Adds FileFlux integration services to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Optional configuration action for FileFlux options</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddFileFluxIntegration(this IServiceCollection services, Action<FileFluxOptions>? configureOptions = null)
    {
        // Register FileFlux services (uses FileFlux 0.2.12 API) - using FileFlux's own extension method
        services.AddFileFlux();

        // Configure FluxIndex-specific options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register FileFlux integration service for FluxIndex
        services.AddScoped<FileFluxIntegration>();

        return services;
    }
}

/// <summary>
/// Configuration options for FileFlux integration with FluxIndex
/// </summary>
public class FileFluxOptions
{
    /// <summary>
    /// Default chunking strategy (Auto, Smart, MemoryOptimizedIntelligent, Intelligent, Semantic, Paragraph, FixedSize)
    /// </summary>
    public string DefaultChunkingStrategy { get; set; } = ChunkingStrategies.Auto;

    /// <summary>
    /// Default maximum chunk size in tokens (recommended: 1024 for RAG optimization)
    /// </summary>
    public int DefaultMaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Default overlap size between chunks in tokens
    /// </summary>
    public int DefaultOverlapSize { get; set; } = 128;

    /// <summary>
    /// Enable streaming API for memory-efficient processing of large files
    /// </summary>
    public bool UseStreamingApi { get; set; } = false;
}

