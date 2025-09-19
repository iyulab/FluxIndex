using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 시맨틱 유사도 기반 쿼리 캐싱 서비스
/// 쿼리 임베딩을 활용하여 의미적으로 유사한 이전 검색 결과를 빠르게 반환
/// </summary>
public interface ISemanticCacheService
{
    /// <summary>
    /// 쿼리와 유사한 캐시된 응답을 검색
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="similarityThreshold">유사도 임계값 (0.0-1.0)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시 결과 또는 null (캐시 미스)</returns>
    Task<CacheResult?> GetCachedResponseAsync(
        string query,
        float similarityThreshold = 0.95f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리와 응답을 캐시에 저장
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="response">생성된 응답</param>
    /// <param name="searchResults">검색 결과 목록</param>
    /// <param name="expiry">캐시 만료 시간 (null이면 기본값 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task CacheResponseAsync(
        string query,
        string response,
        List<SearchResult>? searchResults = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치로 여러 쿼리-응답 쌍을 캐시에 저장
    /// </summary>
    /// <param name="cacheEntries">캐시할 항목들</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task CacheBatchAsync(
        IEnumerable<CacheEntry> cacheEntries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 통계 정보 조회
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시 통계</returns>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴에 매칭되는 캐시 항목들을 무효화
    /// </summary>
    /// <param name="pattern">무효화할 패턴 (예: "product:*")</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task InvalidateCacheAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 자주 사용되는 쿼리들로 캐시를 미리 워밍업
    /// </summary>
    /// <param name="commonQueries">인기 쿼리 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>워밍업 성공 여부</returns>
    Task<bool> WarmupCacheAsync(
        IEnumerable<string> commonQueries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시 크기 및 메모리 사용량 최적화
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    Task OptimizeCacheAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 캐시할 항목 정보
/// </summary>
public class CacheEntry
{
    public string Query { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public List<SearchResult> SearchResults { get; init; } = new();
    public TimeSpan? Expiry { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}