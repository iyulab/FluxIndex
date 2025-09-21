using FluxIndex.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Interfaces;

/// <summary>
/// 시맨틱 캐싱 서비스 인터페이스
/// 쿼리 유사도 기반으로 검색 결과를 캐싱하여 성능 향상
/// </summary>
public interface ISemanticCacheService
{
    /// <summary>
    /// 캐시에서 유사한 쿼리의 결과 검색
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="similarityThreshold">유사도 임계값 (기본값: 0.95)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시된 검색 결과 또는 null</returns>
    Task<CachedSearchResult?> GetCachedResultAsync(
        string query,
        float similarityThreshold = 0.95f,
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
        IReadOnlyList<DocumentChunk> results,
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
    /// 캐시 통계 조회
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시 통계 정보</returns>
    Task<SemanticCacheStatistics> GetCacheStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 워밍업 (사전에 인기 있는 쿼리들을 캐시에 로드)
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
    public IReadOnlyList<DocumentChunk> Results { get; set; } = Array.Empty<DocumentChunk>();

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