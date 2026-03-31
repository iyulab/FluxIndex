using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 벡터 저장소 인터페이스
/// </summary>
public interface IVectorStore
{
    Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default);
    Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of distinct documents stored in this vector store.
    /// Used for statistics reporting without relying on the document repository.
    /// </summary>
    Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    /// <summary>
    /// Checks if any vectors exist for a given document.
    /// Used by integrity checks to detect missing embeddings.
    /// </summary>
    Task<bool> HasVectorsForDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        // Default implementation using GetByDocumentIdAsync
        return GetByDocumentIdAsync(documentId, cancellationToken)
            .ContinueWith(t => t.Result.Any(), cancellationToken);
    }

    /// <summary>
    /// The runtime-resolved store/collection name. Null if not yet initialized.
    /// </summary>
    string? ResolvedStoreName => null;

    /// <summary>
    /// The runtime-detected embedding dimension. Null if not yet detected.
    /// </summary>
    int? DetectedDimension => null;

    /// <summary>
    /// The embedding identity bound to this store. Null if not yet initialized.
    /// Once bound, any attempt to store vectors from a different model will throw
    /// <see cref="FluxIndex.Core.Domain.Exceptions.EmbeddingModelMismatchException"/>.
    /// </summary>
    EmbeddingIdentity? BoundIdentity => null;

    /// <summary>
    /// Binds an embedding identity to this store.
    /// Once bound, the store uses the identity's fingerprint for collection/table naming
    /// and validates that subsequent operations use the same model.
    /// </summary>
    /// <remarks>
    /// Default implementation is a no-op for stores that don't support identity binding.
    /// </remarks>
    void BindIdentity(EmbeddingIdentity identity) { }

    /// <summary>
    /// Verifies that the vector store is operational by testing a write+delete cycle.
    /// Returns false if the store is corrupted and attempts self-healing.
    /// </summary>
    Task<bool> VerifyHealthAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}