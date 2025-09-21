using FluxIndex.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Interfaces;

/// <summary>
/// 희소(Sparse) 검색 인터페이스 - BM25 키워드 검색
/// </summary>
public interface ISparseRetriever
{
    /// <summary>
    /// BM25 키워드 검색 실행
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="options">검색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>키워드 기반 검색 결과</returns>
    Task<IReadOnlyList<SparseSearchResult>> SearchAsync(
        string query,
        SparseSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 청크 인덱싱 - BM25 키워드 인덱스 구축
    /// </summary>
    /// <param name="chunk">인덱싱할 문서 청크</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task IndexDocumentAsync(
        DocumentChunk chunk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 인덱스 통계 정보 조회
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>BM25 인덱스 통계</returns>
    Task<SparseIndexStatistics> GetIndexStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 인덱스 최적화 실행
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    Task OptimizeIndexAsync(CancellationToken cancellationToken = default);
}