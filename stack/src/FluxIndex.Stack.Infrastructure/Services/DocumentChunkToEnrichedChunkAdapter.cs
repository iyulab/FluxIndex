using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Adapter that wraps Stack's DocumentChunk to implement IEnrichedChunk.
/// This enables FluxImprover pipeline integration with Stack's domain entities.
/// </summary>
public sealed class DocumentChunkToEnrichedChunkAdapter : IEnrichedChunk
{
    private readonly DocumentChunk _chunk;
    private readonly Document? _document;
    private readonly ISourceMetadata _source;

    /// <summary>
    /// Creates an adapter for the given DocumentChunk.
    /// </summary>
    /// <param name="chunk">The document chunk to adapt.</param>
    /// <param name="document">Optional document for additional metadata.</param>
    public DocumentChunkToEnrichedChunkAdapter(DocumentChunk chunk, Document? document = null)
    {
        _chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
        _document = document ?? chunk.Document;
        _source = new DocumentChunkSourceMetadata(_chunk, _document);
    }

    /// <inheritdoc />
    public string Content => _chunk.Content;

    /// <inheritdoc />
    public string ChunkId => _chunk.Id.ToString();

    /// <inheritdoc />
    public int ChunkIndex => _chunk.ChunkIndex;

    /// <inheritdoc />
    public IReadOnlyList<string> HeadingPath
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("heading_path", out var path))
            {
                if (path is string pathStr && !string.IsNullOrEmpty(pathStr))
                {
                    return pathStr.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
                }
                if (path is IEnumerable<string> pathList)
                {
                    return pathList.ToList();
                }
            }
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public string? SectionTitle
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("section", out var section))
            {
                return section?.ToString();
            }
            var path = HeadingPath;
            return path.Count > 0 ? path[^1] : null;
        }
    }

    /// <inheritdoc />
    public int? StartPage => GetMetadataInt("start_page");

    /// <inheritdoc />
    public int? EndPage => GetMetadataInt("end_page");

    /// <inheritdoc />
    public double Quality
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("ff_quality", out var quality))
            {
                return Convert.ToDouble(quality);
            }
            return 0.8; // Default quality
        }
    }

    /// <inheritdoc />
    public double ContextDependency
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("context_dependency", out var dep))
            {
                return Convert.ToDouble(dep);
            }
            return 0.5; // Default moderate dependency
        }
    }

    /// <inheritdoc />
    public int? TokenCount => _chunk.TokenCount > 0 ? _chunk.TokenCount : null;

    /// <inheritdoc />
    public ISourceMetadata Source => _source;

    private int? GetMetadataInt(string key)
    {
        if (_chunk.Metadata.TryGetValue(key, out var value))
        {
            return Convert.ToInt32(value);
        }
        return null;
    }
}

/// <summary>
/// Source metadata implementation for DocumentChunk.
/// </summary>
internal sealed class DocumentChunkSourceMetadata : ISourceMetadata
{
    private readonly DocumentChunk _chunk;
    private readonly Document? _document;

    public DocumentChunkSourceMetadata(DocumentChunk chunk, Document? document)
    {
        _chunk = chunk;
        _document = document;
    }

    public string SourceId => _chunk.DocumentId.ToString();

    public string SourceType
    {
        get
        {
            // Document uses SourceType instead of ContentType
            if (_document?.SourceType != null)
            {
                return _document.SourceType;
            }
            if (_chunk.Metadata.TryGetValue("content_type", out var ct))
            {
                return ct?.ToString() ?? "unknown";
            }
            return "text";
        }
    }

    public string Title => _document?.Title ?? "Unknown Document";

    // Document uses SourcePath instead of FilePath
    public string? FilePath => _document?.SourcePath;

    // Url is extracted from metadata or SourcePath if it looks like a URL
    public string? Url
    {
        get
        {
            // Check metadata for explicit URL
            if (_chunk.Metadata.TryGetValue("url", out var url))
            {
                return url?.ToString();
            }
            // Check if SourcePath is a URL
            var sourcePath = _document?.SourcePath;
            if (!string.IsNullOrEmpty(sourcePath) &&
                (sourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 sourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return sourcePath;
            }
            return null;
        }
    }

    public DateTime CreatedAt => _chunk.CreatedAt;

    public string Language
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("language", out var lang))
            {
                return lang?.ToString() ?? "en";
            }
            return "en";
        }
    }

    public double? LanguageConfidence => null;

    public int WordCount
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("word_count", out var wc))
            {
                return Convert.ToInt32(wc);
            }
            return _chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    public int ChunkCount
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("total_chunks", out var tc))
            {
                return Convert.ToInt32(tc);
            }
            return 1;
        }
    }

    public int? PageCount => null;

    public DateTime? PublishedAt => _document?.UpdatedAt;

    public string? Author => null;

    public IReadOnlyList<string>? Keywords
    {
        get
        {
            if (_chunk.Metadata.TryGetValue("keywords", out var kw))
            {
                if (kw is IEnumerable<string> keywords)
                {
                    return keywords.ToList();
                }
                if (kw is string kwStr && !string.IsNullOrEmpty(kwStr))
                {
                    return kwStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                }
            }
            return null;
        }
    }
}
