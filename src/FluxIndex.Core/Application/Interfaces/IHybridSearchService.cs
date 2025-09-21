using FluxIndex.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 하이브리드 검색 서비스 인터페이스 - 벡터 + 키워드 융합 검색
/// </summary>
public interface IHybridSearchService
{
    /// <summary>
    /// 하이브리드 검색 실행 (벡터 + BM25 융합)
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="options">하이브리드 검색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>융합된 검색 결과</returns>
    Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        HybridSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 하이브리드 검색 실행
    /// </summary>
    /// <param name="queries">검색 쿼리 목록</param>
    /// <param name="options">하이브리드 검색 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>배치 검색 결과</returns>
    Task<IReadOnlyList<BatchHybridSearchResult>> SearchBatchAsync(
        IReadOnlyList<string> queries,
        HybridSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 검색 전략 자동 선택
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추천 검색 전략</returns>
    Task<SearchStrategy> RecommendSearchStrategyAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 융합 알고리즘 성능 평가
    /// </summary>
    /// <param name="testQueries">테스트 쿼리 목록</param>
    /// <param name="groundTruth">정답 데이터</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>융합 성능 메트릭</returns>
    Task<FusionPerformanceMetrics> EvaluateFusionPerformanceAsync(
        IReadOnlyList<string> testQueries,
        IReadOnlyList<IReadOnlyList<string>> groundTruth,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ID로 청크 조회 (Small-to-Big 컨텍스트 확장용)
    /// </summary>
    /// <param name="chunkId">청크 ID</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>청크 데이터</returns>
    Task<DocumentChunk?> GetChunkByIdAsync(string chunkId, CancellationToken cancellationToken = default);
}

