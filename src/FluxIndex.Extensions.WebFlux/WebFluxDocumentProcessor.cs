using FluxIndex.Domain.Entities;
using FluxIndex.Application.Interfaces;
using FluxIndex.Extensions.WebFlux.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Concurrent;

namespace FluxIndex.Extensions.WebFlux;

/// <summary>
/// WebFlux integration service for FluxIndex
/// Processes web content using WebFlux and converts to FluxIndex Document format
/// </summary>
public class WebFluxDocumentProcessor : IDocumentProcessor
{
    private readonly IWebContentProcessor _webContentProcessor;
    private readonly ILogger<WebFluxDocumentProcessor> _logger;

    public WebFluxDocumentProcessor(
        IWebContentProcessor webContentProcessor,
        ILogger<WebFluxDocumentProcessor> logger)
    {
        _webContentProcessor = webContentProcessor ?? throw new ArgumentNullException(nameof(webContentProcessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Process web URL into FluxIndex Document
    /// </summary>
    public async Task<Document> ProcessUrlAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        _logger.LogInformation("Processing URL: {Url}", url);

        try
        {
            // Create document from URL
            var document = Document.Create();
            document.SetFilePath(url);
            document.SetFileName(ExtractFileNameFromUrl(url));

            // Process URL with WebFlux
            var chunks = await _webContentProcessor.ProcessAsync(url, crawlOptions, chunkingOptions, cancellationToken);

            // Convert WebFlux chunks to FluxIndex chunks
            var chunkList = chunks.ToList();
            var totalChunks = chunkList.Count;

            foreach (var webChunk in chunkList)
            {
                var fluxChunk = ConvertToDocumentChunk(webChunk, document.Id, totalChunks);
                document.AddChunk(fluxChunk);
            }

            // Set document metadata from first chunk
            if (chunks.Any())
            {
                var firstChunk = chunks.First();
                var metadata = ConvertWebMetadataToDocumentMetadata(firstChunk.Metadata);
                document.UpdateMetadata(metadata);

                // Set full content as concatenated chunks
                var fullContent = string.Join("\n\n", chunks.Select(c => c.Content));
                document.SetContent(fullContent);
            }

            document.MarkAsIndexed();
            _logger.LogInformation("Successfully processed URL {Url} with {ChunkCount} chunks", url, chunks.Count());

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process URL: {Url}", url);

            var document = Document.Create();
            document.SetFilePath(url);
            document.MarkAsFailed(ex.Message);

            return document;
        }
    }

    /// <summary>
    /// Process multiple URLs concurrently
    /// </summary>
    public async Task<IEnumerable<Document>> ProcessUrlsAsync(
        IEnumerable<string> urls,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);

        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = urls.Select(async url =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessUrlAsync(url, crawlOptions, chunkingOptions, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private static DocumentChunk ConvertToDocumentChunk(WebContentChunk webChunk, string documentId, int totalChunks)
    {
        var metadata = new Dictionary<string, object>()
        {
            ["SourceUrl"] = webChunk.SourceUrl,
            ["ChunkIndex"] = webChunk.ChunkIndex,
            ["Quality"] = webChunk.Quality,
            ["Strategy"] = webChunk.Strategy ?? "Unknown",
            ["ProcessedAt"] = DateTime.UtcNow
        };

        // Add WebFlux metadata if available
        if (webChunk.Metadata != null)
        {
            foreach (var prop in webChunk.Metadata.Properties)
            {
                metadata[$"WebFlux_{prop.Key}"] = prop.Value;
            }
        }

        var chunk = DocumentChunk.Create(
            documentId: documentId,
            content: webChunk.Content,
            chunkIndex: webChunk.ChunkIndex,
            totalChunks: totalChunks
        );

        // Add metadata as properties
        foreach (var prop in metadata)
        {
            chunk.AddProperty(prop.Key, prop.Value);
        }

        return chunk;
    }

    private static DocumentMetadata ConvertWebMetadataToDocumentMetadata(WebContentMetadata? webMetadata)
    {
        if (webMetadata == null)
            return new DocumentMetadata();

        var documentMetadata = new DocumentMetadata(
            brand: "Web",
            model: "WebContent",
            category: "WebDocument",
            language: webMetadata.Language ?? "en"
        );

        // Add web-specific metadata as custom fields
        if (!string.IsNullOrEmpty(webMetadata.Title))
            documentMetadata.AddCustomField("Title", webMetadata.Title);

        if (!string.IsNullOrEmpty(webMetadata.Author))
            documentMetadata.AddCustomField("Author", webMetadata.Author);

        if (!string.IsNullOrEmpty(webMetadata.Description))
            documentMetadata.AddCustomField("Description", webMetadata.Description);

        if (!string.IsNullOrEmpty(webMetadata.ContentType))
            documentMetadata.AddCustomField("ContentType", webMetadata.ContentType);

        if (webMetadata.Keywords?.Any() == true)
            documentMetadata.AddCustomField("Keywords", string.Join(", ", webMetadata.Keywords));

        // Add other properties
        foreach (var prop in webMetadata.Properties)
        {
            if (prop.Value != null)
            {
                documentMetadata.AddCustomField($"Web_{prop.Key}", prop.Value.ToString() ?? string.Empty);
            }
        }

        return documentMetadata;
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrEmpty(fileName) ? uri.Host : fileName;
        }
        catch
        {
            return url;
        }
    }
}

/// <summary>
/// Document processor interface for web content
/// </summary>
public interface IDocumentProcessor
{
    Task<Document> ProcessUrlAsync(
        string url,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Document>> ProcessUrlsAsync(
        IEnumerable<string> urls,
        CrawlOptions? crawlOptions = null,
        ChunkingOptions? chunkingOptions = null,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default);
}