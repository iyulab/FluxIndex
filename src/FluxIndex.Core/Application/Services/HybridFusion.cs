using System.Collections.ObjectModel;
using FluxIndex.Core.Domain.Models;

namespace FluxIndex.Core.Services;

/// <summary>
/// Fuses a vector leg and a keyword leg into one ranked list. Shared by every <c>IHybridSearchService</c> so that a
/// <see cref="FusionMethod"/> means the same thing whichever service is registered.
/// </summary>
public static class HybridFusion
{
    /// <summary>
    /// Fuses the two legs with <paramref name="fusion"/> and stamps it on every row.
    /// </summary>
    /// <param name="vectorResults">Vector-leg hits, best first.</param>
    /// <param name="sparseResults">Keyword-leg hits, best first.</param>
    /// <param name="fusion">The method, weights and RRF constant to apply. <see cref="FusionMethod.Product"/> reads no
    /// weights (geometric mean of the raw scores).</param>
    /// <param name="maxResults">Rows to keep after ordering.</param>
    /// <param name="minFusedScore">Rows below this fused score are dropped.</param>
    /// <returns>Rows ordered by fused score, ranked from 1, each carrying <paramref name="fusion"/> and a confidence
    /// derived from how strongly both legs agree.</returns>
    public static IReadOnlyList<HybridSearchResult> Fuse(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        ArgumentNullException.ThrowIfNull(vectorResults);
        ArgumentNullException.ThrowIfNull(sparseResults);
        ArgumentNullException.ThrowIfNull(fusion);

        var fused = FuseWith(vectorResults, sparseResults, fusion, maxResults, minFusedScore);
        return WithConfidence(fused, vectorResults, sparseResults);
    }

    /// <summary>
    /// Confidence from min-max normalised leg scores — the same measure for every method.
    /// </summary>
    private static ReadOnlyCollection<HybridSearchResult> WithConfidence(
        IReadOnlyList<HybridSearchResult> rows,
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults)
    {
        var (vectorMin, vectorRange) = Bounds(vectorResults.Select(r => r.Score));
        var (sparseMin, sparseRange) = Bounds(sparseResults.Select(r => r.Score));

        return rows.Select(row =>
        {
            var vectorNormalized = row.Source is SearchSource.Vector or SearchSource.Both
                ? (row.VectorScore - vectorMin) / vectorRange
                : 0.0;
            var sparseNormalized = row.Source is SearchSource.Sparse or SearchSource.Both
                ? (row.SparseScore - sparseMin) / sparseRange
                : 0.0;
            return row with { Confidence = CalculateConfidence(vectorNormalized, sparseNormalized, row.Source) };
        }).ToList().AsReadOnly();
    }

    private static (double Min, double Range) Bounds(IEnumerable<double> scores)
    {
        var list = scores.ToList();
        if (list.Count == 0)
            return (0.0, 1.0);
        var min = list.Min();
        var range = list.Max() - min;
        return (min, range == 0 ? 1.0 : range);
    }

    private static ReadOnlyCollection<HybridSearchResult> FuseWith(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        return fusion.Method switch
        {
            FusionMethod.RRF => FuseWithRRF(vectorResults, sparseResults, fusion, maxResults, minFusedScore),
            FusionMethod.WeightedSum => FuseWithWeightedSum(vectorResults, sparseResults, fusion, maxResults, minFusedScore),
            FusionMethod.Product => FuseWithProduct(vectorResults, sparseResults, fusion, maxResults, minFusedScore),
            FusionMethod.Maximum => FuseWithMaximum(vectorResults, sparseResults, fusion, maxResults, minFusedScore),
            FusionMethod.HarmonicMean => FuseWithHarmonicMean(vectorResults, sparseResults, fusion, maxResults, minFusedScore),
            FusionMethod.RelativeScoreFusion => FuseWithRelativeScoreFusion(vectorResults, sparseResults, fusion, maxResults, minFusedScore),
            _ => FuseWithRelativeScoreFusion(vectorResults, sparseResults, fusion, maxResults, minFusedScore) // Default to RSF for better reranking
        };
    }

    private static ReadOnlyCollection<HybridSearchResult> FuseWithRRF(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        var fusedResults = new Dictionary<string, HybridSearchResult>();
        var k = fusion.RrfK;

        // 벡터 결과 처리
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (k + i + 1); // RRF 점수

            fusedResults[result.DocumentChunk.Id] = new HybridSearchResult
            {
                Chunk = result.DocumentChunk,
                FusedScore = rrfScore * fusion.VectorWeight,
                VectorScore = result.Score,
                SparseScore = 0.0,
                VectorRank = i + 1,
                SparseRank = int.MaxValue,
                Fusion = fusion,
                Source = SearchSource.Vector,
                MatchedTerms = Array.Empty<string>()
            };
        }

