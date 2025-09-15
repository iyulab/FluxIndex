using System.Collections.Generic;

namespace FluxIndex.Application.Interfaces;

/// <summary>
/// Service for fusing multiple ranked result sets using various fusion algorithms
/// </summary>
public interface IRankFusionService
{
    /// <summary>
    /// Fuses multiple result sets using Reciprocal Rank Fusion (RRF)
    /// </summary>
    /// <param name="resultSets">Dictionary of result sets, keyed by source name</param>
    /// <param name="k">RRF constant parameter (default: 60)</param>
    /// <param name="topN">Number of top results to return</param>
    /// <returns>Fused and re-ranked results</returns>
    IEnumerable<RankedResult> FuseWithRRF(
        Dictionary<string, IEnumerable<RankedResult>> resultSets,
        int k = 60,
        int topN = 10);

    /// <summary>
    /// Fuses multiple result sets using weighted linear combination
    /// </summary>
    /// <param name="resultSets">Dictionary of result sets with weights</param>
    /// <param name="topN">Number of top results to return</param>
    /// <returns>Fused and re-ranked results</returns>
    IEnumerable<RankedResult> FuseWithWeights(
        Dictionary<string, (IEnumerable<RankedResult> results, float weight)> resultSets,
        int topN = 10);

    /// <summary>
    /// Normalizes scores across different result sets
    /// </summary>
    /// <param name="results">Results to normalize</param>
    /// <returns>Results with normalized scores</returns>
    IEnumerable<RankedResult> NormalizeScores(IEnumerable<RankedResult> results);
}

/// <summary>
/// Represents a ranked search result
/// </summary>
public class RankedResult
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public float Score { get; set; }
    public int Rank { get; set; }
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Creates a unique key for deduplication
    /// </summary>
    public string GetUniqueKey() => $"{DocumentId}:{ChunkId}";
}