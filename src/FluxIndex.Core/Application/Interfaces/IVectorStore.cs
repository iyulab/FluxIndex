using FluxIndex.Core.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 벡터 저장소 인터페이스
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// 문서 청크를 벡터 저장소에 저장
    /// </summary>
    Task<string> StoreAsync(DocumentChunk chunk, float[] embedding, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 문서 청크를 배치로 저장
    /// </summary>
    Task<IReadOnlyList<string>> StoreBatchAsync(IReadOnlyList<(DocumentChunk chunk, float[] embedding)> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// 유사한 벡터 검색
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int maxResults = 10, double minScore = 0.0, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 청크 조회
    /// </summary>
    Task<DocumentChunk?> GetChunkAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 문서 청크를 ID로 조회
    /// </summary>
    Task<IReadOnlyList<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 문서의 모든 청크 조회
    /// </summary>
    Task<IReadOnlyList<DocumentChunk>> GetDocumentChunksAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 청크 삭제
    /// </summary>
    Task<bool> DeleteAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서의 모든 청크 삭제
    /// </summary>
    Task<int> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 저장소 통계 조회
    /// </summary>
    Task<VectorStoreStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 인덱스 최적화
    /// </summary>
    Task OptimizeIndexAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 벡터 저장소 통계
/// </summary>
public class VectorStoreStatistics
{
    /// <summary>
    /// 총 문서 수
    /// </summary>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// 총 청크 수
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// 벡터 차원
    /// </summary>
    public int VectorDimension { get; init; }

    /// <summary>
    /// 인덱스 크기 (MB)
    /// </summary>
    public double IndexSizeMB { get; init; }

    /// <summary>
    /// 마지막 업데이트 시간
    /// </summary>
    public System.DateTime LastUpdated { get; init; }
}