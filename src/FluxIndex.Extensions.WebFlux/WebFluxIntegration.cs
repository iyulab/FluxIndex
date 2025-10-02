using FluxIndex.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using WebFlux.Core.Interfaces;
using WebFlux.Core.Models;
using WebFlux.Core.Options;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// WebFlux integration for FluxIndex - processes web content using WebFlux library
/// </summary>
public class WebFluxIntegration
{
    private readonly IWebContentProcessor _webContentProcessor;
    private readonly Indexer _indexer;
    private readonly ILogger<WebFluxIntegration> _logger;
    private readonly WebFluxOptions _options;

    public WebFluxIntegration(
        IWebContentProcessor webContentProcessor,
        Indexer indexer,
        ILogger<WebFluxIntegration> logger,
        Microsoft.Extensions.Options.IOptions<WebFluxOptions>? options = null)
    {
        _webContentProcessor = webContentProcessor;
        _indexer = indexer;
        _logger = logger;
        _options = options?.Value ?? new WebFluxOptions();
    }

    /// <summary>
    /// Process and index web content from a URL using WebFlux 0.1.2 API
    /// </summary>
    public async Task<string> IndexWebContentAsync(
        string url,
        WebFluxProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WebFluxProcessingOptions
        {
            ChunkingStrategy = _options.DefaultChunkingStrategy,
            MaxChunkSize = _options.DefaultMaxChunkSize,
            ChunkOverlap = _options.DefaultChunkOverlap
        };

        _logger.LogInformation("Processing web content from URL: {Url}", url);

        try
        {
            var document = await ProcessWebContentToDocumentAsync(url, options, cancellationToken);

            var documentId = await _indexer.IndexDocumentAsync(document, cancellationToken);

            _logger.LogInformation("Successfully indexed web content. DocumentId: {DocumentId}, Chunks: {ChunkCount}",
                documentId, document.Chunks?.Count ?? 0);

            return documentId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index web content from URL: {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Process web content and return FluxIndex Document without indexing
    /// </summary>
    public async Task<Document> ProcessWebContentToDocumentAsync(
        string url,
        WebFluxProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WebFluxProcessingOptions
        {
            ChunkingStrategy = _options.DefaultChunkingStrategy,
            MaxChunkSize = _options.DefaultMaxChunkSize,
            ChunkOverlap = _options.DefaultChunkOverlap
        };

        var chunkingOptions = new ChunkingOptions
        {
            Strategy = options.ChunkingStrategy,
            MaxChunkSize = options.MaxChunkSize,
            ChunkOverlap = options.ChunkOverlap,
            IncludeImageDescriptions = options.IncludeImages,
            IncludeMetadata = true
        };

        var chunks = new List<DocumentChunk>();
        var documentMetadata = new Dictionary<string, object>
        {
            ["source_url"] = url,
            ["source_type"] = "web",
            ["processed_at"] = DateTime.UtcNow,
            ["webflux_version"] = "0.1.2"
        };

        // Use WebFlux streaming API
        if (_options.UseStreamingApi)
        {
            await foreach (var webChunk in _webContentProcessor.ProcessWebsiteAsync(url, null, chunkingOptions, cancellationToken))
            {
                var documentChunk = ConvertToDocumentChunk(webChunk, chunks.Count, url);
                chunks.Add(documentChunk);
            }
        }
        else
        {
            // Use non-streaming API
            var webChunks = await _webContentProcessor.ProcessUrlAsync(url, chunkingOptions, cancellationToken);
            foreach (var webChunk in webChunks)
            {
                var documentChunk = ConvertToDocumentChunk(webChunk, chunks.Count, url);
                chunks.Add(documentChunk);
            }
        }

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException($"No content chunks were extracted from URL: {url}");
        }

        // Create FluxIndex Document
        var document = new Document
        {
            Id = GenerateDocumentId(url),
            FileName = ExtractFileNameFromUrl(url),
            FilePath = url,
            Content = string.Join("\n\n", chunks.Select(c => c.Content)),
            Metadata = documentMetadata,
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Chunks = chunks
        };

        // Set document ID for all chunks
        foreach (var chunk in chunks)
        {
            chunk.DocumentId = document.Id;
        }

        return document;
    }

    /// <summary>
    /// Batch process multiple URLs using WebFlux API
    /// </summary>
    public async Task<IEnumerable<string>> IndexMultipleUrlsAsync(
        IEnumerable<string> urls,
        WebFluxProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var documentIds = new List<string>();

        foreach (var url in urls)
        {
            try
            {
                var documentId = await IndexWebContentAsync(url, options, cancellationToken);
                documentIds.Add(documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process URL: {Url}", url);
            }
        }

        return documentIds;
    }

    private DocumentChunk ConvertToDocumentChunk(WebContentChunk webChunk, int chunkIndex, string sourceUrl)
    {
        var chunk = new DocumentChunk(webChunk.Content ?? string.Empty, chunkIndex)
        {
            TokenCount = EstimateTokenCount(webChunk.Content ?? string.Empty),
            CreatedAt = DateTime.UtcNow
        };

        // Map WebFlux metadata to FluxIndex
        if (chunk.Metadata == null)
            chunk.Metadata = new Dictionary<string, object>();

        chunk.Metadata["wf_chunk_id"] = webChunk.Id;
        chunk.Metadata["wf_sequence_number"] = webChunk.SequenceNumber;
        chunk.Metadata["wf_source_url"] = webChunk.SourceUrl;
        chunk.Metadata["wf_quality_score"] = webChunk.QualityScore;
        chunk.Metadata["wf_chunk_type"] = webChunk.Type.ToString();

        // Map title if available
        if (!string.IsNullOrEmpty(webChunk.Title))
        {
            chunk.Metadata["wf_title"] = webChunk.Title;
        }

        // Map WebFlux metadata
        if (webChunk.Metadata != null)
        {
            if (!string.IsNullOrEmpty(webChunk.Metadata.Title))
                chunk.Metadata["wf_page_title"] = webChunk.Metadata.Title;

            if (!string.IsNullOrEmpty(webChunk.Metadata.Description))
                chunk.Metadata["wf_description"] = webChunk.Metadata.Description;

            if (webChunk.Metadata.PublishedDate.HasValue)
                chunk.Metadata["wf_published_date"] = webChunk.Metadata.PublishedDate.Value;

            if (webChunk.Metadata.ModifiedDate.HasValue)
                chunk.Metadata["wf_modified_date"] = webChunk.Metadata.ModifiedDate.Value;

            if (!string.IsNullOrEmpty(webChunk.Metadata.Author))
                chunk.Metadata["wf_author"] = webChunk.Metadata.Author;

            if (webChunk.Metadata.Keywords != null && webChunk.Metadata.Keywords.Any())
                chunk.Metadata["wf_keywords"] = string.Join(", ", webChunk.Metadata.Keywords);
        }

        // Map chunking strategy info
        if (webChunk.StrategyInfo != null)
        {
            chunk.Metadata["wf_strategy"] = webChunk.StrategyInfo.StrategyName;
            if (webChunk.StrategyInfo.Parameters != null && webChunk.StrategyInfo.Parameters.Any())
            {
                foreach (var param in webChunk.StrategyInfo.Parameters)
                {
                    chunk.Metadata[$"wf_param_{param.Key}"] = param.Value;
                }
            }
        }

        // Map tags
        if (webChunk.Tags != null && webChunk.Tags.Any())
        {
            chunk.Metadata["wf_tags"] = string.Join(", ", webChunk.Tags);
        }

        // Map related images
        if (webChunk.RelatedImageUrls != null && webChunk.RelatedImageUrls.Any())
        {
            chunk.Metadata["wf_images"] = string.Join(", ", webChunk.RelatedImageUrls);
        }

        // Preserve additional metadata
        if (webChunk.AdditionalMetadata != null)
        {
            foreach (var prop in webChunk.AdditionalMetadata)
            {
                chunk.Metadata[$"wf_{prop.Key}"] = prop.Value;
            }
        }

        chunk.Metadata["content_type"] = "text/html";
        chunk.Metadata["source_type"] = "web";

        return chunk;
    }

    private static string GenerateDocumentId(string url)
    {
        var uri = new Uri(url);
        var documentId = $"web_{uri.Host}_{uri.AbsolutePath}"
            .Replace('/', '_')
            .Replace('.', '_')
            .Replace('-', '_')
            .ToLowerInvariant();

        return $"{documentId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        var uri = new Uri(url);
        var fileName = Path.GetFileName(uri.AbsolutePath);

        if (string.IsNullOrEmpty(fileName) || fileName == "/")
        {
            fileName = $"{uri.Host}.html";
        }

        return fileName;
    }

    private static int EstimateTokenCount(string text)
    {
        // Simple token estimation: ~4 characters per token
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}

/// <summary>
/// Configuration options for WebFlux integration with FluxIndex
/// </summary>
public class WebFluxOptions
{
    /// <summary>
    /// Default chunking strategy for content processing (Auto, Smart, Semantic, Intelligent, MemoryOptimized, Paragraph, FixedSize)
    /// </summary>
    public ChunkingStrategyType DefaultChunkingStrategy { get; set; } = ChunkingStrategyType.Auto;

    /// <summary>
    /// Default maximum size of each content chunk in tokens
    /// </summary>
    public int DefaultMaxChunkSize { get; set; } = 512;

    /// <summary>
    /// Default overlap size between chunks in tokens
    /// </summary>
    public int DefaultChunkOverlap { get; set; } = 50;

    /// <summary>
    /// Whether to include image processing (default: false, requires IImageToTextService)
    /// </summary>
    public bool DefaultIncludeImages { get; set; } = false;

    /// <summary>
    /// Enable streaming API for memory-efficient processing of large websites
    /// </summary>
    public bool UseStreamingApi { get; set; } = true;
}

/// <summary>
/// Processing options for individual WebFlux operations
/// </summary>
public class WebFluxProcessingOptions
{
    /// <summary>
    /// Chunking strategy for content processing
    /// </summary>
    public ChunkingStrategyType ChunkingStrategy { get; set; } = ChunkingStrategyType.Auto;

    /// <summary>
    /// Maximum size of each content chunk
    /// </summary>
    public int MaxChunkSize { get; set; } = 512;

    /// <summary>
    /// Overlap size between chunks
    /// </summary>
    public int ChunkOverlap { get; set; } = 50;

    /// <summary>
    /// Whether to include image processing
    /// </summary>
    public bool IncludeImages { get; set; } = false;
}
