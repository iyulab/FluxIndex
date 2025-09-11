using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Configuration options for FluxIndex benchmarking
/// </summary>
public class BenchmarkOptions
{
    /// <summary>
    /// Sample size for embedding benchmarks
    /// </summary>
    public int EmbeddingSampleSize { get; set; } = 20;

    /// <summary>
    /// Sample size for search query benchmarks
    /// </summary>
    public int SearchQuerySampleSize { get; set; } = 10;

    /// <summary>
    /// Number of queries to use for cache population
    /// </summary>
    public int CachePopulationSize { get; set; } = 50;

    /// <summary>
    /// Sample size for cache retrieval benchmarks
    /// </summary>
    public int CacheRetrievalSampleSize { get; set; } = 100;

    /// <summary>
    /// Sample size for text completion benchmarks
    /// </summary>
    public int TextCompletionSampleSize { get; set; } = 10;

    /// <summary>
    /// Sample size for end-to-end benchmarks
    /// </summary>
    public int EndToEndSampleSize { get; set; } = 15;

    /// <summary>
    /// Whether to include explanations in reranking benchmarks
    /// </summary>
    public bool IncludeExplanations { get; set; } = false;

    /// <summary>
    /// Number of warmup iterations before actual benchmarking
    /// </summary>
    public int WarmupIterations { get; set; } = 3;

    /// <summary>
    /// Enable detailed performance profiling
    /// </summary>
    public bool EnableProfiling { get; set; } = true;

    /// <summary>
    /// Benchmark timeout in minutes
    /// </summary>
    public double TimeoutMinutes { get; set; } = 30;
}

/// <summary>
/// Services container for benchmarking
/// </summary>
public class BenchmarkServices
{
    public IVectorIndexOptimizer? VectorIndexOptimizer { get; set; }
    public IEmbeddingService? EmbeddingService { get; set; }
    public IReranker? Reranker { get; set; }
    public TwoStageRetriever? TwoStageRetriever { get; set; }
    public ISemanticCache? SemanticCache { get; set; }
    public ITextCompletionService? TextCompletionService { get; set; }
}

/// <summary>
/// Comprehensive benchmark results for all FluxIndex components
/// </summary>
public class BenchmarkResults
{
    public DateTime CompletedAt { get; set; }
    public TimeSpan TotalBenchmarkTime { get; set; }
    public string? Error { get; set; }

    public HNSWOptimizationBenchmark? HNSWOptimizationResults { get; set; }
    public EmbeddingBenchmark? EmbeddingResults { get; set; }
    public RerankingBenchmark? RerankingResults { get; set; }
    public TwoStageBenchmark? TwoStageResults { get; set; }
    public CacheBenchmark? CacheResults { get; set; }
    public TextCompletionBenchmark? TextCompletionResults { get; set; }
    public EndToEndBenchmark? EndToEndResults { get; set; }

    /// <summary>
    /// Overall performance score (0-100)
    /// </summary>
    public double OverallPerformanceScore => CalculateOverallScore();

    /// <summary>
    /// Key performance indicators
    /// </summary>
    public Dictionary<string, object> KPIs => CalculateKPIs();

    /// <summary>
    /// Performance recommendations based on benchmark results
    /// </summary>
    public List<string> Recommendations => GenerateRecommendations();

    private double CalculateOverallScore()
    {
        var scores = new List<double>();

        if (EmbeddingResults != null && EmbeddingResults.SingleEmbeddingLatencyMs.Average > 0)
            scores.Add(Math.Max(0, 100 - EmbeddingResults.SingleEmbeddingLatencyMs.Average)); // Lower is better

        if (RerankingResults?.TestResults.Any() == true)
        {
            var avgLatency = RerankingResults.TestResults.Average(r => r.LatencyMs);
            scores.Add(Math.Max(0, 100 - avgLatency)); // Lower is better
        }

        if (EndToEndResults != null)
        {
            var qualityScore = EndToEndResults.AverageQualityScore * 100;
            var speedScore = Math.Max(0, 100 - EndToEndResults.AverageLatencyMs / 10);
            scores.Add((qualityScore + speedScore) / 2);
        }

        return scores.Any() ? scores.Average() : 0;
    }

