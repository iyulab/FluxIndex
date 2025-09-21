using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Domain.Entities;

namespace FluxIndex.Core.Interfaces;

/// <summary>
/// 고급 재순위화 서비스 인터페이스
/// </summary>
public interface IAdvancedRerankingService
{
    /// <summary>
    /// 고급 재순위화 실행
    /// </summary>
    Task<List<EnhancedSearchResult>> RerankAsync(
        string query,
        IEnumerable<SearchResult> initialResults,
        RerankingStrategy strategy = RerankingStrategy.Adaptive,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 재순위화 전략
/// </summary>
public enum RerankingStrategy
{
    /// <summary>
    /// 의미적 유사도 중심
    /// </summary>
    Semantic,

    /// <summary>
    /// 품질 메트릭 중심
    /// </summary>
    Quality,

    /// <summary>
    /// 맥락 및 관계 중심
    /// </summary>
    Contextual,

    /// <summary>
    /// 여러 전략 조합
    /// </summary>
    Hybrid,

    /// <summary>
    /// LLM 기반 평가
    /// </summary>
    LLM,

    /// <summary>
    /// 자동 전략 선택
    /// </summary>
    Adaptive
}