using FluxIndex.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// The semantic cache: search results stored under the query that produced them and served to a later query whose
/// embedding is similar enough. This is the cache <c>FluxIndexContext.SearchAsync</c> and <c>AdaptiveSearchService</c>
/// consult; the Redis, SQLite and PostgreSQL packages implement it.
/// </summary>
/// <remarks>
/// A lookup or write on a keyword-only context (<see cref="FluxIndex.Core.Application.Services.NoEmbeddingService"/>)
/// has no vector to compare, so implementations answer every lookup with a miss and store nothing.
/// </remarks>
public interface ISemanticCacheService
{
    /// <summary>
    /// The cached results of the stored query most similar to <paramref name="query"/>, or null when none is similar
    /// enough. A failure to read the cache is a miss, not an exception.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="similarityThreshold">
    /// Minimum cosine similarity (0.0 to 1.0) for a hit. Null uses the cache's configured threshold (its options'
    /// <c>SimilarityThreshold</c>).
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<CachedSearchResult?> GetCachedResultAsync(
        string query,
        float? similarityThreshold = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 검색 결과를 캐시에 저장
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="results">검색 결과</param>
    /// <param name="metadata">추가 메타데이터</param>
    /// <param name="ttl">캐시 생존 시간 (기본값: 1시간)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task SetCachedResultAsync(
        string query,
        IReadOnlyList<CacheDocumentChunk> results,
        SearchMetadata? metadata = null,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 특정 쿼리 패턴의 캐시 무효화
    /// </summary>
    /// <param name="pattern">무효화할 쿼리 패턴</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task InvalidateCacheAsync(
        string pattern,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every cached entry. The SDK indexer calls this after each write, so a search that starts afterwards is
    /// never answered from before the write (the same rule as the retriever's own result cache).
    /// </summary>
    /// <param name="cancellationToken">Cancels the clear.</param>
    Task ClearCacheAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 통계 조회
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시 통계 정보</returns>
    Task<SemanticCacheStatistics> GetCacheStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares the cache for <paramref name="popularQueries"/> without searching: there are no results to store, so
    /// no lookup hits until a search has stored its results. What is prepared depends on the implementation: Redis
    /// pre-computes and stores the query embeddings; the SQLite and PostgreSQL caches store nothing, since an entry
    /// without results could never be a hit.
    /// </summary>
    /// <param name="popularQueries">인기 쿼리 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task WarmupCacheAsync(
        IReadOnlyList<string> popularQueries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 압축 및 정리
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    Task CompactCacheAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 캐시된 검색 결과
/// </summary>
public class CachedSearchResult
{
    /// <summary>
    /// 원본 쿼리
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// 매칭된 캐시 쿼리
    /// </summary>
    public string CachedQuery { get; set; } = string.Empty;

    /// <summary>
    /// 유사도 점수
    /// </summary>
    public float SimilarityScore { get; set; }

    /// <summary>
    /// 검색 결과
    /// </summary>
    public IReadOnlyList<CacheDocumentChunk> Results { get; set; } = Array.Empty<CacheDocumentChunk>();

    /// <summary>
    /// 검색 메타데이터
    /// </summary>
    public SearchMetadata? Metadata { get; set; }

    /// <summary>
    /// 캐시 생성 시간
    /// </summary>
    public DateTime CachedAt { get; set; }

    /// <summary>
    /// 캐시 히트 횟수
    /// </summary>
    public int HitCount { get; set; }

    /// <summary>
    /// 마지막 액세스 시간
    /// </summary>
    public DateTime LastAccessedAt { get; set; }
}

/// <summary>
/// 검색 메타데이터
/// </summary>
public class SearchMetadata
{
    /// <summary>
    /// 검색 시간 (밀리초)
    /// </summary>
    public long SearchTimeMs { get; set; }

    /// <summary>
    /// 검색된 총 문서 수
    /// </summary>
    public int TotalDocuments { get; set; }

    /// <summary>
    /// 사용된 검색 알고리즘
    /// </summary>
    public string SearchAlgorithm { get; set; } = string.Empty;

    /// <summary>
    /// 검색 품질 점수
    /// </summary>
    public float QualityScore { get; set; }

    /// <summary>
    /// 추가 속성
    /// </summary>
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();
}

/// <summary>
/// 시맨틱 캐시 통계
/// </summary>
public class SemanticCacheStatistics
{
    /// <summary>
    /// 총 캐시 엔트리 수
    /// </summary>
    public long TotalEntries { get; set; }

    /// <summary>
    /// 캐시 히트 수
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// 캐시 미스 수
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// 캐시 히트율
    /// </summary>
    public float HitRate => (CacheHits + CacheMisses) > 0
        ? (float)CacheHits / (CacheHits + CacheMisses)
        : 0f;

    /// <summary>
    /// 평균 응답 시간 (밀리초)
    /// </summary>
    public float AverageResponseTimeMs { get; set; }

    /// <summary>
    /// 캐시 크기 (바이트)
    /// </summary>
    public long CacheSizeBytes { get; set; }

    /// <summary>
    /// 만료된 엔트리 수
    /// </summary>
    public long ExpiredEntries { get; set; }

    /// <summary>
    /// 평균 유사도 점수
    /// </summary>
    public float AverageSimilarityScore { get; set; }

    /// <summary>
    /// 최고 성능 쿼리들
    /// </summary>
    public IReadOnlyList<QueryPerformance> TopPerformingQueries { get; set; } = Array.Empty<QueryPerformance>();

    /// <summary>
    /// 통계 수집 시간
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 쿼리 성능 정보
/// </summary>
public class QueryPerformance
{
    /// <summary>
    /// 쿼리
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 히트 횟수
    /// </summary>
    public int HitCount { get; set; }

    /// <summary>
    /// 평균 유사도
    /// </summary>
    public float AverageSimilarity { get; set; }

    /// <summary>
    /// 평균 응답 시간 (밀리초)
    /// </summary>
    public float AverageResponseTimeMs { get; set; }

    /// <summary>
    /// 마지막 사용 시간
    /// </summary>
    public DateTime LastUsedAt { get; set; }
}