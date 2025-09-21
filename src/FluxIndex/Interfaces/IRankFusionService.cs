using FluxIndex.Domain.Models;

namespace FluxIndex.Interfaces;

/// <summary>
/// 랭크 퓨전 서비스 인터페이스
/// </summary>
public interface IRankFusionService
{
    /// <summary>
    /// RRF(Reciprocal Rank Fusion)를 사용한 결과 융합
    /// </summary>
    IEnumerable<RankedResult> FuseWithRRF(
        Dictionary<string, IEnumerable<RankedResult>> resultSets,
        int k = 60,
        int topN = 10);

    /// <summary>
    /// 가중치를 사용한 결과 융합
    /// </summary>
    IEnumerable<RankedResult> FuseWithWeights(
        Dictionary<string, (IEnumerable<RankedResult> results, float weight)> resultSets,
        int topN = 10);

    /// <summary>
    /// CombSUM 알고리즘을 사용한 결과 융합
    /// </summary>
    IEnumerable<RankedResult> FuseWithCombSUM(
        Dictionary<string, IEnumerable<RankedResult>> resultSets,
        int topN = 10);

    /// <summary>
    /// 베이지안 융합을 사용한 결과 융합
    /// </summary>
    IEnumerable<RankedResult> FuseWithBayesian(
        Dictionary<string, IEnumerable<RankedResult>> resultSets,
        Dictionary<string, float> priors,
        int topN = 10);
}