using FluxIndex.Extensions.WebFlux.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace FluxIndex.Extensions.WebFlux.Services;

/// <summary>
/// Simple web content processor implementation without complex async enumerable
/// </summary>
public class SimpleWebContentProcessor : IWebContentProcessor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SimpleWebContentProcessor> _logger;

    public SimpleWebContentProcessor(
        HttpClient httpClient,
        ILogger<SimpleWebContentProcessor> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<WebContentChunk>> ProcessAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        try
        {
            // Simple processing: Crawl -> Extract -> Parse -> Chunk
            var crawlResults = await CrawlAsync(url, crawlOptions, cancellationToken);
            var chunks = new List<WebContentChunk>();

            foreach (var crawlResult in crawlResults)
            {
                var rawContent = await ExtractAsync(crawlResult.Url, cancellationToken);
                var parsedContent = await ParseAsync(rawContent, cancellationToken);
                var resultChunks = await ChunkAsync(parsedContent, chunkingOptions, cancellationToken);
                chunks.AddRange(resultChunks);
            }

            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process URL: {Url}", url);
            return Array.Empty<WebContentChunk>();
        }
    }

    public async IAsyncEnumerable<WebProcessingResult> ProcessWithProgressAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        // Simple implementation without try-catch around yield
        yield return new WebProcessingResult
        {
            IsSuccess = true,
            Progress = new WebProcessingProgress
            {
                Status = "Starting",
                CurrentUrl = url,
                PagesProcessed = 0,
                TotalPages = 1
            }
        };

        var result = await ProcessAsync(url, crawlOptions, chunkingOptions, cancellationToken);

        yield return new WebProcessingResult
        {
            IsSuccess = true,
            Result = result.ToList(),
            Progress = new WebProcessingProgress
            {
                Status = "Completed",
                CurrentUrl = url,
                PagesProcessed = 1,
                TotalPages = 1
            }
        };
    }

    public async Task<IEnumerable<RawWebContent>> CrawlAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/html";

            var rawContent = new RawWebContent
            {
                Url = url,
                Content = content,
                ContentType = contentType,
                Metadata = new WebContentMetadata
                {
                    ContentLength = content.Length,
                    LastModified = response.Content.Headers.LastModified?.DateTime,
                    ContentType = contentType
                }
            };

            return new[] { rawContent };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to crawl URL: {Url}", url);
            return Array.Empty<RawWebContent>();
        }
    }

    public async Task<RawWebContent> ExtractAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/html";

        return new RawWebContent
        {
            Url = url,
            Content = content,
            ContentType = contentType,
            Metadata = new WebContentMetadata
            {
                ContentLength = content.Length,
                LastModified = response.Content.Headers.LastModified?.DateTime,
                ContentType = contentType
            }
        };
    }

    public Task<ParsedWebContent> ParseAsync(RawWebContent rawContent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawContent);

        var parsedContent = new ParsedWebContent
        {
            Url = rawContent.Url,
            Metadata = rawContent.Metadata
        };

        if (rawContent.ContentType.Contains("html"))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawContent.Content);

            // Extract title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                parsedContent.Metadata.Title = titleNode.InnerText.Trim();
            }

            // Extract text content
            var textNodes = doc.DocumentNode.SelectNodes("//text()[normalize-space(.) != '']");
            if (textNodes != null)
            {
                var textContent = string.Join(" ", textNodes
                    .Where(n => !IsScriptOrStyle(n))
                    .Select(n => n.InnerText.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t)));

                parsedContent.Content = CleanText(textContent);
            }
        }
        else
        {
            parsedContent.Content = rawContent.Content;
        }

        return Task.FromResult(parsedContent);
    }

    public Task<IEnumerable<WebContentChunk>> ChunkAsync(
        ParsedWebContent parsedContent,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedContent);

        chunkingOptions ??= new ChunkingOptions();

        var chunks = new List<WebContentChunk>();
        var content = parsedContent.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult<IEnumerable<WebContentChunk>>(chunks);
        }

        // Simple fixed-size chunking
        var chunkSize = chunkingOptions.MaxChunkSize;
        var overlap = chunkingOptions.OverlapSize;

        for (int i = 0; i < content.Length; i += chunkSize - overlap)
        {
            var end = Math.Min(i + chunkSize, content.Length);
            var chunkContent = content.Substring(i, end - i);

            // Try to break at word boundaries
            if (end < content.Length && !char.IsWhiteSpace(content[end]))
            {
                var lastSpace = chunkContent.LastIndexOf(' ');
                if (lastSpace > chunkContent.Length * 0.8)
                {
                    chunkContent = chunkContent.Substring(0, lastSpace);
                }
            }

            var chunk = new WebContentChunk
            {
                Content = chunkContent.Trim(),
                SourceUrl = parsedContent.Url,
                ChunkIndex = chunks.Count,
                Quality = 0.8, // Simple quality score
                Strategy = chunkingOptions.Strategy,
                Metadata = parsedContent.Metadata
            };

            chunks.Add(chunk);

            if (end >= content.Length) break;
        }

        return Task.FromResult<IEnumerable<WebContentChunk>>(chunks);
    }

    private static bool IsScriptOrStyle(HtmlNode node)
    {
        var parent = node.ParentNode;
        while (parent != null)
        {
            if (parent.Name.ToLower() is "script" or "style" or "noscript")
                return true;
            parent = parent.ParentNode;
        }
        return false;
    }

    private static string CleanText(string text)
    {
        text = Regex.Replace(text, @"\s+", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }
}