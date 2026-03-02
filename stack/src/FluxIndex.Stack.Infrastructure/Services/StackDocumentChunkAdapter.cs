using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Adapts Stack's DocumentChunk to FluxIndex Core's IEnrichedChunk interface.
/// This enables FluxImprover pipeline integration with Stack's document processing.
/// </summary>
public class StackDocumentChunkAdapter : IEnrichedChunk
{
    private readonly DocumentChunk _chunk;
    private readonly Document? _document;
    private readonly StackSourceMetadata _sourceMetadata;

    public StackDocumentChunkAdapter(DocumentChunk chunk, Document? document = null, int totalChunks = 0)
    {
        _chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
        _document = document ?? chunk.Document;

        // Build source metadata from document
        // Map Stack's Document properties to ISourceMetadata
        // - SourceType: corresponds to ContentType (e.g., "text/html", "application/pdf")
        // - SourcePath: may be a file path or URL depending on source
        var sourcePath = _document?.SourcePath;
        var isUrl = sourcePath?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true;

        _sourceMetadata = new StackSourceMetadata(
            _document?.Id.ToString() ?? chunk.DocumentId.ToString(),
            _document?.SourceType ?? "unknown",
            _document?.Title ?? "Unknown Document",
            isUrl ? null : sourcePath,  // FilePath only if not a URL
            isUrl ? sourcePath : null,  // Url only if it is a URL
            _document?.CreatedAt ?? chunk.CreatedAt,
            GetLanguageFromMetadata(chunk.Metadata),
            GetLanguageConfidenceFromMetadata(chunk.Metadata),
            chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            totalChunks > 0 ? totalChunks : GetTotalChunksFromMetadata(chunk.Metadata),
            null, // PageCount
            null, // PublishedAt
            null, // Author
            GetKeywordsFromMetadata(chunk.Metadata)
        );
    }

    /// <summary>
    /// The chunk's text content.
    /// </summary>
    public string Content => _chunk.Content;

    /// <summary>
    /// Unique identifier for the chunk.
    /// </summary>
    public string ChunkId => _chunk.Id.ToString();

    /// <summary>
    /// Zero-based index of the chunk within the document.
    /// </summary>
    public int ChunkIndex => _chunk.ChunkIndex;

    /// <summary>
    /// Heading hierarchy path extracted from metadata.
    /// </summary>
    public IReadOnlyList<string> HeadingPath => GetHeadingPathFromMetadata(_chunk.Metadata);

    /// <summary>
    /// Current section title (last element of HeadingPath).
    /// </summary>
    public string? SectionTitle => GetSectionTitleFromMetadata(_chunk.Metadata);

    /// <summary>
    /// Start page number (if available in metadata).
    /// </summary>
    public int? StartPage => GetPageFromMetadata(_chunk.Metadata, "start_page");

    /// <summary>
    /// End page number (if available in metadata).
    /// </summary>
    public int? EndPage => GetPageFromMetadata(_chunk.Metadata, "end_page");

    /// <summary>
    /// Chunk quality score from FileFlux metadata.
    /// </summary>
    public double Quality => GetDoubleFromMetadata(_chunk.Metadata, "ff_quality", 0.7);

    /// <summary>
    /// Context dependency score.
    /// </summary>
    public double ContextDependency => GetDoubleFromMetadata(_chunk.Metadata, "ff_density", 0.5);

    /// <summary>
    /// Token count for the chunk.
    /// </summary>
    public int? TokenCount => _chunk.TokenCount > 0 ? _chunk.TokenCount : null;

    /// <summary>
    /// Source document metadata.
    /// </summary>
    public ISourceMetadata Source => _sourceMetadata;

    #region Helper Methods

    private static string GetLanguageFromMetadata(Dictionary<string, object> metadata)
    {
        if (metadata.TryGetValue("language", out var lang) && lang is string langStr)
            return langStr;
        return "en";
    }

    private static double? GetLanguageConfidenceFromMetadata(Dictionary<string, object> metadata)
    {
        if (metadata.TryGetValue("language_confidence", out var conf))
        {
            if (conf is double d) return d;
            if (conf is float f) return f;
            if (double.TryParse(conf?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static int GetTotalChunksFromMetadata(Dictionary<string, object> metadata)
    {
        if (metadata.TryGetValue("total_chunks", out var total))
        {
            if (total is int i) return i;
            if (int.TryParse(total?.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }

    private static IReadOnlyList<string> GetHeadingPathFromMetadata(Dictionary<string, object> metadata)
    {
        if (metadata.TryGetValue("heading_path", out var path))
        {
            if (path is string pathStr)
                return pathStr.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
            if (path is IEnumerable<string> pathList)
                return pathList.ToList();
        }
        return Array.Empty<string>();
    }

    private static string? GetSectionTitleFromMetadata(Dictionary<string, object> metadata)
    {
        if (metadata.TryGetValue("section", out var section) && section is string sectionStr)
            return sectionStr;

        var headingPath = GetHeadingPathFromMetadata(metadata);
        return headingPath.Count > 0 ? headingPath[^1] : null;
    }

    private static int? GetPageFromMetadata(Dictionary<string, object> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var page))
        {
            if (page is int i) return i;
            if (int.TryParse(page?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static double GetDoubleFromMetadata(Dictionary<string, object> metadata, string key, double defaultValue)
    {
        if (metadata.TryGetValue(key, out var value))
        {
            if (value is double d) return d;
            if (value is float f) return f;
            if (double.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return defaultValue;
    }

    private static IReadOnlyList<string>? GetKeywordsFromMetadata(Dictionary<string, object> metadata)
    {
        if (metadata.TryGetValue("keywords", out var keywords))
        {
            if (keywords is IEnumerable<string> list)
                return list.ToList();
            if (keywords is string str)
                return str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return null;
    }

    #endregion
}

/// <summary>
/// Source metadata implementation for Stack documents.
/// </summary>
public class StackSourceMetadata : ISourceMetadata
{
    public StackSourceMetadata(
        string sourceId,
        string sourceType,
        string title,
        string? filePath,
        string? url,
        DateTime createdAt,
        string language,
        double? languageConfidence,
        int wordCount,
        int chunkCount,
        int? pageCount,
        DateTime? publishedAt,
        string? author,
        IReadOnlyList<string>? keywords)
    {
        SourceId = sourceId;
        SourceType = sourceType;
        Title = title;
        FilePath = filePath;
        Url = url;
        CreatedAt = createdAt;
        Language = language;
        LanguageConfidence = languageConfidence;
        WordCount = wordCount;
        ChunkCount = chunkCount;
        PageCount = pageCount;
        PublishedAt = publishedAt;
        Author = author;
        Keywords = keywords;
    }

    public string SourceId { get; }
    public string SourceType { get; }
    public string Title { get; }
    public string? FilePath { get; }
    public string? Url { get; }
    public DateTime CreatedAt { get; }
    public string Language { get; }
    public double? LanguageConfidence { get; }
    public int WordCount { get; }
    public int ChunkCount { get; }
    public int? PageCount { get; }
    public DateTime? PublishedAt { get; }
    public string? Author { get; }
    public IReadOnlyList<string>? Keywords { get; }
}
