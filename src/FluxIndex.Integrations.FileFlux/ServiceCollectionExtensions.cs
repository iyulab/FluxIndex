using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FluxIndex.Core.Application.Interfaces;
using MetadataSchema = FluxIndex.Core.Application.Models.MetadataSchema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IFileFluxDocumentAnalysisService = FileFlux.IDocumentAnalysisService;

namespace FluxIndex.Integrations.FileFlux;

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
        // Register FileFlux services (uses FileFlux 0.4.8 API) - using FileFlux's own extension method
        services.AddFileFlux();

        // Register FluxIndex's text completion adapter for FileFlux
        // This adapter bridges FluxIndex's ITextCompletionService to FileFlux's IDocumentAnalysisService interface
        // FileFlux will use FluxIndex's OpenAI implementation for all LLM-based operations
        services.AddScoped<IFileFluxDocumentAnalysisService, FluxIndexTextCompletionAdapter>();

        // Register FluxIndex's LLM refiner adapter for FileFlux
        // This adapter bridges FluxIndex's ITextCompletionService to FileFlux's ILlmRefiner interface
        // Enables LLM-based content refinement in the FileFlux 5-stage pipeline
        services.TryAddScoped<ILlmRefiner, LlmRefinerAdapter>();

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
    /// Default chunking strategy (Auto, Smart, MemoryOptimizedIntelligent, Intelligent, Semantic, Paragraph, FixedSize, Hierarchical, PageLevel)
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
    /// Default language code for language-aware chunking (e.g., "ko", "en", "zh", "ja", "ar")
    /// FileFlux 0.4.8 supports 11 language profiles with comprehensive text segmentation
    /// Set to null or empty to enable automatic language detection
    /// </summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>
    /// Enable automatic language detection using Unicode script analysis
    /// When enabled and DefaultLanguage is not set, FileFlux will auto-detect the document language
    /// FileFlux 0.4.8 uses ILanguageProfileProvider for intelligent language detection
    /// </summary>
    public bool EnableLanguageAutoDetection { get; set; } = true;

    /// <summary>
    /// Extract AI metadata for indexed documents by default (<see cref="ProcessingOptions.EnableMetadataEnrichment"/>);
    /// requires an <see cref="IMetadataExtractor"/> registered on the FluxIndex builder.
    /// </summary>
    public bool EnableMetadataEnrichment { get; set; }

    /// <summary>
    /// Default schema for AI metadata extraction (<see cref="ProcessingOptions.MetadataSchema"/>).
    /// </summary>
    public MetadataSchema DefaultMetadataSchema { get; set; } = MetadataSchema.General;

    /// <summary>
    /// Enable streaming API for memory-efficient processing of large files (recommended for files > 10MB)
    /// </summary>
    public bool UseStreamingApi { get; set; } = true;

    /// <summary>
    /// Enable immediate indexing for ultra-large files (chunks indexed in batches during processing)
    /// Only applies when UseStreamingApi is true
    /// </summary>
    public bool EnableImmediateIndexing { get; set; }

    /// <summary>
    /// Batch size for immediate indexing (default: 100 chunks)
    /// Only applies when EnableImmediateIndexing is true
    /// </summary>
    public int ImmediateIndexingBatchSize { get; set; } = 100;

    /// <summary>
    /// Include extended language profile metadata in indexed chunks
    /// When enabled, adds script code, writing direction, and other language-specific metadata
    /// </summary>
    public bool IncludeLanguageProfileMetadata { get; set; } = true;

    /// <summary>
    /// Run FileFlux's LLM refine stage (noise removal, sentence restoration, …) before chunking. Off by default: it costs
    /// a model call per document and rewrites the text. Needs an <c>ITextCompletionService</c> on the FluxIndex builder.
    /// </summary>
    public bool EnableLlmRefine { get; set; }

    /// <summary>
    /// The refine settings FileFlux uses when <see cref="EnableLlmRefine"/> is on; <c>null</c> (the default) uses
    /// FileFlux's defaults.
    /// </summary>
    public LlmRefineOptions? LlmRefineOptions { get; set; }
}

