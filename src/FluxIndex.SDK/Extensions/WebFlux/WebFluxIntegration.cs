using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;
using WebFlux.Core.Interfaces;
using WebFlux.Core.Models;
using WebFlux.Core.Options;
using CrawlProgressModel = WebFlux.Core.Models.CrawlProgress;

namespace FluxIndex.SDK.Extensions.WebFlux;

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
            ["webflux_version"] = "0.1.7",
            ["chunking_strategy"] = options.ChunkingStrategy.ToString()
        };

        // Diagnostic info for better error messages
        bool hasNetworkError = false;
        Exception? processingException = null;

        try
        {
            // Use WebFlux streaming API
            if (_options.UseStreamingApi)
            {
                await foreach (var webChunk in _webContentProcessor.ProcessWebsiteAsync(url, options.CrawlOptions, chunkingOptions, cancellationToken))
                {
                    var documentChunk = ConvertToDocumentChunk(webChunk, chunks.Count, url);
                    chunks.Add(documentChunk);
                }
            }
            else
            {
                // Use non-streaming API
                var webChunks = await _webContentProcessor.ProcessUrlAsync(url, chunkingOptions, cancellationToken);

                _logger.LogInformation("=== WEBFLUX CHUNKING: Generated {Count} chunks from URL ===", webChunks.Count);

                foreach (var webChunk in webChunks)
                {
                    var estimatedTokens = webChunk.Content.Length / 4;
                    _logger.LogInformation("=== WEBFLUX CHUNK {Seq}/{Total}: {Length} chars (~{Tokens} tokens, quality: {Quality}) ===",
                        webChunk.SequenceNumber, webChunks.Count, webChunk.Content.Length, estimatedTokens, webChunk.QualityScore);

                    if (estimatedTokens > 8000)
                    {
                        _logger.LogWarning("=== WEBFLUX CHUNK {Seq} EXCEEDS TOKEN LIMIT: ~{Tokens} tokens ===",
                            webChunk.SequenceNumber, estimatedTokens);
                    }

                    var documentChunk = ConvertToDocumentChunk(webChunk, chunks.Count, url);
                    chunks.Add(documentChunk);
                }
            }
        }
        catch (HttpRequestException httpEx)
        {
            hasNetworkError = true;
            processingException = httpEx;
        }
        catch (Exception ex)
        {
            processingException = ex;
        }

        if (chunks.Count == 0)
        {
            var errorMessage = BuildDetailedErrorMessage(url, hasNetworkError, processingException, chunkingOptions);
            throw new InvalidOperationException(errorMessage, processingException);
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
    /// Batch process multiple URLs using WebFlux API (sequential processing)
    /// </summary>
    [Obsolete("Use IndexMultipleUrlsBatchAsync for better performance with parallel processing")]
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

    /// <summary>
    /// Batch process multiple URLs using WebFlux batch API (5-10x faster than sequential processing)
    /// </summary>
    public async Task<BatchIndexingResult> IndexMultipleUrlsBatchAsync(
        IEnumerable<string> urls,
        WebFluxProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
        => await IndexMultipleUrlsBatchAsync(urls, options, null, cancellationToken);

    /// <summary>
    /// Batch process multiple URLs with progress reporting (WebFlux 0.1.6+)
    /// </summary>
    public async Task<BatchIndexingResult> IndexMultipleUrlsBatchAsync(
        IEnumerable<string> urls,
        WebFluxProcessingOptions? options,
        IProgress<WebFluxCrawlProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var urlList = urls.ToList();
        _logger.LogInformation("Batch processing {UrlCount} URLs with WebFlux", urlList.Count);

        var result = new BatchIndexingResult
        {
            TotalUrls = urlList.Count,
            StartTime = DateTime.UtcNow
        };

        try
        {
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = options?.ChunkingStrategy ?? _options.DefaultChunkingStrategy,
                MaxChunkSize = options?.MaxChunkSize ?? _options.DefaultMaxChunkSize,
                ChunkOverlap = options?.ChunkOverlap ?? _options.DefaultChunkOverlap
            };

            // Use WebFlux batch processing API
            var batchResults = await _webContentProcessor.ProcessUrlsBatchAsync(urlList, chunkingOptions, cancellationToken);

            foreach (var kvp in batchResults)
            {
                var url = kvp.Key;
                var webChunks = kvp.Value;

                try
                {
                    if (webChunks.Count == 0)
                    {
                        _logger.LogWarning("No chunks generated for URL: {Url}", url);
                        result.FailedUrls.Add(url, "No chunks generated");
                        continue;
                    }

                    // Convert WebFlux chunks to FluxIndex document
                    var document = await ConvertToFluxIndexDocumentAsync(webChunks, url, cancellationToken);

                    // Index with FluxIndex
                    var documentId = await _indexer.IndexDocumentAsync(document, cancellationToken);

                    result.SuccessfulUrls.Add(url, documentId);
                    result.TotalChunksIndexed += webChunks.Count;

                    _logger.LogDebug("Successfully indexed {ChunkCount} chunks from {Url} as document {DocumentId}",
                        webChunks.Count, url, documentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to index URL: {Url}", url);
                    result.FailedUrls.Add(url, ex.Message);
                }
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;

            _logger.LogInformation(
                "Batch processing completed: {SuccessCount}/{TotalCount} URLs indexed successfully in {Duration}ms ({ChunkCount} total chunks)",
                result.SuccessfulUrls.Count,
                result.TotalUrls,
                result.Duration.TotalMilliseconds,
                result.TotalChunksIndexed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch processing failed");
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            throw;
        }
    }

    /// <summary>
    /// Convert WebFlux chunks to FluxIndex document
    /// </summary>
    private async Task<Document> ConvertToFluxIndexDocumentAsync(
        IReadOnlyList<WebContentChunk> webChunks,
        string url,
        CancellationToken cancellationToken = default)
    {
        var documentId = GenerateDocumentId(url);
        var document = new Document
        {
            Id = documentId,
            FileName = ExtractFileNameFromUrl(url),
            FilePath = url,
            Content = string.Join("\n\n", webChunks.Select(c => c.Content)),
            Metadata = new Dictionary<string, object>
            {
                ["source_url"] = url,
                ["source_type"] = "web",
                ["processed_at"] = DateTime.UtcNow,
                ["webflux_version"] = "0.1.7",
                ["chunk_count"] = webChunks.Count
            },
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Chunks = new List<DocumentChunk>()
        };

        // Convert WebFlux chunks to FluxIndex chunks
        for (int i = 0; i < webChunks.Count; i++)
        {
            var webChunk = webChunks[i];
            var fluxChunk = ConvertToDocumentChunk(webChunk, i, url);
            fluxChunk.DocumentId = documentId;
            ((List<DocumentChunk>)document.Chunks).Add(fluxChunk);
        }

        return await Task.FromResult(document);
    }

    private DocumentChunk ConvertToDocumentChunk(WebContentChunk webChunk, int chunkIndex, string sourceUrl)
    {
        var chunk = new DocumentChunk(webChunk.Content, chunkIndex)
        {
            TokenCount = EstimateTokenCount(webChunk.Content),
            CreatedAt = DateTime.UtcNow
        };

        // Map WebFlux metadata to FluxIndex
        if (chunk.Metadata == null)
            chunk.Metadata = new Dictionary<string, object>();

        // Core WebFlux chunk properties
        chunk.Metadata["wf_chunk_id"] = webChunk.Id;
        chunk.Metadata["wf_sequence_number"] = webChunk.SequenceNumber;
        chunk.Metadata["wf_source_url"] = webChunk.SourceUrl;
        chunk.Metadata["wf_quality_score"] = webChunk.QualityScore;
        chunk.Metadata["wf_chunk_type"] = webChunk.Type.ToString();
        chunk.Metadata["wf_created_at"] = webChunk.CreatedAt;

        // Map title if available
        if (!string.IsNullOrEmpty(webChunk.Title))
        {
            chunk.Metadata["wf_title"] = webChunk.Title;
        }

        // Map parent-child relationships
        if (!string.IsNullOrEmpty(webChunk.ParentChunkId))
        {
            chunk.Metadata["wf_parent_chunk_id"] = webChunk.ParentChunkId;
        }

        if (webChunk.ChildChunkIds?.Any() == true)
        {
            chunk.Metadata["wf_child_chunk_ids"] = string.Join(",", webChunk.ChildChunkIds);
        }

        // Map WebFlux page metadata
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

            if (webChunk.Metadata.Keywords?.Any() == true)
                chunk.Metadata["wf_keywords"] = string.Join(", ", webChunk.Metadata.Keywords);

            if (!string.IsNullOrEmpty(webChunk.Metadata.Language))
                chunk.Metadata["wf_language"] = webChunk.Metadata.Language;
        }

        // Map chunking strategy info
        if (webChunk.StrategyInfo != null)
        {
            chunk.Metadata["wf_strategy"] = webChunk.StrategyInfo.StrategyName;
            chunk.Metadata["wf_processing_time_ms"] = webChunk.StrategyInfo.ProcessingTimeMs;

            if (webChunk.StrategyInfo.Parameters?.Any() == true)
            {
                foreach (var param in webChunk.StrategyInfo.Parameters)
                {
                    chunk.Metadata[$"wf_param_{param.Key}"] = param.Value;
                }
            }
        }

        // Map tags
        if (webChunk.Tags?.Any() == true)
        {
            chunk.Metadata["wf_tags"] = string.Join(", ", webChunk.Tags);
        }

        // Map related images
        if (webChunk.RelatedImageUrls?.Any() == true)
        {
            chunk.Metadata["wf_images"] = string.Join(", ", webChunk.RelatedImageUrls);
            chunk.Metadata["wf_image_count"] = webChunk.RelatedImageUrls.Count;
        }

        // Preserve additional metadata
        if (webChunk.AdditionalMetadata?.Any() == true)
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

    private static string BuildDetailedErrorMessage(
        string url,
        bool hasNetworkError,
        Exception? processingException,
        ChunkingOptions chunkingOptions)
    {
        var errorMessage = new System.Text.StringBuilder();
        errorMessage.AppendLine($"Failed to extract content from URL: {url}");
        errorMessage.AppendLine();

        // Network error
        if (hasNetworkError)
        {
            errorMessage.AppendLine("⚠️  Network Error:");
            errorMessage.AppendLine("   The URL could not be accessed. Possible causes:");
            errorMessage.AppendLine("   • The URL is incorrect or no longer exists (404 Not Found)");
            errorMessage.AppendLine("   • The server is temporarily unavailable (503 Service Unavailable)");
            errorMessage.AppendLine("   • Network connectivity issues");
            errorMessage.AppendLine("   • The site is blocking automated access (403 Forbidden)");
            if (processingException != null)
            {
                errorMessage.AppendLine($"   • Technical details: {processingException.Message}");
            }
        }
        // Processing error (HTML fetched but no content extracted)
        else if (processingException != null)
        {
            errorMessage.AppendLine("⚠️  Content Processing Error:");
            errorMessage.AppendLine("   The page was accessed but no extractable content was found.");
            errorMessage.AppendLine($"   Error: {processingException.Message}");
        }
        // No error exception but still no content (most common case)
        else
        {
            errorMessage.AppendLine("⚠️  Content Extraction Failed:");
            errorMessage.AppendLine("   The page was accessed successfully, but WebFlux could not extract any");
            errorMessage.AppendLine("   meaningful text content. This usually happens when:");
            errorMessage.AppendLine();
            errorMessage.AppendLine("   1. JavaScript-Rendered Content (SPA):");
            errorMessage.AppendLine("      • The page uses client-side rendering (React, Vue, Angular, etc.)");
            errorMessage.AppendLine("      • Content is loaded dynamically after the initial HTML");
            errorMessage.AppendLine("      • WebFlux can only parse static HTML, not execute JavaScript");
            errorMessage.AppendLine();
            errorMessage.AppendLine("   2. Empty or Minimal Content:");
            errorMessage.AppendLine("      • The page contains mostly scripts, styles, or navigation");
            errorMessage.AppendLine("      • No substantial text content in standard HTML tags");
            errorMessage.AppendLine("      • Content may be hidden behind authentication or paywalls");
            errorMessage.AppendLine();
            errorMessage.AppendLine("   3. Non-Standard HTML Structure:");
            errorMessage.AppendLine("      • Unusual DOM structure that WebFlux cannot parse");
            errorMessage.AppendLine("      • Content in custom elements or web components");
            errorMessage.AppendLine("      • Heavy use of iframes or embedded content");
        }

        errorMessage.AppendLine();
        errorMessage.AppendLine("💡 Suggestions:");
        errorMessage.AppendLine("   • Try a different URL from the same website (e.g., a specific article or doc page)");
        errorMessage.AppendLine("   • Verify the URL in a browser to ensure it has readable text content");
        errorMessage.AppendLine("   • Check if the site uses server-side rendering (SSR) instead of client-side");
        errorMessage.AppendLine("   • For JavaScript-heavy sites, consider using a headless browser approach");
        errorMessage.AppendLine();
        errorMessage.AppendLine("📋 Processing Configuration Used:");
        errorMessage.AppendLine($"   • Strategy: {chunkingOptions.Strategy}");
        errorMessage.AppendLine($"   • Max Chunk Size: {chunkingOptions.MaxChunkSize}");
        errorMessage.AppendLine($"   • Chunk Overlap: {chunkingOptions.ChunkOverlap}");

        return errorMessage.ToString();
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

    /// <summary>
    /// Optional crawl options for dynamic rendering and SPA support
    /// </summary>
    public CrawlOptions? CrawlOptions { get; set; }
}

/// <summary>
/// Result of batch indexing operation
/// </summary>
public class BatchIndexingResult
{
    /// <summary>
    /// Total number of URLs processed
    /// </summary>
    public int TotalUrls { get; set; }

    /// <summary>
    /// Successfully indexed URLs with their document IDs
    /// </summary>
    public Dictionary<string, string> SuccessfulUrls { get; set; } = new();

    /// <summary>
    /// Failed URLs with error messages
    /// </summary>
    public Dictionary<string, string> FailedUrls { get; set; } = new();

    /// <summary>
    /// Total number of chunks indexed
    /// </summary>
    public int TotalChunksIndexed { get; set; }

    /// <summary>
    /// Processing start time
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Processing end time
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Total duration
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Success rate (0.0 - 1.0)
    /// </summary>
    public double SuccessRate => TotalUrls > 0 ? (double)SuccessfulUrls.Count / TotalUrls : 0;

    /// <summary>
    /// Average chunks per URL
    /// </summary>
    public double AverageChunksPerUrl => SuccessfulUrls.Count > 0 ? (double)TotalChunksIndexed / SuccessfulUrls.Count : 0;
}

/// <summary>
/// Progress information for WebFlux crawling operations (WebFlux 0.1.6+)
/// </summary>
public class WebFluxCrawlProgress
{
    /// <summary>
    /// Total number of URLs to process
    /// </summary>
    public int TotalUrls { get; init; }

    /// <summary>
    /// Number of URLs processed so far
    /// </summary>
    public int ProcessedUrls { get; init; }

    /// <summary>
    /// Number of successfully processed URLs
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of failed URLs
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Total chunks generated so far
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Currently processing URL
    /// </summary>
    public string CurrentUrl { get; init; } = string.Empty;

    /// <summary>
    /// Elapsed time since start
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Estimated remaining time
    /// </summary>
    public TimeSpan EstimatedRemaining { get; init; }

    /// <summary>
    /// Progress percentage (0.0 - 1.0)
    /// </summary>
    public double ProgressPercentage => TotalUrls > 0 ? (double)ProcessedUrls / TotalUrls : 0.0;

    /// <summary>
    /// Success rate (0.0 - 1.0)
    /// </summary>
    public double SuccessRate => ProcessedUrls > 0 ? (double)SuccessCount / ProcessedUrls : 0.0;

    /// <summary>
    /// URLs processed per second
    /// </summary>
    public double UrlsPerSecond => ElapsedTime.TotalSeconds > 0 ? ProcessedUrls / ElapsedTime.TotalSeconds : 0.0;

    /// <summary>
    /// Recent errors (last 5)
    /// </summary>
    public IReadOnlyList<string> RecentErrors { get; init; } = Array.Empty<string>();
}
