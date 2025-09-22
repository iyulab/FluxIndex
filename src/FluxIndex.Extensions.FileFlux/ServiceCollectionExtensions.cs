using FluxIndex.Extensions.FileFlux;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluxIndex.SDK;

/// <summary>
/// Extension methods for integrating FileFlux with FluxIndex SDK
/// </summary>
public static class FileFluxExtensions
{
    /// <summary>
    /// Adds FileFlux integration to FluxIndex
    /// </summary>
    /// <param name="builder">FluxIndex context builder</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseFileFlux(this FluxIndexContextBuilder builder)
    {
        return builder.ConfigureServices(services => services.AddFileFluxIntegration());
    }

    /// <summary>
    /// Adds FileFlux integration with custom options
    /// </summary>
    /// <param name="builder">FluxIndex context builder</param>
    /// <param name="configureOptions">Configuration action for FileFlux options</param>
    /// <returns>Builder for chaining</returns>
    public static FluxIndexContextBuilder UseFileFlux(
        this FluxIndexContextBuilder builder,
        Action<FileFluxOptions> configureOptions)
    {
        return builder.ConfigureServices(services =>
        {
            services.Configure(configureOptions);
            services.AddFileFluxIntegration();
        });
    }

    /// <summary>
    /// Internal service registration method
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    private static IServiceCollection AddFileFluxIntegration(this IServiceCollection services)
    {
        // Register FileFlux document processor
        services.AddTransient<IDocumentProcessor, DefaultDocumentProcessor>();

        // Register FileFlux integration service
        services.AddTransient<FileFluxIntegration>();

        return services;
    }
}

/// <summary>
/// Configuration options for FileFlux integration
/// </summary>
public class FileFluxOptions
{
    /// <summary>
    /// Default chunking strategy
    /// </summary>
    public string DefaultChunkingStrategy { get; set; } = "Auto";

    /// <summary>
    /// Default maximum chunk size
    /// </summary>
    public int DefaultMaxChunkSize { get; set; } = 512;

    /// <summary>
    /// Default overlap size between chunks
    /// </summary>
    public int DefaultOverlapSize { get; set; } = 64;
}

/// <summary>
/// Default implementation of IDocumentProcessor for FileFlux
/// </summary>
internal class DefaultDocumentProcessor : IDocumentProcessor
{
    private readonly ILogger<DefaultDocumentProcessor> _logger;

    public DefaultDocumentProcessor(ILogger<DefaultDocumentProcessor> logger)
    {
        _logger = logger;
    }

    public async Task<List<IDocumentChunk>> ProcessAsync(string filePath, ChunkingOptions options)
    {
        _logger.LogInformation("Processing document: {FilePath}", filePath);

        // Basic file processing implementation
        // In real implementation, this would use actual FileFlux library
        var content = await File.ReadAllTextAsync(filePath);
        var chunks = new List<IDocumentChunk>();

        var chunkSize = options.MaxSize;
        var overlap = options.OverlapSize;

        for (int i = 0; i < content.Length; i += chunkSize - overlap)
        {
            var chunkContent = content.Substring(i, Math.Min(chunkSize, content.Length - i));
            chunks.Add(new DocumentChunkImpl(chunkContent, chunks.Count, i, i + chunkContent.Length));
        }

        return chunks;
    }
}

/// <summary>
/// Simple implementation of IDocumentChunk
/// </summary>
internal class DocumentChunkImpl : IDocumentChunk
{
    public string Content { get; }
    public int ChunkIndex { get; }
    public int StartPosition { get; }
    public int EndPosition { get; }
    public Dictionary<string, object>? Properties { get; }

    public DocumentChunkImpl(string content, int chunkIndex, int startPosition, int endPosition)
    {
        Content = content;
        ChunkIndex = chunkIndex;
        StartPosition = startPosition;
        EndPosition = endPosition;
        Properties = new Dictionary<string, object>();
    }
}