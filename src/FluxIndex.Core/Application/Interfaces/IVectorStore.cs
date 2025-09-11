using System;
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
    Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchAsync(
        EmbeddingVector queryVector, 
        int topK = 10, 
        float minScore = 0.0f,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<int> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
}