        // 키워드 결과 처리
        for (int i = 0; i < sparseResults.Count; i++)
        {
            var result = sparseResults[i];
            var rrfScore = 1.0 / (k + i + 1); // RRF 점수

            if (fusedResults.TryGetValue(result.Chunk.Id, out var existing))
            {
                // 기존 결과에 융합
                fusedResults[result.Chunk.Id] = existing with
                {
                    FusedScore = existing.FusedScore + (rrfScore * fusion.SparseWeight),
                    SparseScore = result.Score,
                    SparseRank = i + 1,
                    Source = SearchSource.Both,
                    MatchedTerms = result.MatchedTerms
                };
            }
            else
            {
                // 새 결과 생성
                fusedResults[result.Chunk.Id] = new HybridSearchResult
                {
                    Chunk = result.Chunk,
                    FusedScore = rrfScore * fusion.SparseWeight,
                    VectorScore = 0.0,
                    SparseScore = result.Score,
                    VectorRank = int.MaxValue,
                    SparseRank = i + 1,
                    Fusion = fusion,
                    Source = SearchSource.Sparse,
                    MatchedTerms = result.MatchedTerms
                };
            }
        }

        // 최종 점수로 정렬 및 상위 결과 반환
        var sortedResults = fusedResults.Values
            .Where(r => r.FusedScore >= minFusedScore)
            .OrderByDescending(r => r.FusedScore)
            .Take(maxResults)
            .Select((result, index) => result with { FusedRank = index + 1 })
            .ToList();

