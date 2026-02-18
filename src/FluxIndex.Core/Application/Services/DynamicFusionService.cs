using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Alias to avoid ambiguity with Domain.Models.QueryType
using AppQueryType = FluxIndex.Core.Application.Interfaces.QueryType;

namespace FluxIndex.Core.Services;

/// <summary>
/// Dynamic Alpha Tuning (DAT) service implementation.
/// Provides query-adaptive fusion weight optimization based on research findings
/// showing 6.6% improvement with query-type specific weights.
/// </summary>
public partial class DynamicFusionService : IDynamicFusionService
{
    private readonly IQueryComplexityAnalyzer _queryAnalyzer;
    private readonly ILogger<DynamicFusionService> _logger;

    /// <summary>
    /// Research-based default weights by query type.
    /// These weights are derived from empirical studies showing optimal
    /// vector vs sparse ratios for different query categories.
    /// </summary>
    private static readonly Dictionary<AppQueryType, (double Vector, double Sparse)> QueryTypeWeights = new()
    {
        // Simple keywords → favor keyword matching (high precision for exact terms)
        [AppQueryType.SimpleKeyword] = (0.35, 0.65),

        // Natural language questions → favor semantic understanding
        [AppQueryType.NaturalQuestion] = (0.70, 0.30),

        // Complex boolean/structured queries → balanced with slight keyword preference
        [AppQueryType.ComplexSearch] = (0.45, 0.55),

        // Reasoning queries → strong semantic preference
        [AppQueryType.ReasoningQuery] = (0.80, 0.20),

        // Comparison queries → balanced for both semantic and term matching
        [AppQueryType.ComparisonQuery] = (0.55, 0.45),

        // Temporal queries → slight semantic preference
        [AppQueryType.TemporalQuery] = (0.60, 0.40),

        // Multi-hop reasoning → strong semantic for understanding relationships
        [AppQueryType.MultiHopQuery] = (0.75, 0.25)
    };

    /// <summary>
    /// Domain-specific weight adjustments.
    /// Technical domains often benefit from keyword matching.
    /// </summary>
    private static readonly Dictionary<string, (double VectorDelta, double SparseDelta)> DomainAdjustments = new()
    {
        // Programming → boost keyword for API names, syntax
        ["programming"] = (-0.10, +0.10),

        // AI/ML → boost semantic for conceptual understanding
        ["ai_ml"] = (+0.05, -0.05),

        // Database → balanced, specific terms matter
        ["database"] = (-0.05, +0.05),

        // DevOps → keyword important for tool names, commands
        ["devops"] = (-0.08, +0.08),

        // Korean → slight semantic boost for morphological variations
        ["korean"] = (+0.03, -0.03)
    };

    /// <summary>
    /// Fusion method recommendations by query characteristics.
    /// </summary>
    private static readonly Dictionary<AppQueryType, FusionMethod> RecommendedFusionMethods = new()
    {
        [AppQueryType.SimpleKeyword] = FusionMethod.WeightedSum,
        [AppQueryType.NaturalQuestion] = FusionMethod.RelativeScoreFusion,
        [AppQueryType.ComplexSearch] = FusionMethod.RRF,
        [AppQueryType.ReasoningQuery] = FusionMethod.RelativeScoreFusion,
        [AppQueryType.ComparisonQuery] = FusionMethod.RRF,
        [AppQueryType.TemporalQuery] = FusionMethod.WeightedSum,
        [AppQueryType.MultiHopQuery] = FusionMethod.RelativeScoreFusion
    };

