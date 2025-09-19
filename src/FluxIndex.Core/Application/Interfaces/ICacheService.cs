using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 캐시 서비스 인터페이스
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// 캐시에서 값 조회
    /// </summary>
    /// <typeparam name="T">값 타입</typeparam>
    /// <param name="key">캐시 키</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시된 값 (없으면 null)</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 캐시에 값 저장
    /// </summary>
    /// <typeparam name="T">값 타입</typeparam>
    /// <param name="key">캐시 키</param>
    /// <param name="value">저장할 값</param>
    /// <param name="expiry">만료 시간 (null이면 기본값 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 캐시에서 값 제거
    /// </summary>
    /// <param name="key">캐시 키</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>제거 성공 여부</returns>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 패턴에 매칭되는 캐시 키 제거
    /// </summary>
    /// <param name="pattern">패턴 (예: "user:*")</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>제거된 키 수</returns>
    Task<long> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// 검색 결과 캐싱
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="results">검색 결과</param>
    /// <param name="expiry">만료 시간</param>
    /// <param name="cancellationToken">취소 토큰</param>
    Task CacheSearchResultsAsync(string query, IEnumerable<SearchResult> results, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시된 검색 결과 조회
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>캐시된 검색 결과</returns>
    Task<IEnumerable<SearchResult>?> GetCachedSearchResultsAsync(string query, CancellationToken cancellationToken = default);
}