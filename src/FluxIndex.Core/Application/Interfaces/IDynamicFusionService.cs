using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Dynamic Alpha Tuning (DAT) service interface for query-adaptive fusion weight optimization.
/// Research shows 6.6% improvement in retrieval quality with query-type specific weights.
/// </summary>
public interface IDynamicFusionService
{
    /// <summary>
    /// Calculates optimal fusion weights based on query analysis.
    /// Uses query type, complexity, and domain to determine vector vs sparse weights.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dynamic fusion configuration with optimized weights</returns>
    Task<DynamicFusionConfiguration> CalculateDynamicWeightsAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates optimal fusion weights from pre-analyzed query.
    /// Use this when query has already been analyzed by QueryComplexityAnalyzer.
    /// </summary>
    /// <param name="analysis">Pre-computed query analysis</param>
    /// <returns>Dynamic fusion configuration with optimized weights</returns>
    DynamicFusionConfiguration CalculateDynamicWeights(QueryAnalysis analysis);

    /// <summary>
    /// Updates performance feedback for continuous optimization.
    /// Enables learning from retrieval outcomes to refine weight mappings.
    /// </summary>
    /// <param name="configuration">Configuration that was used</param>
    /// <param name="metrics">Observed performance metrics</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdatePerformanceFeedbackAsync(
        DynamicFusionConfiguration configuration,
        FusionPerformanceFeedback metrics,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dynamic fusion configuration result from DAT analysis.
/// </summary>
public class DynamicFusionConfiguration
{
    /// <summary>
    /// Optimized vector search weight (0.0 - 1.0)
    /// </summary>
    public double VectorWeight { get; init; }

    /// <summary>
    /// Optimized sparse/keyword search weight (0.0 - 1.0)
    /// </summary>
    public double SparseWeight { get; init; }

    /// <summary>
    /// Recommended fusion method based on query characteristics
    /// </summary>
    public Domain.Models.FusionMethod RecommendedFusion { get; init; }

    /// <summary>
    /// Query type that determined the weights
    /// </summary>
    public QueryType QueryType { get; init; }

    /// <summary>
    /// Complexity level of the query
    /// </summary>
    public ComplexityLevel Complexity { get; init; }

    /// <summary>
    /// Confidence in the weight recommendation (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Human-readable reasoning for the weight selection
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// Whether to use quantized search for performance
    /// </summary>
    public bool UseQuantizedSearch { get; init; }

    /// <summary>
    /// Detected technical domains in the query
    /// </summary>
    public IReadOnlyList<string> TechnicalDomains { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Performance feedback for DAT learning.
/// </summary>
public class FusionPerformanceFeedback
{
    /// <summary>
    /// Number of relevant results found
    /// </summary>
    public int RelevantResults { get; init; }

    /// <summary>
    /// Total results returned
    /// </summary>
    public int TotalResults { get; init; }

    /// <summary>
    /// User satisfaction indicator (if available)
    /// </summary>
    public bool? UserSatisfied { get; init; }

    /// <summary>
    /// Mean Reciprocal Rank
    /// </summary>
    public double? MRR { get; init; }

    /// <summary>
    /// Search latency in milliseconds
    /// </summary>
    public double LatencyMs { get; init; }
}