    private Dictionary<string, object> CalculateKPIs()
    {
        var kpis = new Dictionary<string, object>();

        if (EmbeddingResults != null)
        {
            kpis["embedding_latency_p95"] = EmbeddingResults.SingleEmbeddingLatencyMs.P95;
            kpis["embedding_dimensions"] = EmbeddingResults.EmbeddingDimensions;
        }

        if (TwoStageResults != null)
        {
            kpis["search_latency_avg"] = TwoStageResults.AverageTotalLatencyMs;
            kpis["recall_efficiency"] = TwoStageResults.AverageRecallCount > 0 
                ? TwoStageResults.AverageFinalCount / TwoStageResults.AverageRecallCount : 0;
        }

        if (CacheResults != null)
        {
            kpis["cache_hit_ratio"] = CacheResults.HitRatio;
            kpis["cache_retrieval_latency"] = CacheResults.RetrievalLatencyMs.Average;
        }

        if (EndToEndResults != null)
        {
            kpis["end_to_end_success_rate"] = EndToEndResults.SuccessRate;
            kpis["average_result_quality"] = EndToEndResults.AverageQualityScore;
        }

        kpis["overall_performance_score"] = OverallPerformanceScore;
        kpis["benchmark_duration_minutes"] = TotalBenchmarkTime.TotalMinutes;

        return kpis;
    }

    private List<string> GenerateRecommendations()
    {
        var recommendations = new List<string>();

        if (EmbeddingResults?.SingleEmbeddingLatencyMs.Average > 500)
        {
            recommendations.Add("Consider enabling embedding caching to improve latency");
        }

        if (TwoStageResults?.AverageTotalLatencyMs > 1000)
        {
            recommendations.Add("Consider optimizing HNSW parameters or reducing recall size");
        }

        if (CacheResults?.HitRatio < 0.7)
        {
            recommendations.Add("Consider lowering similarity threshold or increasing cache size");
        }

        if (EndToEndResults?.SuccessRate < 0.9)
        {
            recommendations.Add("Investigate error handling and service reliability");
        }

        if (OverallPerformanceScore < 70)
        {
            recommendations.Add("Overall performance below target - consider infrastructure scaling");
        }

        return recommendations;
    }
}

#region HNSW Optimization Benchmark Models

public class HNSWOptimizationBenchmark
{
    public List<HNSWTestResult> TestResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public double AverageOptimizationTime { get; set; }
}

public class HNSWTestCase
{
    public int DatasetSize { get; set; }
    public int Dimensions { get; set; }
    public QualityTarget Target { get; set; }

    public override string ToString() => $"{DatasetSize}x{Dimensions}D-{Target}";
}

public class HNSWTestResult
{
    public HNSWTestCase TestCase { get; set; } = new();
    public HNSWParameters OptimizedParameters { get; set; } = new();
    public TimeSpan OptimizationTime { get; set; }
    public long MemoryEstimate { get; set; }
    public double ConstructionTimeEstimate { get; set; }
    public double QueryLatencyEstimate { get; set; }
    public ParameterValidationResult ValidationResult { get; set; } = new();
}

#endregion

#region Embedding Benchmark Models

