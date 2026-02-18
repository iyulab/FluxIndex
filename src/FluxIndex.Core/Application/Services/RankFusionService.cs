using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxIndex.Core.Services;

/// <summary>
/// Implementation of rank fusion algorithms for combining multiple search result sets.
/// </summary>
public partial class RankFusionService : IRankFusionService
{
    private readonly ILogger<RankFusionService> _logger;

    public RankFusionService(ILogger<RankFusionService>? logger = null)
    {
        _logger = logger ?? new NullLogger<RankFusionService>();
    }

    /// <summary>
    /// Implements Reciprocal Rank Fusion (RRF) algorithm.
    /// RRF Score = Σ(1/(k + rank_i)) for each result set i
    /// </summary>
    public IEnumerable<RankedResult> FuseWithRRF(
        Dictionary<string, IEnumerable<RankedResult>> resultSets,
        int k = 60,
        int topN = 10)
    {
        if (resultSets == null || resultSets.Count == 0)
        {
            LogRankFusion8(_logger);
            return Enumerable.Empty<RankedResult>();
        }

        if (_logger.IsEnabled(LogLevel.Warning))
            LogRankFusion7(_logger, resultSets.Count, k);

        // Dictionary to accumulate RRF scores
        var rrfScores = new Dictionary<string, (RankedResult result, double score)>();

        foreach (var (sourceName, results) in resultSets)
        {
            // Ensure results are ranked (1-based ranking)
            var rankedResults = results.Select((r, index) =>
            {
                r.Rank = index + 1;
                r.Source = sourceName;
                return r;
            }).ToList();

            foreach (var result in rankedResults)
            {
                var key = result.GetUniqueKey();
                var rrfScore = 1.0 / (k + result.Rank);

                if (rrfScores.TryGetValue(key, out var existing))
                {
                    // Accumulate RRF score for items appearing in multiple result sets
                    rrfScores[key] = (
                        result: MergeResults(existing.result, result),
                        score: existing.score + rrfScore
                    );

                    if (_logger.IsEnabled(LogLevel.Warning))
                        LogRankFusion6(_logger, key, rrfScores[key].score);
                }
                else
                {
                    rrfScores[key] = (result, rrfScore);
                }
            }
        }

        // Sort by RRF score and assign final ranks
        var fusedResults = rrfScores
            .OrderByDescending(kvp => kvp.Value.score)
            .Select((kvp, index) =>
            {
                var result = kvp.Value.result;
                result.Score = kvp.Value.score;
                result.Rank = index + 1;
                return result;
            })
            .Take(topN)
            .ToList();

        LogRankFusion5(_logger, rrfScores.Count, fusedResults.Count);

        return fusedResults;
    }

    /// <summary>
    /// Implements weighted linear combination of scores.
    /// </summary>
    public IEnumerable<RankedResult> FuseWithWeights(
        Dictionary<string, (IEnumerable<RankedResult> results, double weight)> resultSets,
        int topN = 10)
    {
        if (resultSets == null || resultSets.Count == 0)
        {
            LogRankFusion4(_logger);
            return Enumerable.Empty<RankedResult>();
        }

        LogRankFusion3(_logger, resultSets.Count);

        // Normalize weights to sum to 1
        var totalWeight = resultSets.Sum(rs => rs.Value.weight);
        if (totalWeight <= 0)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
                LogRankFusion2(_logger, totalWeight);
            throw new ArgumentException("Total weight must be positive", nameof(resultSets));
        }

        // Dictionary to accumulate weighted scores
        var weightedScores = new Dictionary<string, (RankedResult result, double score)>();

        foreach (var (sourceName, (results, weight)) in resultSets)
        {
            var normalizedWeight = weight / totalWeight;

            // Normalize scores within this result set
            var normalizedResults = NormalizeScores(results).ToList();

            foreach (var result in normalizedResults)
            {
                var key = result.GetUniqueKey();
                result.Source = sourceName;
                var weightedScore = result.Score * normalizedWeight;

                if (weightedScores.TryGetValue(key, out var existing))
                {
                    weightedScores[key] = (
                        result: MergeResults(existing.result, result),
                        score: existing.score + weightedScore
                    );
                }
                else
                {
                    weightedScores[key] = (result, weightedScore);
                }
            }
        }

        // Sort by weighted score and assign final ranks
        var fusedResults = weightedScores
            .OrderByDescending(kvp => kvp.Value.score)
            .Select((kvp, index) =>
            {
                var result = kvp.Value.result;
                result.Score = kvp.Value.score;
                result.Rank = index + 1;
                return result;
            })
            .Take(topN)
            .ToList();

        LogRankFusion1(_logger, weightedScores.Count, fusedResults.Count);

        return fusedResults;
    }

    /// <summary>
    /// Normalizes scores to [0, 1] range using min-max normalization.
    /// </summary>
    public IEnumerable<RankedResult> NormalizeScores(IEnumerable<RankedResult> results)
    {
        var resultList = results.ToList();
        if (resultList.Count == 0)
        {
            return resultList;
        }

        var minScore = resultList.Min(r => r.Score);
        var maxScore = resultList.Max(r => r.Score);
        var range = maxScore - minScore;

        // If all scores are the same, return uniform scores
        if (range <= double.Epsilon)
        {
            foreach (var result in resultList)
            {
                result.Score = 1.0;
            }
            return resultList;
        }

        // Normalize to [0, 1] range
        foreach (var result in resultList)
        {
            result.Score = (result.Score - minScore) / range;
        }

        return resultList;
    }

    /// <summary>
    /// Merges two results representing the same document/chunk.
    /// Preserves the result with more complete information.
    /// </summary>
    private static RankedResult MergeResults(RankedResult existing, RankedResult incoming)
    {
        // Keep the existing result but update source information
        if (string.IsNullOrEmpty(existing.Source))
        {
            existing.Source = incoming.Source;
        }
        else if (!existing.Source.Contains(incoming.Source))
        {
            existing.Source = $"{existing.Source},{incoming.Source}";
        }

        // Merge metadata if both have it
        if (existing.Metadata != null && incoming.Metadata != null)
        {
            foreach (var kvp in incoming.Metadata)
            {
                existing.Metadata.TryAdd(kvp.Key, kvp.Value);
            }
        }
        else if (existing.Metadata == null && incoming.Metadata != null)
        {
            existing.Metadata = incoming.Metadata;
        }

        return existing;
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "No result sets provided for RRF fusion")]
    private static partial void LogRankFusion8(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Performing RRF fusion on {Count} result sets with k={K}")]
    private static partial void LogRankFusion7(ILogger logger, int count, int k);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Accumulated RRF score for {Key}: {Score}")]
    private static partial void LogRankFusion6(ILogger logger, string key, double score);
    [LoggerMessage(Level = LogLevel.Information, Message = "RRF fusion completed: {Count} unique results, returning top {TopN}")]
    private static partial void LogRankFusion5(ILogger logger, int count, int topN);
    [LoggerMessage(Level = LogLevel.Warning, Message = "No result sets provided for weighted fusion")]
    private static partial void LogRankFusion4(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Performing weighted fusion on {Count} result sets")]
    private static partial void LogRankFusion3(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Error, Message = "Invalid weights: total weight is {TotalWeight}")]
    private static partial void LogRankFusion2(ILogger logger, double totalWeight);
    [LoggerMessage(Level = LogLevel.Information, Message = "Weighted fusion completed: {Count} unique results, returning top {TopN}")]
    private static partial void LogRankFusion1(ILogger logger, int count, int topN);

    #endregion
}