        return sortedResults.AsReadOnly();
    }

    private static ReadOnlyCollection<HybridSearchResult> FuseWithWeightedSum(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        var fusedResults = new Dictionary<string, HybridSearchResult>();

        // 점수 정규화를 위한 최대값 계산
        var maxVectorScore = vectorResults.Any() ? vectorResults.Max(r => r.Score) : 1.0;
        var maxSparseScore = sparseResults.Any() ? sparseResults.Max(r => r.Score) : 1.0;

        // 벡터 결과 처리
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var normalizedScore = result.Score / maxVectorScore;
            var weightedScore = normalizedScore * fusion.VectorWeight;

            fusedResults[result.DocumentChunk.Id] = new HybridSearchResult
            {
                Chunk = result.DocumentChunk,
                FusedScore = weightedScore,
                VectorScore = result.Score,
                SparseScore = 0.0,
                VectorRank = i + 1,
                SparseRank = int.MaxValue,
                Fusion = fusion,
                Source = SearchSource.Vector,
                MatchedTerms = Array.Empty<string>()
            };
        }

        // 키워드 결과 처리
        for (int i = 0; i < sparseResults.Count; i++)
        {
            var result = sparseResults[i];
            var normalizedScore = result.Score / maxSparseScore;
            var weightedScore = normalizedScore * fusion.SparseWeight;

            if (fusedResults.TryGetValue(result.Chunk.Id, out var existing))
            {
                fusedResults[result.Chunk.Id] = existing with
                {
                    FusedScore = existing.FusedScore + weightedScore,
                    SparseScore = result.Score,
                    SparseRank = i + 1,
                    Source = SearchSource.Both,
                    MatchedTerms = result.MatchedTerms
                };
            }
            else
            {
                fusedResults[result.Chunk.Id] = new HybridSearchResult
                {
                    Chunk = result.Chunk,
                    FusedScore = weightedScore,
                    VectorScore = 0.0,
                    SparseScore = result.Score,
                    VectorRank = int.MaxValue,
                    SparseRank = i + 1,
                    Fusion = fusion,
                    Source = SearchSource.Sparse,
                    MatchedTerms = result.MatchedTerms
                };
            }
        }

        return OrderAndLimitResults(fusedResults.Values, maxResults, minFusedScore);
    }

    private static ReadOnlyCollection<HybridSearchResult> FuseWithProduct(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        // 곱셈 융합은 양쪽 모두에서 매칭된 결과만 유지
        var fusedResults = new List<HybridSearchResult>();
        var vectorDict = vectorResults.ToDictionary(r => r.DocumentChunk.Id, r => r);
        var sparseDict = sparseResults.ToDictionary(r => r.Chunk.Id, r => r);

        foreach (var chunkId in vectorDict.Keys.Intersect(sparseDict.Keys))
        {
            var vectorResult = vectorDict[chunkId];
            var sparseResult = sparseDict[chunkId];

            var vectorNormalized = vectorResult.Score;
            var sparseNormalized = sparseResult.Score;
            var productScore = Math.Sqrt(vectorNormalized * sparseNormalized); // 기하평균

            fusedResults.Add(new HybridSearchResult
            {
                Chunk = vectorResult.DocumentChunk,
                FusedScore = productScore,
                VectorScore = vectorResult.Score,
                SparseScore = sparseResult.Score,
                VectorRank = vectorResults.ToList().IndexOf(vectorResult) + 1,
                SparseRank = sparseResults.ToList().IndexOf(sparseResult) + 1,
                Fusion = fusion,
                Source = SearchSource.Both,
                MatchedTerms = sparseResult.MatchedTerms
            });
        }

        return OrderAndLimitResults(fusedResults, maxResults, minFusedScore);
    }

    private static ReadOnlyCollection<HybridSearchResult> FuseWithMaximum(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        var fusedResults = new Dictionary<string, HybridSearchResult>();

        // 모든 결과를 통합하고 최대 점수 선택
        var allChunkIds = vectorResults.Select(r => r.DocumentChunk.Id)
            .Concat(sparseResults.Select(r => r.Chunk.Id))
            .Distinct();

        foreach (var chunkId in allChunkIds)
        {
            var vectorResult = vectorResults.FirstOrDefault(r => r.DocumentChunk.Id == chunkId);
            var sparseResult = sparseResults.FirstOrDefault(r => r.Chunk.Id == chunkId);

            var vectorScore = vectorResult?.Score ?? 0.0;
            var sparseScore = sparseResult?.Score ?? 0.0;
            var maxScore = Math.Max(vectorScore * fusion.VectorWeight, sparseScore * fusion.SparseWeight);

            var chunk = vectorResult?.DocumentChunk ?? sparseResult!.Chunk;
            var source = (vectorResult != null && sparseResult != null) ? SearchSource.Both :
                         (vectorResult != null) ? SearchSource.Vector : SearchSource.Sparse;

            fusedResults[chunkId] = new HybridSearchResult
            {
                Chunk = chunk,
                FusedScore = maxScore,
                VectorScore = vectorScore,
                SparseScore = sparseScore,
                VectorRank = vectorResult != null ? vectorResults.ToList().IndexOf(vectorResult) + 1 : int.MaxValue,
                SparseRank = sparseResult != null ? sparseResults.ToList().IndexOf(sparseResult) + 1 : int.MaxValue,
                Fusion = fusion,
                Source = source,
                MatchedTerms = sparseResult?.MatchedTerms ?? Array.Empty<string>()
            };
        }

        return OrderAndLimitResults(fusedResults.Values, maxResults, minFusedScore);
    }

    private static ReadOnlyCollection<HybridSearchResult> FuseWithHarmonicMean(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        // 조화평균은 양쪽 모두에서 매칭된 결과만 유지
        var fusedResults = new List<HybridSearchResult>();
        var vectorDict = vectorResults.ToDictionary(r => r.DocumentChunk.Id, r => r);
        var sparseDict = sparseResults.ToDictionary(r => r.Chunk.Id, r => r);

        foreach (var chunkId in vectorDict.Keys.Intersect(sparseDict.Keys))
        {
            var vectorResult = vectorDict[chunkId];
            var sparseResult = sparseDict[chunkId];

            var vectorWeighted = vectorResult.Score * fusion.VectorWeight;
            var sparseWeighted = sparseResult.Score * fusion.SparseWeight;

            // 조화평균 계산
            var harmonicMean = (vectorWeighted + sparseWeighted) > 0
                ? 2 * vectorWeighted * sparseWeighted / (vectorWeighted + sparseWeighted)
                : 0.0;

            fusedResults.Add(new HybridSearchResult
            {
                Chunk = vectorResult.DocumentChunk,
                FusedScore = harmonicMean,
                VectorScore = vectorResult.Score,
                SparseScore = sparseResult.Score,
                VectorRank = vectorResults.ToList().IndexOf(vectorResult) + 1,
                SparseRank = sparseResults.ToList().IndexOf(sparseResult) + 1,
                Fusion = fusion,
                Source = SearchSource.Both,
                MatchedTerms = sparseResult.MatchedTerms
            });
        }

        return OrderAndLimitResults(fusedResults, maxResults, minFusedScore);
    }

    private static ReadOnlyCollection<HybridSearchResult> FuseWithRelativeScoreFusion(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        // Relative Score Fusion (RSF) preserves score magnitude information
        // by normalizing scores within each retriever before fusion

        var fusedResults = new Dictionary<string, HybridSearchResult>();

        // Calculate min-max normalization bounds for each retriever
        var vectorScores = vectorResults.Select(r => r.Score).ToList();
        var sparseScores = sparseResults.Select(r => r.Score).ToList();

        var (vectorMin, vectorMax) = vectorScores.Count > 0
            ? (vectorScores.Min(), vectorScores.Max())
            : (0.0, 1.0);
        var (sparseMin, sparseMax) = sparseScores.Count > 0
            ? (sparseScores.Min(), sparseScores.Max())
            : (0.0, 1.0);

        // Avoid division by zero
        var vectorRange = vectorMax - vectorMin;
        var sparseRange = sparseMax - sparseMin;
        if (vectorRange == 0) vectorRange = 1.0;
        if (sparseRange == 0) sparseRange = 1.0;

        // Process vector results
        for (var i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var normalizedScore = (result.Score - vectorMin) / vectorRange;
            var weightedScore = normalizedScore * fusion.VectorWeight;

            fusedResults[result.DocumentChunk.Id] = new HybridSearchResult
            {
                Chunk = result.DocumentChunk,
                FusedScore = weightedScore,
                VectorScore = result.Score,
                SparseScore = 0.0,
                VectorRank = i + 1,
                SparseRank = int.MaxValue,
                Fusion = fusion,
                Source = SearchSource.Vector,
                MatchedTerms = Array.Empty<string>()
            };
        }

        // Process sparse results and merge with existing
        for (var i = 0; i < sparseResults.Count; i++)
        {
            var result = sparseResults[i];
            var normalizedScore = (result.Score - sparseMin) / sparseRange;
            var weightedScore = normalizedScore * fusion.SparseWeight;

            if (fusedResults.TryGetValue(result.Chunk.Id, out var existing))
            {
                // Combine scores for results found in both
                var vectorNormalized = (existing.VectorScore - vectorMin) / vectorRange;
                var combinedScore = vectorNormalized * fusion.VectorWeight + weightedScore;

                fusedResults[result.Chunk.Id] = existing with
                {
                    FusedScore = combinedScore,
                    SparseScore = result.Score,
                    SparseRank = i + 1,
                    Source = SearchSource.Both,
                    MatchedTerms = result.MatchedTerms
                };
            }
            else
            {
                fusedResults[result.Chunk.Id] = new HybridSearchResult
                {
                    Chunk = result.Chunk,
                    FusedScore = weightedScore,
                    VectorScore = 0.0,
                    SparseScore = result.Score,
                    VectorRank = int.MaxValue,
                    SparseRank = i + 1,
                    Fusion = fusion,
                    Source = SearchSource.Sparse,
                    MatchedTerms = result.MatchedTerms
                };
            }
        }

        return OrderAndLimitResults(fusedResults.Values, maxResults, minFusedScore);
    }

    /// <summary>
    /// Calculates confidence score based on normalized scores and source diversity
    /// </summary>
    private static double CalculateConfidence(double vectorNormalized, double sparseNormalized, SearchSource source)
    {
        // Base confidence from score agreement (both sources agreeing = higher confidence)
        var scoreAgreement = source == SearchSource.Both
            ? 1.0 - Math.Abs(vectorNormalized - sparseNormalized)
            : 0.5; // Single source gets moderate base confidence

        // Boost for high scores
        var avgScore = source switch
        {
            SearchSource.Both => (vectorNormalized + sparseNormalized) / 2.0,
            SearchSource.Vector => vectorNormalized,
            SearchSource.Sparse => sparseNormalized,
            _ => 0.0
        };

        // Source diversity bonus
        var diversityBonus = source == SearchSource.Both ? 0.2 : 0.0;

        // Final confidence: weighted combination
        var confidence = (scoreAgreement * 0.4) + (avgScore * 0.4) + diversityBonus;

        return Math.Clamp(confidence, 0.0, 1.0);
    }

    private static ReadOnlyCollection<HybridSearchResult> OrderAndLimitResults(
        IEnumerable<HybridSearchResult> results,
        int maxResults,
        double minFusedScore)
    {
        return results
            .Where(r => r.FusedScore >= minFusedScore)
            .OrderByDescending(r => r.FusedScore)
            .Take(maxResults)
            .Select((result, index) => result with { FusedRank = index + 1 })
            .ToList()
            .AsReadOnly();
    }
}
