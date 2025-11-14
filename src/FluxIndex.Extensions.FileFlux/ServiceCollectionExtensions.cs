using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Quality;
using Microsoft.Extensions.DependencyInjection;
using IFileFluxTextCompletionService = FileFlux.ITextCompletionService;

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
        // Register FileFlux services (uses FileFlux 0.3.0 API) - using FileFlux's own extension method
        services.AddFileFlux();

        // Register FileFlux quality analyzer for document quality analysis and QA generation (FileFlux 0.3.0)
        services.AddScoped<ChunkQualityEngine>();
        services.AddScoped<IDocumentQualityAnalyzer, DocumentQualityAnalyzer>();

        // Register FluxIndex's text completion adapter for FileFlux
        // This adapter bridges FluxIndex's ITextCompletionService to FileFlux's ITextCompletionService interface
        // FileFlux will use FluxIndex's OpenAI implementation for all LLM-based operations
        services.AddScoped<IFileFluxTextCompletionService, FluxIndexTextCompletionAdapter>();

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
    /// Enable streaming API for memory-efficient processing of large files (recommended for files > 10MB)
    /// </summary>
    public bool UseStreamingApi { get; set; } = true;

    /// <summary>
    /// Enable immediate indexing for ultra-large files (chunks indexed in batches during processing)
    /// Only applies when UseStreamingApi is true
    /// </summary>
    public bool EnableImmediateIndexing { get; set; } = false;

    /// <summary>
    /// Batch size for immediate indexing (default: 100 chunks)
    /// Only applies when EnableImmediateIndexing is true
    /// </summary>
    public int ImmediateIndexingBatchSize { get; set; } = 100;
}

