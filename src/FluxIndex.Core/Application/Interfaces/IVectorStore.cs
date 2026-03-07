using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;

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
}