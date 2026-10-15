using System.Collections.ObjectModel;
using FluxIndex.Core.Domain.Models;

namespace FluxIndex.Core.Application.Services;

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
        => Fuse(vectorResults, sparseResults, [], fusion, maxResults, minFusedScore);

    /// <summary>
    /// Fuses the two text legs and the graph leg with <paramref name="fusion"/> and stamps it on every row. The graph leg
    /// adds <see cref="AppliedFusion.GraphWeight"/> times its own contribution on top of the text legs' fused score: RRF
    /// <c>1 / (k + rank)</c>, WeightedSum and Maximum its score over the leg's best, RelativeScoreFusion its min-max
    /// normalised score (Maximum keeps the larger of the two instead of adding). A row only the graph found is kept.
    /// </summary>
    /// <param name="vectorResults">Vector-leg hits, best first.</param>
    /// <param name="sparseResults">Keyword-leg hits, best first.</param>
    /// <param name="graphResults">Graph-leg hits, best first; empty when the graph leg is off.</param>
    /// <param name="fusion">The method, weights and RRF constant to apply.</param>
    /// <param name="maxResults">Rows to keep after ordering.</param>
    /// <param name="minFusedScore">Rows below this fused score are dropped.</param>
    /// <returns>Rows ordered by fused score, ranked from 1, each carrying <paramref name="fusion"/> and a confidence
    /// derived from how strongly the two text legs agree.</returns>
    /// <exception cref="ArgumentException">Graph hits with <see cref="FusionMethod.Product"/> or
    /// <see cref="FusionMethod.HarmonicMean"/>, which keep only rows both text legs found.</exception>
    public static IReadOnlyList<HybridSearchResult> Fuse(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        IReadOnlyList<GraphChunkHit> graphResults,
        AppliedFusion fusion,
        int maxResults,
        double minFusedScore)
    {
        ArgumentNullException.ThrowIfNull(vectorResults);
        ArgumentNullException.ThrowIfNull(sparseResults);
        ArgumentNullException.ThrowIfNull(graphResults);
        ArgumentNullException.ThrowIfNull(fusion);

        if (graphResults.Count > 0 && !AcceptsGraphLeg(fusion.Method))
            throw new ArgumentException(
                $"FusionMethod.{fusion.Method} keeps only rows both text legs found and cannot fuse a graph leg; use RRF, WeightedSum, RelativeScoreFusion or Maximum.",
                nameof(fusion));

        var rows = FuseWith(vectorResults, sparseResults, fusion);
        if (graphResults.Count > 0)
            rows = WithGraphLeg(rows, graphResults, fusion);
        var fused = OrderAndLimitResults(rows, maxResults, minFusedScore);
        return WithConfidence(fused, vectorResults, sparseResults);
    }

    /// <summary>
    /// Whether <paramref name="method"/> can fuse a graph leg: every method that keeps a row any leg found. Product and
    /// HarmonicMean keep only rows both text legs found, which defeats a leg added for recall.
    /// </summary>
    public static bool AcceptsGraphLeg(FusionMethod method) =>
        method is not (FusionMethod.Product or FusionMethod.HarmonicMean);

    private static List<HybridSearchResult> WithGraphLeg(
        IEnumerable<HybridSearchResult> rows,
        IReadOnlyList<GraphChunkHit> graphResults,
        AppliedFusion fusion)
    {
        var byId = rows.ToDictionary(r => r.Chunk.Id, StringComparer.Ordinal);
        var best = graphResults.Max(h => h.Score);
        var (min, range) = Bounds(graphResults.Select(h => h.Score));

        for (var i = 0; i < graphResults.Count; i++)
        {
            var hit = graphResults[i];
            var contribution = fusion.GraphWeight * fusion.Method switch
            {
                FusionMethod.RRF => 1.0 / (fusion.RrfK + i + 1),
                FusionMethod.WeightedSum or FusionMethod.Maximum => best > 0 ? hit.Score / best : 0.0,
                _ => (hit.Score - min) / range,
            };

            byId[hit.Chunk.Id] = byId.TryGetValue(hit.Chunk.Id, out var existing)
                ? existing with
                {
                    FusedScore = fusion.Method == FusionMethod.Maximum
                        ? Math.Max(existing.FusedScore, contribution)
                        : existing.FusedScore + contribution,
                    GraphScore = hit.Score,
                    GraphRank = i + 1,
                    Source = existing.Source | SearchSource.Graph,
                }
                : new HybridSearchResult
                {
                    Chunk = hit.Chunk,
                    FusedScore = contribution,
                    VectorRank = int.MaxValue,
                    SparseRank = int.MaxValue,
                    GraphScore = hit.Score,
                    GraphRank = i + 1,
                    Fusion = fusion,
                    Source = SearchSource.Graph,
                    MatchedTerms = Array.Empty<string>(),
                };
        }

        return byId.Values.ToList();
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
            // Confidence measures how far the two text legs agree; the graph leg does not enter it.
            var vectorNormalized = row.Source.HasFlag(SearchSource.Vector)
                ? (row.VectorScore - vectorMin) / vectorRange
                : 0.0;
            var sparseNormalized = row.Source.HasFlag(SearchSource.Sparse)
                ? (row.SparseScore - sparseMin) / sparseRange
                : 0.0;
            return row with { Confidence = CalculateConfidence(vectorNormalized, sparseNormalized, row.Source & SearchSource.Both) };
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

    private static List<HybridSearchResult> FuseWith(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion)
    {
        return fusion.Method switch
        {
            FusionMethod.RRF => FuseWithRRF(vectorResults, sparseResults, fusion),
            FusionMethod.WeightedSum => FuseWithWeightedSum(vectorResults, sparseResults, fusion),
            FusionMethod.Product => FuseWithProduct(vectorResults, sparseResults, fusion),
            FusionMethod.Maximum => FuseWithMaximum(vectorResults, sparseResults, fusion),
            FusionMethod.HarmonicMean => FuseWithHarmonicMean(vectorResults, sparseResults, fusion),
            FusionMethod.RelativeScoreFusion => FuseWithRelativeScoreFusion(vectorResults, sparseResults, fusion),
            _ => FuseWithRelativeScoreFusion(vectorResults, sparseResults, fusion) // Default to RSF for better reranking
        };
    }

    private static List<HybridSearchResult> FuseWithRRF(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion)
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

        return fusedResults.Values.ToList();
    }

    private static List<HybridSearchResult> FuseWithWeightedSum(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion)
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

        return fusedResults.Values.ToList();
    }

    private static List<HybridSearchResult> FuseWithProduct(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion)
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

        return fusedResults;
    }

    private static List<HybridSearchResult> FuseWithMaximum(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion)
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

        return fusedResults.Values.ToList();
    }

    private static List<HybridSearchResult> FuseWithHarmonicMean(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion)
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

        return fusedResults;
    }

    private static List<HybridSearchResult> FuseWithRelativeScoreFusion(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        AppliedFusion fusion)
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

        return fusedResults.Values.ToList();
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
