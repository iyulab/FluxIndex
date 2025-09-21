using FluxIndex.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxIndex.Core.Services;

/// <summary>
/// Implementation of rank fusion algorithms for combining multiple search result sets
/// </summary>
public class RankFusionService : IRankFusionService
{
    private readonly ILogger<RankFusionService> _logger;

    public RankFusionService(ILogger<RankFusionService>? logger = null)
    {
        _logger = logger ?? new NullLogger<RankFusionService>();
    }

    /// <summary>
    /// Implements Reciprocal Rank Fusion (RRF) algorithm
    /// RRF Score = Σ(1/(k + rank_i)) for each result set i
    /// </summary>
    public IEnumerable<RankedResult> FuseWithRRF(
        Dictionary<string, IEnumerable<RankedResult>> resultSets,
        int k = 60,
        int topN = 10)
    {
        if (resultSets == null || !resultSets.Any())
        {
            _logger.LogWarning("No result sets provided for RRF fusion");
            return Enumerable.Empty<RankedResult>();
        }

        _logger.LogInformation("Performing RRF fusion on {Count} result sets with k={K}", 
            resultSets.Count, k);

        // Dictionary to accumulate RRF scores
        var rrfScores = new Dictionary<string, (RankedResult result, float score)>();

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
                var rrfScore = 1.0f / (k + result.Rank);

                if (rrfScores.ContainsKey(key))
                {
                    // Accumulate RRF score for items appearing in multiple result sets
                    rrfScores[key] = (
                        result: MergeResults(rrfScores[key].result, result),
                        score: rrfScores[key].score + rrfScore
                    );
                    
                    _logger.LogDebug("Accumulated RRF score for {Key}: {Score}", 
                        key, rrfScores[key].score);
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

        _logger.LogInformation("RRF fusion completed: {Count} unique results, returning top {TopN}", 
            rrfScores.Count, fusedResults.Count);

        return fusedResults;
    }

    /// <summary>
    /// Implements weighted linear combination of scores
    /// </summary>
    public IEnumerable<RankedResult> FuseWithWeights(
        Dictionary<string, (IEnumerable<RankedResult> results, float weight)> resultSets,
        int topN = 10)
    {
        if (resultSets == null || !resultSets.Any())
        {
            _logger.LogWarning("No result sets provided for weighted fusion");
            return Enumerable.Empty<RankedResult>();
        }

        _logger.LogInformation("Performing weighted fusion on {Count} result sets", 
            resultSets.Count);

        // Normalize weights to sum to 1
        var totalWeight = resultSets.Sum(rs => rs.Value.weight);
        if (totalWeight <= 0)
        {
            _logger.LogError("Invalid weights: total weight is {TotalWeight}", totalWeight);
            throw new ArgumentException("Total weight must be positive", nameof(resultSets));
        }

        // Dictionary to accumulate weighted scores
        var weightedScores = new Dictionary<string, (RankedResult result, float score)>();

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

                if (weightedScores.ContainsKey(key))
                {
                    weightedScores[key] = (
                        result: MergeResults(weightedScores[key].result, result),
                        score: weightedScores[key].score + weightedScore
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

        _logger.LogInformation("Weighted fusion completed: {Count} unique results, returning top {TopN}", 
            weightedScores.Count, fusedResults.Count);

        return fusedResults;
    }

    /// <summary>
    /// Normalizes scores to [0, 1] range using min-max normalization
    /// </summary>
    public IEnumerable<RankedResult> NormalizeScores(IEnumerable<RankedResult> results)
    {
        var resultList = results.ToList();
        if (!resultList.Any())
        {
            return resultList;
        }

        var minScore = resultList.Min(r => r.Score);
        var maxScore = resultList.Max(r => r.Score);
        var range = maxScore - minScore;

        // If all scores are the same, return uniform scores
        if (range <= float.Epsilon)
        {
            foreach (var result in resultList)
            {
                result.Score = 1.0f;
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
    /// Merges two results representing the same document/chunk
    /// Preserves the result with more complete information
    /// </summary>
    private RankedResult MergeResults(RankedResult existing, RankedResult incoming)
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
                if (!existing.Metadata.ContainsKey(kvp.Key))
                {
                    existing.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }
        else if (existing.Metadata == null && incoming.Metadata != null)
        {
            existing.Metadata = incoming.Metadata;
        }

        return existing;
    }
}