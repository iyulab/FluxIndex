using FileFlux;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

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
    public static IServiceCollection AddFileFlux(this IServiceCollection services, Action<FileFluxOptions>? configureOptions = null)
    {
        // Configure options if provided
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register our custom document processor implementation
        services.AddScoped<IDocumentProcessor, SimpleDocumentProcessor>();

        // Register FileFlux integration service
        services.AddScoped<FileFluxIntegration>();

        return services;
    }
}

/// <summary>
/// Configuration options for FileFlux integration
/// </summary>
public class FileFluxOptions
{
    /// <summary>
    /// Default chunking strategy (Auto, Smart, MemoryOptimizedIntelligent, Intelligent, Semantic, Paragraph, FixedSize)
    /// </summary>
    public string DefaultChunkingStrategy { get; set; } = "Auto";

    /// <summary>
    /// Default maximum chunk size in characters (recommended: 512 for RAG optimization)
    /// </summary>
    public int DefaultMaxChunkSize { get; set; } = 512;

    /// <summary>
    /// Default overlap size between chunks in characters
    /// </summary>
    public int DefaultOverlapSize { get; set; } = 64;

    /// <summary>
    /// Whether to preserve document structure during chunking by default
    /// </summary>
    public bool DefaultPreserveStructure { get; set; } = true;

    /// <summary>
    /// Enable parallel processing for better performance
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;

    /// <summary>
    /// Enable LRU caching for repeated document processing
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Enable streaming API for memory-efficient processing of large files
    /// </summary>
    public bool UseStreamingApi { get; set; } = true;
}

/// <summary>
/// Minimal document processor interface for FileFlux compatibility
/// </summary>
public interface IDocumentProcessor
{
    IAsyncEnumerable<DocumentChunk> ProcessAsync(string filePath, ChunkingOptions options);
}

/// <summary>
/// Chunking options for document processing
/// </summary>
public class ChunkingOptions
{
    public string Strategy { get; set; } = "Auto";
    public int MaxChunkSize { get; set; } = 512;
    public int OverlapSize { get; set; } = 64;
}

/// <summary>
/// Document chunk representation
/// </summary>
public class DocumentChunk
{
    public required string Content { get; init; }
    public int ChunkIndex { get; init; }
    public int StartPosition { get; init; }
    public int EndPosition { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Simple document processor implementation
/// </summary>
internal class SimpleDocumentProcessor : IDocumentProcessor
{
    private readonly ILogger<SimpleDocumentProcessor> _logger;

    public SimpleDocumentProcessor(ILogger<SimpleDocumentProcessor> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<DocumentChunk> ProcessAsync(string filePath, ChunkingOptions options)
    {
        _logger.LogInformation("Processing document: {FilePath}", filePath);

        var content = await File.ReadAllTextAsync(filePath);
        var chunks = CreateChunks(content, options);

        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    private static IEnumerable<DocumentChunk> CreateChunks(string content, ChunkingOptions options)
    {
        var chunkSize = options.MaxChunkSize;
        var overlap = options.OverlapSize;
        var chunkIndex = 0;

        for (int i = 0; i < content.Length; i += chunkSize - overlap)
        {
            var remainingLength = content.Length - i;
            var actualChunkSize = Math.Min(chunkSize, remainingLength);
            var chunkContent = content.Substring(i, actualChunkSize);

            yield return new DocumentChunk
            {
                Content = chunkContent,
                ChunkIndex = chunkIndex++,
                StartPosition = i,
                EndPosition = i + actualChunkSize,
                Metadata = new Dictionary<string, object>
                {
                    ["strategy"] = options.Strategy,
                    ["source"] = "SimpleDocumentProcessor"
                }
            };

            if (i + actualChunkSize >= content.Length)
                break;
        }
    }
}

