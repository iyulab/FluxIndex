using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IFileFluxDocumentAnalysisService = FileFlux.IDocumentAnalysisService;

namespace FluxIndex.SDK.Extensions.FileFlux;

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
    /// Enable metadata enrichment by default (requires ITextCompletionService)
    /// </summary>
    public bool EnableMetadataEnrichment { get; set; }

    /// <summary>
    /// Default metadata schema (General, Academic, Technical, Legal, Medical)
    /// </summary>
    public string DefaultMetadataSchema { get; set; } = "General";

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
    /// Enable LLM-based content refinement in the FileFlux 5-stage pipeline.
    /// When enabled, content is refined using ITextCompletionService before chunking.
    /// Requires ITextCompletionService to be registered in DI.
    /// </summary>
    public bool EnableLlmRefine { get; set; }

    /// <summary>
    /// LLM refinement options when EnableLlmRefine is true.
    /// Controls what improvements the LLM should make (noise removal, sentence restoration, etc.)
    /// </summary>
    public LlmRefineOptionsConfig LlmRefineOptions { get; set; } = new();
}

/// <summary>
/// Configuration for LLM refinement options (mirrors FileFlux.Core.LlmRefineOptions)
/// </summary>
public class LlmRefineOptionsConfig
{
    /// <summary>
    /// Enable noise removal (ads, legal notices, irrelevant content).
    /// Default: true
    /// </summary>
    public bool RemoveNoise { get; set; } = true;

    /// <summary>
    /// Restore broken sentences (PDF line breaks).
    /// Default: true
    /// </summary>
    public bool RestoreSentences { get; set; } = true;

    /// <summary>
    /// Restructure document sections (merge/split, fix heading levels).
    /// Default: true
    /// </summary>
    public bool RestructureSections { get; set; } = true;

    /// <summary>
    /// Correct OCR errors.
    /// Default: true
    /// </summary>
    public bool CorrectOcrErrors { get; set; } = true;

    /// <summary>
    /// Merge semantically duplicate content.
    /// Default: true
    /// </summary>
    public bool MergeDuplicates { get; set; } = true;

    /// <summary>
    /// Preserve original formatting where possible.
    /// Default: true
    /// </summary>
    public bool PreserveFormatting { get; set; } = true;

    /// <summary>
    /// Maximum tokens to use for LLM (0 = no limit).
    /// Default: 0
    /// </summary>
    public int MaxTokens { get; set; }

    /// <summary>
    /// LLM temperature (0.0 - 1.0). Lower = more deterministic.
    /// Default: 0.3
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Custom instructions for LLM.
    /// </summary>
    public string? CustomInstructions { get; set; }
}

