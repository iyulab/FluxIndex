using FluxImprover.Models;
using FluxIndexChunk = Flux.Abstractions.IEnrichedChunk;

namespace FluxIndex.SDK.Extensions.FluxImprover.Adapters;

/// <summary>
/// Adapter that bridges FluxIndex.Core.IEnrichedChunk to FluxImprover.Models.ILlmEnrichedChunk.
/// This allows FluxImprover services to process chunks from FluxIndex.
/// </summary>
public sealed class EnrichedChunkAdapter : ILlmEnrichedChunk
{
    private readonly FluxIndexChunk _fluxIndexChunk;
    private IReadOnlyDictionary<string, object>? _cachedMetadata;

    /// <summary>
    /// Creates a new adapter wrapping the FluxIndex enriched chunk.
    /// </summary>
    /// <param name="fluxIndexChunk">The FluxIndex enriched chunk to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when fluxIndexChunk is null.</exception>
    public EnrichedChunkAdapter(FluxIndexChunk fluxIndexChunk)
    {
        _fluxIndexChunk = fluxIndexChunk ?? throw new ArgumentNullException(nameof(fluxIndexChunk));
    }

    // Flux.Abstractions.IEnrichedChunk implementation (delegated to underlying chunk)

    /// <inheritdoc />
    public string ChunkId => _fluxIndexChunk.ChunkId;

    /// <inheritdoc />
    public string Content => _fluxIndexChunk.Content;

    /// <inheritdoc />
    public int ChunkIndex => _fluxIndexChunk.ChunkIndex;

    /// <inheritdoc />
    public IReadOnlyList<string> HeadingPath => _fluxIndexChunk.HeadingPath;

    /// <inheritdoc />
    public string? SectionTitle => _fluxIndexChunk.SectionTitle;

    /// <inheritdoc />
    public int? StartPage => _fluxIndexChunk.StartPage;

    /// <inheritdoc />
    public int? EndPage => _fluxIndexChunk.EndPage;

    /// <inheritdoc />
    public double Quality => _fluxIndexChunk.Quality;

    /// <inheritdoc />
    public double ContextDependency => _fluxIndexChunk.ContextDependency;

    /// <inheritdoc />
    public int? TokenCount => _fluxIndexChunk.TokenCount;

    /// <inheritdoc />
    public Flux.Abstractions.ISourceMetadata Source => _fluxIndexChunk.Source;

    // ILlmEnrichedChunk additions — pre-enrichment adapter returns null

    /// <inheritdoc />
    public string? Summary => null;

    /// <inheritdoc />
    public IReadOnlyList<string>? Keywords => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata
    {
        get
        {
            if (_cachedMetadata != null)
            {
                return _cachedMetadata;
            }

            var metadata = new Dictionary<string, object>
            {
                ["ChunkIndex"] = _fluxIndexChunk.ChunkIndex,
                ["Quality"] = _fluxIndexChunk.Quality,
                ["ContextDependency"] = _fluxIndexChunk.ContextDependency
            };

            if (_fluxIndexChunk.TokenCount.HasValue)
            {
                metadata["TokenCount"] = _fluxIndexChunk.TokenCount.Value;
            }

            if (_fluxIndexChunk.StartPage.HasValue)
            {
                metadata["StartPage"] = _fluxIndexChunk.StartPage.Value;
            }

            if (_fluxIndexChunk.EndPage.HasValue)
            {
                metadata["EndPage"] = _fluxIndexChunk.EndPage.Value;
            }

            if (!string.IsNullOrEmpty(_fluxIndexChunk.SectionTitle))
            {
                metadata["SectionTitle"] = _fluxIndexChunk.SectionTitle;
            }

            var source = _fluxIndexChunk.Source;
            if (!string.IsNullOrEmpty(source.FilePath))
            {
                metadata["FilePath"] = source.FilePath;
            }

            if (!string.IsNullOrEmpty(source.SourceType))
            {
                metadata["SourceType"] = source.SourceType;
            }

            if (!string.IsNullOrEmpty(source.Title))
            {
                metadata["Title"] = source.Title;
            }

            if (!string.IsNullOrEmpty(source.Author))
            {
                metadata["Author"] = source.Author;
            }

            if (!string.IsNullOrEmpty(source.Language))
            {
                metadata["Language"] = source.Language;
            }

            if (source.WordCount > 0)
            {
                metadata["WordCount"] = source.WordCount;
            }

            _cachedMetadata = metadata;
            return _cachedMetadata;
        }
    }

    // Backward-compat helpers used by wrapper services

    /// <summary>Alias for <see cref="ChunkId"/> for FluxImprover Chunk.Id compatibility.</summary>
    public string Id => _fluxIndexChunk.ChunkId;

    /// <summary>Alias for <see cref="Content"/> for FluxImprover Chunk.Content compatibility.</summary>
    public string Text => _fluxIndexChunk.Content;

    /// <summary>Source document identifier shorthand.</summary>
    public string SourceId => _fluxIndexChunk.Source.SourceId;

    /// <summary>Heading path joined as a string for FluxImprover compatibility.</summary>
    public string? HeadingPathString
    {
        get
        {
            var path = _fluxIndexChunk.HeadingPath;
            if (path == null || path.Count == 0)
            {
                return null;
            }

            return string.Join(" > ", path);
        }
    }

    /// <summary>
    /// Gets the underlying FluxIndex chunk for advanced scenarios.
    /// </summary>
    public FluxIndexChunk UnderlyingChunk => _fluxIndexChunk;
}