public class EmbeddingBenchmark
{
    public int TotalEmbeddings { get; set; }
    public int EmbeddingDimensions { get; set; }
    public LatencyMetrics SingleEmbeddingLatencyMs { get; set; } = new();
    public List<BatchEmbeddingResult> BatchResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class BatchEmbeddingResult
{
    public int BatchSize { get; set; }
    public double TotalLatencyMs { get; set; }
    public double LatencyPerItemMs { get; set; }
    public double ThroughputItemsPerSecond { get; set; }
    public bool Success { get; set; }
}

#endregion

#region Reranking Benchmark Models

public class RerankingBenchmark
{
    public List<RerankTestResult> TestResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public RerankModelInfo? ModelInfo { get; set; }
}

public class RerankTestResult
{
    public int CandidateCount { get; set; }
    public int ResultCount { get; set; }
    public double LatencyMs { get; set; }
    public double ThroughputCandidatesPerSecond { get; set; }
    public double AverageScoreChange { get; set; }
    public bool Success { get; set; }
}

#endregion

#region Two-Stage Benchmark Models

public class TwoStageBenchmark
{
    public List<TwoStageTestResult> TestResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public double AverageStage1LatencyMs { get; set; }
    public double AverageStage2LatencyMs { get; set; }
    public double AverageTotalLatencyMs { get; set; }
    public double AverageRecallCount { get; set; }
    public double AverageFinalCount { get; set; }
}

public class TwoStageTestResult
{
    public string Query { get; set; } = string.Empty;
    public long Stage1LatencyMs { get; set; }
    public long Stage2LatencyMs { get; set; }
    public long TotalLatencyMs { get; set; }
    public int RecallCount { get; set; }
    public int FinalCount { get; set; }
    public float RecallToFinalRatio { get; set; }
    public float AverageScoreImprovement { get; set; }
    public bool Success { get; set; }
}

#endregion

#region Cache Benchmark Models

public class CacheBenchmark
{
    public int CachedQueries { get; set; }
    public double PopulationTimeMs { get; set; }
    public LatencyMetrics RetrievalLatencyMs { get; set; } = new();
    public double HitRatio { get; set; }
    public CacheStatistics? Statistics { get; set; }
    public List<string> Errors { get; set; } = new();
}

#endregion

#region Text Completion Benchmark Models

public class TextCompletionBenchmark
{
    public List<TextCompletionTestResult> TestResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public double AverageLatencyMs { get; set; }
    public double AverageCompletionLength { get; set; }
    public double SuccessRate { get; set; }
}

public class TextCompletionTestResult
{
    public string Prompt { get; set; } = string.Empty;
    public string Completion { get; set; } = string.Empty;
    public double LatencyMs { get; set; }
    public int CompletionLength { get; set; }
    public bool Success { get; set; }
}

#endregion

#region End-to-End Benchmark Models

public class EndToEndBenchmark
{
    public List<EndToEndTestResult> TestResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public double AverageLatencyMs { get; set; }
    public double AverageResultCount { get; set; }
    public double AverageQualityScore { get; set; }
    public double SuccessRate { get; set; }
}

public class EndToEndTestResult
{
    public string Query { get; set; } = string.Empty;
    public bool Success { get; set; }
    public double TotalLatencyMs { get; set; }
    public int ResultCount { get; set; }
    public double QualityScore { get; set; }
    public Dictionary<string, double> ComponentLatencies { get; set; } = new();
    public string? Error { get; set; }
}

#endregion

#region Utility Classes

/// <summary>
/// Statistical metrics for latency measurements
/// </summary>
public class LatencyMetrics
{
    public double Average { get; private set; }
    public double Median { get; private set; }
    public double P95 { get; private set; }
    public double P99 { get; private set; }
    public double Min { get; private set; }
    public double Max { get; private set; }
    public double StandardDeviation { get; private set; }
    public int SampleCount { get; private set; }

    public LatencyMetrics() { }

    public LatencyMetrics(IEnumerable<double> values)
    {
        var sortedValues = values.OrderBy(v => v).ToArray();
        SampleCount = sortedValues.Length;

        if (SampleCount == 0) return;

        Average = sortedValues.Average();
        Min = sortedValues.First();
        Max = sortedValues.Last();
        Median = CalculatePercentile(sortedValues, 0.5);
        P95 = CalculatePercentile(sortedValues, 0.95);
        P99 = CalculatePercentile(sortedValues, 0.99);

        var variance = sortedValues.Select(v => Math.Pow(v - Average, 2)).Average();
        StandardDeviation = Math.Sqrt(variance);
    }

    private static double CalculatePercentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;
        
        var index = percentile * (sortedValues.Length - 1);
        var lowerIndex = (int)Math.Floor(index);
        var upperIndex = (int)Math.Ceiling(index);

        if (lowerIndex == upperIndex)
            return sortedValues[lowerIndex];

        var weight = index - lowerIndex;
        return sortedValues[lowerIndex] * (1 - weight) + sortedValues[upperIndex] * weight;
    }
}

#endregion