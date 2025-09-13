using System.Diagnostics;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileFlux;
using FluxIndex.SDK;
using Microsoft.Extensions.Logging;

namespace FileFluxIndexSample;

/// <summary>
/// 검색 품질 테스트 서비스
/// </summary>
public class QualityTester
{
    private readonly IFileFluxIntegration _integration;
    private readonly IFluxIndexClient _fluxIndex;
    private readonly ILogger<QualityTester> _logger;

    public QualityTester(
        IFileFluxIntegration integration,
        IFluxIndexClient fluxIndex,
        ILogger<QualityTester> logger)
    {
        _integration = integration;
        _fluxIndex = fluxIndex;
        _logger = logger;
    }

    public async Task<List<QualityResult>> TestSearchQuality(string[] queries)
    {
        var results = new List<QualityResult>();

        foreach (var query in queries)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // 기본 검색
            var basicResults = await _fluxIndex.SearchAsync(query, maxResults: 10);
            
            // 고급 검색 (재순위화 포함)
            var advancedResults = await _fluxIndex.AdvancedSearchAsync(
                query, 
                topK: 10,
                rerankingStrategy: RerankingStrategy.Adaptive);
            
            stopwatch.Stop();

            // 품질 메트릭 계산
            var result = new QualityResult
            {
                Query = query,
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                ResultCount = advancedResults.Count(),
                RecallAt10 = CalculateRecall(query, advancedResults.Take(10)),
                MRR = CalculateMRR(query, advancedResults),
                AverageScore = advancedResults.Take(10).Average(r => r.RerankedScore)
            };

            results.Add(result);
            _logger.LogInformation(
                "Query '{Query}' - Recall@10: {Recall:P0}, MRR: {MRR:F3}",
                query, result.RecallAt10, result.MRR);
        }

