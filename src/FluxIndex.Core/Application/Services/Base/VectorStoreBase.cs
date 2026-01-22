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
///         float[] queryEmbedding, int topK, CancellationToken ct)
///     {
///         // In-memory cosine similarity search
///     }
/// }
/// </example>
public abstract class VectorStoreBase : IVectorStore
{
    protected readonly ILogger? Logger;

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
    /// Returns results with scores; filtering and sorting handled by base class.
    /// </summary>
    /// <param name="queryEmbedding">Query vector.</param>
    /// <param name="topK">Maximum results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with scores (no minScore filtering needed).</returns>
    protected abstract Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
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
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            return [];

        if (topK <= 0)
            return [];

        var results = await SearchCoreAsync(queryEmbedding, topK, cancellationToken);
        return SearchResultProcessor.FilterAndSort(results, minScore, topK);
    }

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
            Logger?.LogWarning("Storing chunk with empty content (DocumentId: {DocumentId})", chunk.DocumentId);
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
}
