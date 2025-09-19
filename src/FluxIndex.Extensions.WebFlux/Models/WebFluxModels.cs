namespace FluxIndex.Extensions.WebFlux.Models;

/// <summary>
/// Web crawling options
/// </summary>
public class CrawlOptions
{
    public int MaxDepth { get; set; } = 3;
    public int MaxPages { get; set; } = 100;
    public TimeSpan DelayBetweenRequests { get; set; } = TimeSpan.FromMilliseconds(500);
    public bool RespectRobotsTxt { get; set; } = true;
    public string UserAgent { get; set; } = "FluxIndex-WebCrawler/1.0";
    public int MaxConcurrentRequests { get; set; } = 5;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public int RetryCount { get; set; } = 3;
    public IEnumerable<string>? AllowedDomains { get; set; }
    public IEnumerable<string>? ExcludePatterns { get; set; }
    public IEnumerable<string>? IncludePatterns { get; set; }
    public int MinContentLength { get; set; } = 100;
    public int MaxContentLength { get; set; } = 1000000;
    public DateTime? IfModifiedSince { get; set; }
    public string Strategy { get; set; } = "BreadthFirst";
}

/// <summary>
/// Web content chunking options
/// </summary>
public class ChunkingOptions
{
    public string Strategy { get; set; } = "Auto";
    public int MaxChunkSize { get; set; } = 512;
    public int OverlapSize { get; set; } = 64;
    public bool PreserveStructure { get; set; } = true;
    public double QualityThreshold { get; set; } = 0.7;
}

/// <summary>
/// Web content chunk
/// </summary>
public class WebContentChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public double Quality { get; set; }
    public string? Strategy { get; set; }
    public WebContentMetadata? Metadata { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Web content metadata
/// </summary>
public class WebContentMetadata
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public List<string> Keywords { get; set; } = new();
    public string? Language { get; set; }
    public string? ContentType { get; set; }
    public DateTime? LastModified { get; set; }
    public int ContentLength { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// Raw web content before processing
/// </summary>
public class RawWebContent
{
    public string Url { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public WebContentMetadata Metadata { get; set; } = new();
    public byte[]? RawData { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}

/// <summary>
/// Parsed web content after structure analysis
/// </summary>
public class ParsedWebContent
{
    public string Url { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public WebContentMetadata Metadata { get; set; } = new();
    public List<string> ExtractedLinks { get; set; } = new();
    public List<string> ExtractedImages { get; set; } = new();
    public Dictionary<string, object> StructuralElements { get; set; } = new();
}

/// <summary>
/// Web content processing result
/// </summary>
public class WebProcessingResult
{
    public bool IsSuccess { get; set; }
    public List<WebContentChunk>? Result { get; set; }
    public WebProcessingProgress? Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
}

/// <summary>
/// Web content processing progress
/// </summary>
public class WebProcessingProgress
{
    public int PagesProcessed { get; set; }
    public int TotalPages { get; set; }
    public double PercentComplete => TotalPages > 0 ? (double)PagesProcessed / TotalPages * 100 : 0;
    public TimeSpan? EstimatedRemainingTime { get; set; }
    public string CurrentUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Content extractor interface
/// </summary>
public interface IContentExtractor
{
    string ExtractorType { get; }
    IEnumerable<string> SupportedContentTypes { get; }
    bool CanExtract(string contentType, string url);
    Task<RawWebContent> ExtractAsync(string url, HttpResponseMessage response, CancellationToken cancellationToken = default);
}

/// <summary>
/// Web content processor interface
/// </summary>
public interface IWebContentProcessor
{
    Task<IEnumerable<WebContentChunk>> ProcessAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<WebProcessingResult> ProcessWithProgressAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<RawWebContent>> CrawlAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        CancellationToken cancellationToken = default);

    Task<RawWebContent> ExtractAsync(
        string url,
        CancellationToken cancellationToken = default);

    Task<ParsedWebContent> ParseAsync(
        RawWebContent rawContent,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<WebContentChunk>> ChunkAsync(
        ParsedWebContent parsedContent,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default);
}