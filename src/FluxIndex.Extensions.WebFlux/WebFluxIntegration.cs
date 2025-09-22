using FluxIndex.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// WebFlux integration for FluxIndex - processes web content using WebFlux library
/// </summary>
public class WebFluxIntegration
{
    private readonly FluxIndexContext _fluxIndexContext;
    private readonly ILogger<WebFluxIntegration> _logger;

    public WebFluxIntegration(
        FluxIndexContext fluxIndexContext,
        ILogger<WebFluxIntegration> logger)
    {
        _fluxIndexContext = fluxIndexContext;
        _logger = logger;
    }

    /// <summary>
    /// Process and index web content from a URL
    /// </summary>
    public async Task<string> IndexWebContentAsync(
        string url,
        WebFluxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= WebFluxOptions.Default;

        _logger.LogInformation("Processing web content from URL: {Url}", url);

        try
        {
            var document = await ProcessWebContentToDocumentAsync(url, options, cancellationToken);

            var documentId = await _fluxIndexContext.Indexer.IndexDocumentAsync(document, cancellationToken);

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
        WebFluxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= WebFluxOptions.Default;

        var crawlOptions = new CrawlOptions
        {
            MaxDepth = options.MaxDepth,
            FollowExternalLinks = options.FollowExternalLinks,
            ChunkingStrategy = options.ChunkingStrategy,
            MaxChunkSize = options.MaxChunkSize,
            ChunkOverlap = options.ChunkOverlap,
            IncludeImages = options.IncludeImages
        };

        var chunks = new List<DocumentChunk>();
        var documentMetadata = new Dictionary<string, object>
        {
            ["source_url"] = url,
            ["source_type"] = "web",
            ["processed_at"] = DateTime.UtcNow,
            ["webflux_version"] = "0.1.1"
        };

        // Use the WebFlux library to process web content
        // This is a placeholder implementation until the exact WebFlux API is verified
        try
        {
            // Simplified implementation for web content processing
            var content = await ProcessWebContentSimpleAsync(url, crawlOptions, cancellationToken);

            if (!string.IsNullOrEmpty(content))
            {
                // Create chunks from the processed content
                var chunkSize = crawlOptions.MaxChunkSize ?? 512;
                var overlap = crawlOptions.ChunkOverlap ?? 64;

                for (int i = 0; i < content.Length; i += chunkSize - overlap)
                {
                    var chunkContent = content.Substring(i, Math.Min(chunkSize, content.Length - i));
                    var documentChunk = ConvertToDocumentChunk(chunkContent, chunks.Count);
                    chunks.Add(documentChunk);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to process web content: {Error}", ex.Message);
            throw;
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
    /// Batch process multiple URLs
    /// </summary>
    public async Task<IEnumerable<string>> IndexMultipleUrlsAsync(
        IEnumerable<string> urls,
        WebFluxOptions? options = null,
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
    /// Simple web content processing method - placeholder until WebFlux API is verified
    /// </summary>
    private async Task<string> ProcessWebContentSimpleAsync(string url, CrawlOptions crawlOptions, CancellationToken cancellationToken)
    {
        // This is a simplified implementation for demonstration
        // In a real implementation, this would use the WebFlux IWebContentProcessor
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            var response = await httpClient.GetStringAsync(url, cancellationToken);

            // Basic HTML stripping (very simple implementation)
            // Real implementation would use WebFlux for proper content extraction
            var content = System.Text.RegularExpressions.Regex.Replace(response, "<[^>]*>", " ");
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ").Trim();

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch content from URL: {Url}", url);
            return string.Empty;
        }
    }

    private DocumentChunk ConvertToDocumentChunk(string content, int chunkIndex, Dictionary<string, object>? metadata = null)
    {
        var chunk = new DocumentChunk(content ?? string.Empty, chunkIndex)
        {
            TokenCount = EstimateTokenCount(content ?? string.Empty),
            CreatedAt = DateTime.UtcNow
        };

        // Add WebFlux metadata to chunk
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                chunk.AddProperty($"webflux_{kvp.Key}", kvp.Value);
            }
        }

        // Add additional metadata
        if (chunk.Metadata == null)
            chunk.Metadata = new Dictionary<string, object>();

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
/// Configuration options for WebFlux integration
/// </summary>
public class WebFluxOptions
{
    /// <summary>
    /// Maximum crawling depth (default: 1 for single page)
    /// </summary>
    public int MaxDepth { get; set; } = 1;

    /// <summary>
    /// Whether to follow external links (default: false)
    /// </summary>
    public bool FollowExternalLinks { get; set; } = false;

    /// <summary>
    /// Chunking strategy for content processing
    /// </summary>
    public string ChunkingStrategy { get; set; } = "Smart";

    /// <summary>
    /// Maximum size of each content chunk
    /// </summary>
    public int MaxChunkSize { get; set; } = 512;

    /// <summary>
    /// Overlap size between chunks
    /// </summary>
    public int ChunkOverlap { get; set; } = 64;

    /// <summary>
    /// Whether to include image processing
    /// </summary>
    public bool IncludeImages { get; set; } = false;

    /// <summary>
    /// Default configuration
    /// </summary>
    public static WebFluxOptions Default => new();

    /// <summary>
    /// Configuration for deep crawling
    /// </summary>
    public static WebFluxOptions DeepCrawl => new()
    {
        MaxDepth = 3,
        FollowExternalLinks = false,
        ChunkingStrategy = "Intelligent",
        MaxChunkSize = 1024,
        ChunkOverlap = 128
    };

    /// <summary>
    /// Configuration for large content processing
    /// </summary>
    public static WebFluxOptions LargeContent => new()
    {
        MaxDepth = 1,
        ChunkingStrategy = "Auto",
        MaxChunkSize = 2048,
        ChunkOverlap = 256,
        IncludeImages = true
    };
}

/// <summary>
/// Internal crawl options class - placeholder until WebFlux API is verified
/// </summary>
internal class CrawlOptions
{
    public int MaxDepth { get; set; } = 1;
    public bool FollowExternalLinks { get; set; } = false;
    public string ChunkingStrategy { get; set; } = "Smart";
    public int? MaxChunkSize { get; set; } = 512;
    public int? ChunkOverlap { get; set; } = 64;
    public bool IncludeImages { get; set; } = false;
}