        return results;
    }

    public async Task<RerankingComparison> CompareRerankingStrategies(string query)
    {
        var comparison = new RerankingComparison { Query = query };
        var strategies = Enum.GetValues<RerankingStrategy>();

        foreach (var strategy in strategies)
        {
            var stopwatch = Stopwatch.StartNew();
            
            var results = await _fluxIndex.AdvancedSearchAsync(
                query,
                topK: 10,
                rerankingStrategy: strategy);
            
            stopwatch.Stop();

            comparison.StrategyResults.Add(new StrategyResult
            {
                Strategy = strategy,
                TopScore = results.FirstOrDefault()?.RerankedScore ?? 0,
                AverageScore = results.Take(10).Average(r => r.RerankedScore),
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                ResultCount = results.Count()
            });
        }

        return comparison;
    }

    public async Task<ChunkQualityAnalysis> AnalyzeChunkQuality(string documentId)
    {
        var document = await _fluxIndex.GetDocumentAsync(documentId);
        if (document == null) return new ChunkQualityAnalysis();

        var analysis = new ChunkQualityAnalysis
        {
            DocumentId = documentId,
            TotalChunks = document.Chunks.Count
        };

        foreach (var chunk in document.Chunks)
        {
            var quality = chunk.Quality;
            
            analysis.ChunkMetrics.Add(new ChunkMetric
            {
                ChunkIndex = chunk.ChunkIndex,
                ContentCompleteness = quality.ContentCompleteness,
                InformationDensity = quality.InformationDensity,
                Coherence = quality.Coherence,
                TokenCount = chunk.Metadata.TokenCount,
                KeywordCount = chunk.Metadata.Keywords.Count,
                EntityCount = chunk.Metadata.Entities.Count
            });
        }

        // 집계 통계
        analysis.AverageCompleteness = analysis.ChunkMetrics.Average(m => m.ContentCompleteness);
        analysis.AverageInformationDensity = analysis.ChunkMetrics.Average(m => m.InformationDensity);
        analysis.AverageCoherence = analysis.ChunkMetrics.Average(m => m.Coherence);

        return analysis;
    }

    public async Task<RelevanceFeedback> CollectRelevanceFeedback(
        string query,
        IEnumerable<EnhancedSearchResult> results)
    {
        var feedback = new RelevanceFeedback { Query = query };

        // 시뮬레이션: 실제로는 사용자 입력을 받아야 함
        var topResults = results.Take(10).ToList();
        
        for (int i = 0; i < topResults.Count; i++)
        {
            var result = topResults[i];
            
            // 시뮬레이션된 관련성 점수 (실제로는 사용자 피드백)
            var relevance = SimulateRelevance(query, result.Chunk.Content, i);
            
            feedback.Items.Add(new FeedbackItem
            {
                ChunkId = result.Chunk.Id,
                Rank = i + 1,
                RelevanceScore = relevance,
                OriginalScore = result.RerankedScore,
                IsRelevant = relevance >= 3 // 3점 이상을 관련으로 간주
            });
        }

        // 메트릭 계산
        feedback.Precision = feedback.Items.Count(i => i.IsRelevant) / (double)feedback.Items.Count;
        feedback.DCG = CalculateDCG(feedback.Items);
        feedback.NDCG = CalculateNDCG(feedback.Items);

        return feedback;
    }

    private double CalculateRecall(string query, IEnumerable<EnhancedSearchResult> results)
    {
        // 시뮬레이션: 실제로는 ground truth가 필요
        var relevantCount = results.Count(r => IsRelevant(query, r.Chunk.Content));
        return relevantCount / (double)Math.Max(1, results.Count());
    }

    private double CalculateMRR(string query, IEnumerable<EnhancedSearchResult> results)
    {
        var rank = 1;
        foreach (var result in results)
        {
            if (IsRelevant(query, result.Chunk.Content))
            {
                return 1.0 / rank;
            }
            rank++;
        }
        return 0;
    }

    private bool IsRelevant(string query, string content)
    {
        // 간단한 관련성 판단 (실제로는 더 정교한 로직 필요)
        var queryTerms = query.ToLower().Split(' ');
        var contentLower = content.ToLower();
        
        var matchCount = queryTerms.Count(term => contentLower.Contains(term));
        return matchCount >= queryTerms.Length * 0.5;
    }

    private int SimulateRelevance(string query, string content, int rank)
    {
        // 시뮬레이션된 관련성 점수 (1-5)
        var baseRelevance = IsRelevant(query, content) ? 4 : 2;
        var rankPenalty = Math.Min(1, rank / 5);
        return Math.Max(1, baseRelevance - rankPenalty);
    }

    private double CalculateDCG(List<FeedbackItem> items)
    {
        double dcg = 0;
        for (int i = 0; i < items.Count; i++)
        {
            dcg += (Math.Pow(2, items[i].RelevanceScore) - 1) / Math.Log(i + 2, 2);
        }
        return dcg;
    }

    private double CalculateNDCG(List<FeedbackItem> items)
    {
        var dcg = CalculateDCG(items);
        
        // IDCG 계산 (이상적인 순서)
        var sortedItems = items.OrderByDescending(i => i.RelevanceScore).ToList();
        var idcg = CalculateDCG(sortedItems);
        
        return idcg > 0 ? dcg / idcg : 0;
    }
}

public class QualityResult
{
    public string Query { get; set; } = string.Empty;
    public long ResponseTimeMs { get; set; }
    public int ResultCount { get; set; }
    public double RecallAt10 { get; set; }
    public double MRR { get; set; }
    public double AverageScore { get; set; }
}

public class RerankingComparison
{
    public string Query { get; set; } = string.Empty;
    public List<StrategyResult> StrategyResults { get; set; } = new();
}

public class StrategyResult
{
    public RerankingStrategy Strategy { get; set; }
    public double TopScore { get; set; }
    public double AverageScore { get; set; }
    public long ProcessingTimeMs { get; set; }
    public int ResultCount { get; set; }
}

public class ChunkQualityAnalysis
{
    public string DocumentId { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public List<ChunkMetric> ChunkMetrics { get; set; } = new();
    public double AverageCompleteness { get; set; }
    public double AverageInformationDensity { get; set; }
    public double AverageCoherence { get; set; }
}

public class ChunkMetric
{
    public int ChunkIndex { get; set; }
    public double ContentCompleteness { get; set; }
    public double InformationDensity { get; set; }
    public double Coherence { get; set; }
    public int TokenCount { get; set; }
    public int KeywordCount { get; set; }
    public int EntityCount { get; set; }
}

public class RelevanceFeedback
{
    public string Query { get; set; } = string.Empty;
    public List<FeedbackItem> Items { get; set; } = new();
    public double Precision { get; set; }
    public double DCG { get; set; }
    public double NDCG { get; set; }
}

public class FeedbackItem
{
    public string ChunkId { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int RelevanceScore { get; set; } // 1-5
    public double OriginalScore { get; set; }
    public bool IsRelevant { get; set; }
}