    public DynamicFusionService(
        IQueryComplexityAnalyzer queryAnalyzer,
        ILogger<DynamicFusionService> logger)
    {
        _queryAnalyzer = queryAnalyzer ?? throw new ArgumentNullException(nameof(queryAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DynamicFusionConfiguration> CalculateDynamicWeightsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return CreateDefaultConfiguration();
        }

        var analysis = await _queryAnalyzer.AnalyzeAsync(query, cancellationToken);
        return CalculateDynamicWeights(analysis);
    }

    /// <inheritdoc />
    public DynamicFusionConfiguration CalculateDynamicWeights(QueryAnalysis analysis)
    {
        // Start with base weights for the query type
        var (vectorWeight, sparseWeight) = GetBaseWeights(analysis.Type);

        // Apply domain-specific adjustments
        (vectorWeight, sparseWeight) = ApplyDomainAdjustments(
            vectorWeight, sparseWeight, analysis.TechnicalDomains);

        // Apply complexity adjustments
        (vectorWeight, sparseWeight) = ApplyComplexityAdjustments(
            vectorWeight, sparseWeight, analysis);

        // Normalize weights to sum to 1.0
        var total = vectorWeight + sparseWeight;
        vectorWeight /= total;
        sparseWeight /= total;

        // Clamp to valid range
        vectorWeight = Math.Clamp(vectorWeight, 0.15, 0.90);
        sparseWeight = Math.Clamp(sparseWeight, 0.10, 0.85);

        // Re-normalize after clamping
        total = vectorWeight + sparseWeight;
        vectorWeight /= total;
        sparseWeight /= total;

        // Determine fusion method
        var fusionMethod = SelectFusionMethod(analysis);

        // Calculate confidence
        var confidence = CalculateConfidence(analysis);

        var config = new DynamicFusionConfiguration
        {
            VectorWeight = vectorWeight,
            SparseWeight = sparseWeight,
            RecommendedFusion = fusionMethod,
            QueryType = analysis.Type,
            Complexity = analysis.Complexity,
            Confidence = confidence,
            Reasoning = GenerateReasoning(analysis, vectorWeight, sparseWeight, fusionMethod),
            UseQuantizedSearch = analysis.Complexity >= ComplexityLevel.Complex,
            TechnicalDomains = analysis.TechnicalDomains
        };

        LogDatCalculated(_logger, config.VectorWeight, config.SparseWeight, config.QueryType,
            config.RecommendedFusion, config.Confidence);

        return config;
    }

    /// <inheritdoc />
    public Task UpdatePerformanceFeedbackAsync(
        DynamicFusionConfiguration configuration,
        FusionPerformanceFeedback metrics,
        CancellationToken cancellationToken = default)
    {
        // Log performance feedback for potential weight tuning
        LogDatFeedback(_logger, configuration.QueryType, configuration.VectorWeight,
            configuration.SparseWeight, metrics.RelevantResults, metrics.TotalResults,
            metrics.MRR, metrics.LatencyMs);

        // Future: Implement online learning to adjust weights based on feedback
        // For now, just log for analysis

        return Task.CompletedTask;
    }

    #region Private Methods

    private static (double Vector, double Sparse) GetBaseWeights(AppQueryType queryType)
    {
        if (QueryTypeWeights.TryGetValue(queryType, out var weights))
        {
            return weights;
        }

        // Default balanced weights
        return (0.60, 0.40);
    }

    private static (double Vector, double Sparse) ApplyDomainAdjustments(
        double vectorWeight,
        double sparseWeight,
        IReadOnlyList<string> domains)
    {
        foreach (var domain in domains)
        {
            if (DomainAdjustments.TryGetValue(domain.ToLowerInvariant(), out var adjustment))
            {
                vectorWeight += adjustment.VectorDelta;
                sparseWeight += adjustment.SparseDelta;
            }
        }

        return (vectorWeight, sparseWeight);
    }

    private static (double Vector, double Sparse) ApplyComplexityAdjustments(
        double vectorWeight,
        double sparseWeight,
        QueryAnalysis analysis)
    {
        // High specificity → boost keyword matching
        if (analysis.Specificity > 0.6)
        {
            vectorWeight -= 0.05;
            sparseWeight += 0.05;
        }

        // Many entities → boost keyword matching for exact matches
        if (analysis.Entities.Count >= 2)
        {
            vectorWeight -= 0.05;
            sparseWeight += 0.05;
        }

        // Reasoning required → boost semantic understanding
        if (analysis.RequiresReasoning)
        {
            vectorWeight += 0.10;
            sparseWeight -= 0.10;
        }

        // Multi-hop → strong semantic preference
        if (analysis.IsMultiHop)
        {
            vectorWeight += 0.08;
            sparseWeight -= 0.08;
        }

        // Long queries → semantic benefits more
        if (analysis.Keywords.Count > 8)
        {
            vectorWeight += 0.05;
            sparseWeight -= 0.05;
        }

        // Very short queries → keyword matching more reliable
        if (analysis.Keywords.Count <= 2)
        {
            vectorWeight -= 0.08;
            sparseWeight += 0.08;
        }

        return (vectorWeight, sparseWeight);
    }

    private static FusionMethod SelectFusionMethod(QueryAnalysis analysis)
    {
        // High complexity → RSF for score magnitude preservation
        if (analysis.Complexity >= ComplexityLevel.Complex)
        {
            return FusionMethod.RelativeScoreFusion;
        }

        // Technical with high specificity → WeightedSum
        if (analysis.ContainsTechnicalTerms && analysis.Specificity > 0.5)
        {
            return FusionMethod.WeightedSum;
        }

        // Multi-hop needs exact matches → Product fusion
        if (analysis.IsMultiHop)
        {
            return FusionMethod.Product;
        }

        // Check query type default
        if (RecommendedFusionMethods.TryGetValue(analysis.Type, out var method))
        {
            return method;
        }

        // Default to RRF for robustness
        return FusionMethod.RRF;
    }

    private static double CalculateConfidence(QueryAnalysis analysis)
    {
        var confidence = analysis.ConfidenceScore;

        // Boost confidence for clear patterns
        if (analysis.Type != AppQueryType.SimpleKeyword)
        {
            confidence += 0.05;
        }

        // Technical domains increase pattern confidence
        if (analysis.TechnicalDomains.Count > 0)
        {
            confidence += 0.05;
        }

        // Reasoning queries are well-defined
        if (analysis.RequiresReasoning)
        {
            confidence += 0.05;
        }

        return Math.Clamp(confidence, 0.3, 0.95);
    }

    private static string GenerateReasoning(
        QueryAnalysis analysis,
        double vectorWeight,
        double sparseWeight,
        FusionMethod fusion)
    {
        var parts = new List<string>
        {
            $"Type: {analysis.Type}",
            $"Complexity: {analysis.Complexity}",
            $"Weights: V={vectorWeight:F2}/S={sparseWeight:F2}"
        };

        if (analysis.TechnicalDomains.Count > 0)
        {
            parts.Add($"Domains: {string.Join(", ", analysis.TechnicalDomains)}");
        }

        if (analysis.RequiresReasoning)
        {
            parts.Add("Reasoning: +semantic");
        }

        if (analysis.IsMultiHop)
        {
            parts.Add("MultiHop: +semantic");
        }

        if (analysis.Specificity > 0.6)
        {
            parts.Add("HighSpecificity: +keyword");
        }

        parts.Add($"→ {fusion}");

        return string.Join("; ", parts);
    }

    private static DynamicFusionConfiguration CreateDefaultConfiguration()
    {
        return new DynamicFusionConfiguration
        {
            VectorWeight = 0.60,
            SparseWeight = 0.40,
            RecommendedFusion = FusionMethod.RRF,
            QueryType = AppQueryType.SimpleKeyword,
            Complexity = ComplexityLevel.Simple,
            Confidence = 0.5,
            Reasoning = "Default configuration for empty query",
            UseQuantizedSearch = false,
            TechnicalDomains = Array.Empty<string>()
        };
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "DAT calculated: Vector={VectorWeight}, Sparse={SparseWeight}, Type={QueryType}, Fusion={Fusion}, Confidence={Confidence}")]
    private static partial void LogDatCalculated(ILogger logger, double vectorWeight, double sparseWeight, AppQueryType queryType, FusionMethod fusion, double confidence);

    [LoggerMessage(Level = LogLevel.Information, Message = "DAT performance feedback: QueryType={QueryType}, Weights=({Vector}/{Sparse}), Results={Results}/{Total}, MRR={MRR}, Latency={Latency}ms")]
    private static partial void LogDatFeedback(ILogger logger, AppQueryType queryType, double vector, double sparse, int results, int total, double? mrr, double latency);

    #endregion
}
