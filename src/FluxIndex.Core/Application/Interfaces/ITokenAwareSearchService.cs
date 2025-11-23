using FluxIndex.Core.Application.Models;

// Use types from Models namespace for token-aware search
using QueryAnalysis = FluxIndex.Core.Application.Models.QueryAnalysis;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 토큰 예산 기반 검색 서비스 인터페이스
/// </summary>
public interface ITokenAwareSearchService
{
    /// <summary>
    /// 토큰 예산 내에서 검색 수행
    /// </summary>
    Task<TokenAwareSearchResult> SearchAsync(
        TokenAwareSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 간단한 검색 (기본 옵션)
    /// </summary>
    Task<TokenAwareSearchResult> SearchAsync(
        string query,
        int maxTokens,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 질의 분석 서비스 인터페이스
/// </summary>
public interface IQueryAnalysisService
{
    /// <summary>
    /// 질의 분석
    /// </summary>
    Task<Models.QueryAnalysis> AnalyzeAsync(
        string query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 토큰 카운터 인터페이스
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// 텍스트의 토큰 수 계산
    /// </summary>
    int CountTokens(string text);

    /// <summary>
    /// 여러 텍스트의 총 토큰 수 계산
    /// </summary>
    int CountTokens(IEnumerable<string> texts);
}
