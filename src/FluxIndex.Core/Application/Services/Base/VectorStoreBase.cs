using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Base;

/// <summary>
/// Base class for vector store implementations.
/// Provides common functionality including validation, metadata handling, and search result processing.
/// Follows Template Method pattern - providers implement Core methods for storage-specific operations.
/// </summary>
/// <example>
/// // SQLite implementation:
/// public class SQLiteVectorStore : VectorStoreBase
/// {
///     protected override Task&lt;string&gt; StoreCoreAsync(DocumentChunk chunk, CancellationToken ct)
///     {
///         // SQLite-specific storage logic
///     }
///     protected override Task&lt;IEnumerable&lt;VectorSearchResult&gt;&gt; SearchCoreAsync(
///         float[] queryEmbedding, int topK, Dictionary&lt;string, object&gt;? filters, CancellationToken ct)
///     {
///         // In-memory cosine similarity search; apply filters before any candidate trimming
///     }
/// }
/// </example>
public abstract partial class VectorStoreBase : IVectorStore
{
    protected ILogger? Logger { get; }

    protected VectorStoreBase()
    {
    }

    protected VectorStoreBase(ILogger logger)
    {
        Logger = logger;
    }

    #region Abstract Methods - Provider Must Implement

    /// <summary>
    /// Core storage implementation. Called after validation and metadata preparation.
    /// </summary>
    /// <param name="chunk">Chunk with validated and enriched metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored chunk's ID.</returns>
    protected abstract Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken);

    /// <summary>
    /// Core retrieval by ID implementation.
    /// </summary>
    protected abstract Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Core vector search implementation.
    /// Returns results with scores; minScore filtering and sorting handled by base class.
    /// </summary>
    /// <param name="queryEmbedding">Query vector.</param>
    /// <param name="topK">Maximum results to return.</param>
    /// <param name="filters">Optional metadata equality filters. Implementations SHOULD apply these
    /// natively (or at least before any internal candidate trimming) so that matching chunks are not
    /// crowded out of the candidate window by higher-scoring non-matching chunks. The base class
    /// re-applies <see cref="MatchesMetadataFilter"/> as an idempotent correctness backstop, so an
    /// implementation that cannot push filters down may ignore this parameter at the cost of recall,
    /// never correctness.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with scores (no minScore filtering needed).</returns>
    protected abstract Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken);

    /// <summary>
    /// Core delete by ID implementation.
    /// </summary>
    protected abstract Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Core update implementation.
    /// </summary>
    protected abstract Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken);

    /// <summary>
    /// Core retrieval by document ID implementation.
    /// </summary>
    protected abstract Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Core delete by document ID implementation.
    /// </summary>
    protected abstract Task<bool> DeleteByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Core count implementation.
    /// </summary>
    protected abstract Task<int> CountCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Core clear implementation.
    /// </summary>
    protected abstract Task ClearCoreAsync(CancellationToken cancellationToken);

    #endregion

    #region IVectorStore Implementation

    /// <inheritdoc />
    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        ValidateChunk(chunk);
        PrepareMetadata(chunk);
        return await StoreCoreAsync(chunk, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation processes chunks sequentially.
    /// Override for batch optimization if provider supports it.
    /// </remarks>
    public virtual async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        foreach (var chunk in chunks)
        {
            var id = await StoreAsync(chunk, cancellationToken);
            ids.Add(id);
        }
        return ids;
    }

    /// <inheritdoc />
    public Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult<DocumentChunk?>(null);

        return GetCoreAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return GetAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return Task.FromResult<IEnumerable<DocumentChunk>>([]);

        return GetByDocumentIdCoreAsync(documentId, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation retrieves chunks individually.
    /// Override for batch optimization if provider supports it.
    /// </remarks>
    public virtual async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        foreach (var id in ids)
        {
            var chunk = await GetAsync(id, cancellationToken);
            if (chunk != null)
                chunks.Add(chunk);
        }
        return chunks;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            return [];

        if (topK <= 0)
            return [];

        // Fail-loud at call time: unsupported filter values must throw here, not on (possibly
        // deferred) result enumeration, and regardless of whether any row reaches the backstop.
        ValidateFilters(filters);

        var results = await SearchCoreAsync(queryEmbedding, topK, filters, cancellationToken);

        // Metadata backstop MUST run before the topK trim: trimming first lets higher-scoring
        // non-matching chunks crowd matching ones out of the window (multi-tenant recall loss).
        // Idempotent w.r.t. native filtering done inside SearchCoreAsync.
        if (filters != null && filters.Count > 0)
        {
            results = results.Where(r => MatchesMetadataFilter(r.Chunk.Metadata, filters));
        }

        return SearchResultProcessor.FilterAndSort(results, minScore, topK);
    }

    /// <summary>
    /// Returns true if <paramref name="metadata"/> contains every key in <paramref name="filters"/>
    /// with an equal (ordinal string, JSON-normalized) value. A collection-valued filter entry
    /// matches when the metadata value equals ANY of its elements (see
    /// <see cref="ExpandFilterValue"/>). Shared by search post-filtering and
    /// <see cref="DeleteByFilterAsync"/> so both agree on match semantics.
    /// </summary>
    public static bool MatchesMetadataFilter(
        IReadOnlyDictionary<string, object>? metadata,
        IReadOnlyDictionary<string, object> filters)
    {
        if (metadata is null)
            return false;

        foreach (var (key, value) in filters)
        {
            if (!metadata.TryGetValue(key, out var metaValue))
                return false;

            var metaNormalized = NormalizeFilterValue(metaValue);
            var matched = false;
            foreach (var alternative in ExpandFilterValue(key, value))
            {
                if (string.Equals(metaNormalized, alternative, StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Expands a filter value into its normalized match alternatives, enforcing the
    /// <see cref="IVectorStore.SearchAsync"/> filter contract:
    /// a scalar (string / number / bool / scalar <see cref="System.Text.Json.JsonElement"/>)
    /// yields one alternative; a collection of scalars (any non-string <see cref="System.Collections.IEnumerable"/>
    /// or a JsonElement array) yields one alternative per element (OR / MatchAny semantics).
    /// Anything else — arbitrary objects, nested collections, empty collections — throws
    /// <see cref="ArgumentException"/> so a filter is never silently un-matchable
    /// (previously e.g. a <c>List&lt;string&gt;</c> degraded to its <c>ToString()</c> type name
    /// and returned zero results without any signal).
    /// </summary>
    /// <param name="key">Filter key, used in exception messages only.</param>
    /// <param name="value">Filter value to expand.</param>
    public static IReadOnlyList<string?> ExpandFilterValue(string key, object? value)
    {
        switch (value)
        {
            case null:
            case string:
            case bool:
                return [NormalizeFilterValue(value)];

            case System.Text.Json.JsonElement je:
                switch (je.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Array:
                        var elements = new List<string?>();
                        foreach (var item in je.EnumerateArray())
                        {
                            if (item.ValueKind is System.Text.Json.JsonValueKind.Array
                                or System.Text.Json.JsonValueKind.Object)
                                throw UnsupportedFilterValue(key, $"a JSON array containing a nested {item.ValueKind}");
                            elements.Add(NormalizeFilterValue(item));
                        }
                        return elements.Count > 0
                            ? elements
                            : throw EmptyFilterCollection(key);

                    case System.Text.Json.JsonValueKind.Object:
                        throw UnsupportedFilterValue(key, "a JSON object");

                    default:
                        return [NormalizeFilterValue(je)];
                }

            case System.Collections.IEnumerable enumerable:
                var alternatives = new List<string?>();
                foreach (var item in enumerable)
                {
                    if (item is System.Collections.IEnumerable and not string
                        || item is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array or System.Text.Json.JsonValueKind.Object })
                        throw UnsupportedFilterValue(key, "a collection containing nested collections or objects");
                    if (item is not (null or string or bool or IFormattable or System.Text.Json.JsonElement))
                        throw UnsupportedFilterValue(key, $"a collection containing a {item.GetType().Name}");
                    alternatives.Add(NormalizeFilterValue(item));
                }
                return alternatives.Count > 0
                    ? alternatives
                    : throw EmptyFilterCollection(key);

            case IFormattable:
                return [NormalizeFilterValue(value)];

            default:
                throw UnsupportedFilterValue(key, $"a {value.GetType().Name}");
        }
    }

    /// <summary>
    /// Eagerly validates every filter value against the IVectorStore filter contract
    /// (via <see cref="ExpandFilterValue"/>), throwing <see cref="ArgumentException"/> for
    /// unsupported values. Call at API entry points so violations surface at call time.
    /// </summary>
    public static void ValidateFilters(IReadOnlyDictionary<string, object>? filters)
    {
        if (filters is not { Count: > 0 })
            return;
        foreach (var (key, value) in filters)
            ExpandFilterValue(key, value);
    }

    private static ArgumentException UnsupportedFilterValue(string key, string what) => new(
        $"Filter '{key}' has an unsupported value: {what}. Supported: a scalar " +
        "(string/number/bool/scalar JsonElement) for equality, or a collection of scalars " +
        "for multi-value OR (MatchAny) matching.");

    private static ArgumentException EmptyFilterCollection(string key) => new(
        $"Filter '{key}' is an empty collection — its match semantics are ambiguous. " +
        "Pass at least one value, or omit the key to not filter on it.");

    /// <summary>
    /// Normalizes a metadata/filter value to its JSON text representation so that values compare
    /// equal across a JSON round-trip (e.g. .NET <c>true</c> vs a deserialized
    /// <see cref="System.Text.Json.JsonElement"/> reading "true") and across native store
    /// pushdown (jsonb text extraction) vs in-memory comparison.
    /// </summary>
    public static string? NormalizeFilterValue(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        System.Text.Json.JsonElement je => je.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            _ => je.ToString(),
        },
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation throws; stores capable of metadata-scoped deletion override this.
    /// </remarks>
    public virtual Task<int> DeleteByFilterAsync(
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support DeleteByFilterAsync.");

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Task.FromResult(false);

        return DeleteCoreAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return Task.FromResult(false);

        return DeleteByDocumentIdCoreAsync(documentId, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var chunk = await GetCoreAsync(id, cancellationToken);
        return chunk != null;
    }

    /// <inheritdoc />
    public virtual async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null || string.IsNullOrWhiteSpace(chunk.Id))
            return false;

        // Update timestamp in metadata
        chunk.Metadata = MetadataHelper.EnsureInitialized(chunk.Metadata);
        MetadataHelper.SetUpdatedTimestamp(chunk.Metadata);

        return await UpdateCoreAsync(chunk, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return CountCoreAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return CountCoreAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return ClearCoreAsync(cancellationToken);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates chunk before storage.
    /// Override to add provider-specific validation.
    /// </summary>
    protected virtual void ValidateChunk(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (string.IsNullOrWhiteSpace(chunk.Content))
        {
            if (Logger is not null) LogStoringEmptyContent(Logger, chunk.DocumentId);
        }
    }

    /// <summary>
    /// Prepares metadata before storage.
    /// Ensures metadata is initialized and adds standard fields.
    /// </summary>
    protected virtual void PrepareMetadata(DocumentChunk chunk)
    {
        chunk.Metadata = MetadataHelper.EnsureInitialized(chunk.Metadata);
        MetadataHelper.AddStandardFields(chunk.Metadata, chunk);

        // Serialize rich metadata for storage
        MetadataHelper.SerializeChunkMetadata(chunk.Metadata, chunk.ChunkMetadata);
        MetadataHelper.SerializeChunkQuality(chunk.Metadata, chunk.Quality);
        MetadataHelper.SerializeRelationships(chunk.Metadata, chunk.Relationships);
    }

    /// <summary>
    /// Restores rich metadata (ChunkMetadata, ChunkQuality, ChunkRelationships) from stored metadata.
    /// Call this in GetCoreAsync and SearchCoreAsync implementations to restore full chunk state.
    /// </summary>
    protected virtual void RestoreRichMetadata(DocumentChunk chunk)
    {
        if (chunk.Metadata == null)
            return;

        // Restore ChunkMetadata
        var chunkMetadata = MetadataHelper.DeserializeChunkMetadata(chunk.Metadata);
        if (chunkMetadata != null)
            chunk.SetMetadata(chunkMetadata);

        // Restore ChunkQuality
        var quality = MetadataHelper.DeserializeChunkQuality(chunk.Metadata);
        if (quality != null)
            chunk.SetQuality(quality);

        // Restore ChunkRelationships
        var relationships = MetadataHelper.DeserializeRelationships(chunk.Metadata);
        if (relationships != null)
        {
            foreach (var rel in relationships)
                chunk.AddRelationship(rel);
        }
    }

    /// <summary>
    /// Helper for computing cosine similarity (delegates to VectorMathUtilities).
    /// </summary>
    protected static float ComputeCosineSimilarity(float[]? a, float[]? b)
    {
        return VectorMathUtilities.CosineSimilarity(a, b);
    }

    /// <summary>
    /// Helper for computing magnitude (delegates to VectorMathUtilities).
    /// </summary>
    protected static float ComputeMagnitude(float[]? vector)
    {
        return VectorMathUtilities.ComputeMagnitude(vector);
    }

    /// <summary>
    /// Helper for fast cosine similarity with pre-computed query magnitude.
    /// </summary>
    protected static float ComputeFastCosineSimilarity(float[] query, float[] candidate, float queryMagnitude)
    {
        return VectorMathUtilities.FastCosineSimilarity(query, candidate, queryMagnitude);
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Storing chunk with empty content (DocumentId: {DocumentId})")]
    private static partial void LogStoringEmptyContent(ILogger logger, string documentId);

    #endregion
}
