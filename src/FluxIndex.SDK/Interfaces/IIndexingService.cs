using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Domain.Entities;

namespace FluxIndex.SDK.Interfaces;

/// <summary>
/// 문서 인덱싱 서비스 인터페이스 - FileFlux 청크를 벡터 저장소에 인덱싱
/// </summary>
public interface IIndexingService
{
    /// <summary>
    /// 단일 문서 인덱싱
    /// </summary>
    Task<IndexingResult> IndexDocumentAsync(string filePath, IndexingOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// FileFlux 청크 인덱싱
    /// </summary>
    Task<IndexingResult> IndexChunksAsync(IEnumerable<DocumentChunk> chunks, DocumentMetadata metadata, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 스트리밍 방식 인덱싱 (메모리 효율적)
    /// </summary>
    IAsyncEnumerable<IndexingProgress> IndexStreamAsync(string filePath, IndexingOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 여러 문서 일괄 인덱싱
    /// </summary>
    Task<BatchIndexingResult> IndexBatchAsync(IEnumerable<string> filePaths, IndexingOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 문서 재인덱싱
    /// </summary>
    Task<IndexingResult> ReindexDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 인덱싱 상태 확인
    /// </summary>
    Task<IndexingStatus> GetIndexingStatusAsync(string jobId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 인덱싱 작업 취소
    /// </summary>
    Task<bool> CancelIndexingAsync(string jobId, CancellationToken cancellationToken = default);
}