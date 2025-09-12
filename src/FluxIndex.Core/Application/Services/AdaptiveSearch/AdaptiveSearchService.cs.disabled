using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.AdaptiveSearch;

/// <summary>
/// 적응형 검색 서비스 구현
/// </summary>
public class AdaptiveSearchService : IAdaptiveSearchService
{
    private readonly IQueryComplexityAnalyzer _queryAnalyzer;
    private readonly ISearchService _searchService;
    private readonly IQueryOrchestrator _queryOrchestrator;
    private readonly ITwoStageRetriever _twoStageRetriever;
    private readonly ISemanticCache? _cache;
    private readonly ILogger<AdaptiveSearchService> _logger;
    
    // 성능 통계 저장소
    private readonly Dictionary<SearchStrategy, StrategyMetrics> _strategyMetrics = new();
    private readonly Dictionary<QueryType, SearchStrategy> _optimalStrategies = new();
    private readonly Queue<SearchSession> _recentSessions = new();
    private readonly object _lockObject = new();

    public AdaptiveSearchService(
        IQueryComplexityAnalyzer queryAnalyzer,
        ISearchService searchService,
        IQueryOrchestrator queryOrchestrator,
        ITwoStageRetriever twoStageRetriever,
        ILogger<AdaptiveSearchService> logger,
        ISemanticCache? cache = null)
    {
        _queryAnalyzer = queryAnalyzer ?? throw new ArgumentNullException(nameof(queryAnalyzer));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _queryOrchestrator = queryOrchestrator ?? throw new ArgumentNullException(nameof(queryOrchestrator));
        _twoStageRetriever = twoStageRetriever ?? throw new ArgumentNullException(nameof(twoStageRetriever));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache;

        InitializeStrategyMetrics();
    }

    public async Task<AdaptiveSearchResult> SearchAsync(
        string query, 
        AdaptiveSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AdaptiveSearchOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Starting adaptive search for query: {Query}", query);

        var result = new AdaptiveSearchResult
        {
            Performance = new SearchPerformanceMetrics()
        };

        try
        {
            // 1. 쿼리 분석
            var analysisStopwatch = Stopwatch.StartNew();
            result.QueryAnalysis = await _queryAnalyzer.AnalyzeAsync(query, cancellationToken);
            analysisStopwatch.Stop();
            result.Performance.AnalysisTime = analysisStopwatch.Elapsed;

            _logger.LogDebug("Query analysis: Type={Type}, Complexity={Complexity}, Strategy Recommended",
                result.QueryAnalysis.Type, result.QueryAnalysis.Complexity);

            // 2. 검색 전략 결정
            var strategy = options.ForceStrategy ?? DetermineOptimalStrategy(result.QueryAnalysis);
            result.UsedStrategy = strategy;

            // 3. A/B 테스트 처리
            if (options.EnableABTest)
            {
                result.ABTestInfo = await SetupABTestAsync(query, strategy, result.QueryAnalysis, cancellationToken);
            }

            // 4. 캐시 확인
            if (options.UseCache && _cache != null)
            {
                var cachedResult = await CheckCacheAsync(query, strategy, cancellationToken);
                if (cachedResult != null)
                {
                    _logger.LogDebug("Cache hit for query with strategy {Strategy}", strategy);
                    result.Documents = cachedResult;
                    result.Performance.CacheHit = true;
                    result.Performance.TotalTime = stopwatch.Elapsed;
                    return result;
                }
            }

            // 5. 검색 실행
            var searchStopwatch = Stopwatch.StartNew();
            result.Documents = await ExecuteSearchWithStrategyAsync(query, strategy, options, cancellationToken);
            searchStopwatch.Stop();
            result.Performance.SearchTime = searchStopwatch.Elapsed;

            // 6. 후처리
            var postProcessStopwatch = Stopwatch.StartNew();
            await PostProcessResultsAsync(result, options, cancellationToken);
            postProcessStopwatch.Stop();
            result.Performance.PostProcessingTime = postProcessStopwatch.Elapsed;

            // 7. 캐시 업데이트
            if (options.UseCache && _cache != null && result.Documents.Any())
            {
                await UpdateCacheAsync(query, strategy, result.Documents, cancellationToken);
            }

            // 8. 성능 지표 업데이트
            await UpdatePerformanceMetricsAsync(result, cancellationToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in adaptive search for query: {Query}", query);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            result.Performance.TotalTime = stopwatch.Elapsed;
            
            _logger.LogInformation("Adaptive search completed: Strategy={Strategy}, Results={Count}, Time={Time}ms",
                result.UsedStrategy, result.Documents.Count(), result.Performance.TotalTime.TotalMilliseconds);
        }

        return result;
    }

    public async Task<AdaptiveSearchResult> SearchWithStrategyAsync(
        string query, 
        SearchStrategy strategy, 
        AdaptiveSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AdaptiveSearchOptions();
        options.ForceStrategy = strategy;

        return await SearchAsync(query, options, cancellationToken);
    }

    public async Task UpdateFeedbackAsync(
        string query,
        AdaptiveSearchResult result, 
        UserFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating feedback for query: {Query}, Strategy: {Strategy}, Satisfaction: {Satisfaction}",
            query, result.UsedStrategy, feedback.Satisfaction);

        lock (_lockObject)
        {
            // 전략별 성능 업데이트
            if (_strategyMetrics.ContainsKey(result.UsedStrategy))
            {
                var metrics = _strategyMetrics[result.UsedStrategy];
                
                // 만족도 업데이트 (이동 평균)
                var alpha = 0.1; // 학습률
                metrics.AverageSatisfaction = metrics.AverageSatisfaction * (1 - alpha) + feedback.Satisfaction * alpha;
                
                // 성공률 업데이트 (만족도 3 이상을 성공으로 간주)
                var isSuccess = feedback.Satisfaction >= 3;
                var totalSuccessCount = (int)(metrics.SuccessRate * metrics.TotalUses);
                if (isSuccess) totalSuccessCount++;
                metrics.TotalUses++;
                metrics.SuccessRate = (double)totalSuccessCount / metrics.TotalUses;
                
                _logger.LogDebug("Updated strategy {Strategy} metrics: Success rate {SuccessRate:P}, Satisfaction {Satisfaction:F2}",
                    result.UsedStrategy, metrics.SuccessRate, metrics.AverageSatisfaction);
            }

            // 최적 전략 업데이트
            UpdateOptimalStrategies(result.QueryAnalysis.Type, result.UsedStrategy, feedback);

            // 세션 기록
            _recentSessions.Enqueue(new SearchSession
            {
                Query = query,
                Strategy = result.UsedStrategy,
                QueryType = result.QueryAnalysis.Type,
                Feedback = feedback,
                Timestamp = DateTime.UtcNow,
                ProcessingTime = result.Performance.TotalTime
            });

            // 최근 세션 제한 (메모리 관리)
            while (_recentSessions.Count > 1000)
            {
                _recentSessions.Dequeue();
            }
        }

        // 쿼리 분석기에 성능 피드백
        var searchResult = new SearchResult
        {
            ResultCount = result.Documents.Count(),
            RelevanceScore = result.Performance.AverageRelevanceScore,
            ProcessingTime = result.Performance.TotalTime,
            UserSatisfied = feedback.Satisfaction >= 3
        };

        await _queryAnalyzer.UpdatePerformanceAsync(query, result.QueryAnalysis, searchResult, cancellationToken);
    }

    public async Task<StrategyPerformanceReport> GetPerformanceReportAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        lock (_lockObject)
        {
            var report = new StrategyPerformanceReport();

            // 전략별 성능 통계
            report.StrategyMetrics = new Dictionary<SearchStrategy, StrategyMetrics>(_strategyMetrics);

            // 순위 계산
            var strategies = report.StrategyMetrics.Keys.ToList();
            
            // 사용 빈도 순위
            var usageRanking = strategies.OrderByDescending(s => report.StrategyMetrics[s].TotalUses).ToList();
            for (int i = 0; i < usageRanking.Count; i++)
            {
                report.StrategyMetrics[usageRanking[i]].UsageRank = i + 1;
            }

            // 성능 순위 (만족도 기준)
            var performanceRanking = strategies.OrderByDescending(s => report.StrategyMetrics[s].AverageSatisfaction).ToList();
            for (int i = 0; i < performanceRanking.Count; i++)
            {
                report.StrategyMetrics[performanceRanking[i]].PerformanceRank = i + 1;
            }

            // 쿼리 타입별 최적 전략
            report.OptimalStrategies = new Dictionary<QueryType, SearchStrategy>(_optimalStrategies);

            // 전체 통계
            var allSessions = _recentSessions.ToList();
            report.Overall = new OverallStatistics
            {
                TotalSearches = allSessions.Count,
                AverageProcessingTime = allSessions.Any() ? 
                    TimeSpan.FromMilliseconds(allSessions.Average(s => s.ProcessingTime.TotalMilliseconds)) : 
                    TimeSpan.Zero,
                OverallSatisfaction = allSessions.Any() ? allSessions.Average(s => s.Feedback.Satisfaction) : 0,
                MostUsedStrategy = usageRanking.FirstOrDefault(),
                BestPerformingStrategy = performanceRanking.FirstOrDefault()
            };

            // 최근 트렌드 (7일)
            report.Trends = GenerateTrendData(allSessions);

            return report;
        }
    }

    private SearchStrategy DetermineOptimalStrategy(QueryAnalysis analysis)
    {
        // 1. 학습된 최적 전략 확인
        if (_optimalStrategies.ContainsKey(analysis.Type))
        {
            var optimalStrategy = _optimalStrategies[analysis.Type];
            var metrics = _strategyMetrics[optimalStrategy];
            
            // 신뢰할 만한 데이터가 있고 성과가 좋다면 사용
            if (metrics.TotalUses >= 10 && metrics.SuccessRate > 0.7)
            {
                _logger.LogDebug("Using learned optimal strategy {Strategy} for query type {Type}",
                    optimalStrategy, analysis.Type);
                return optimalStrategy;
            }
        }

        // 2. 쿼리 분석기 추천 사용
        var recommendedStrategy = _queryAnalyzer.RecommendStrategy(analysis);
        _logger.LogDebug("Using analyzer recommended strategy {Strategy}", recommendedStrategy);
        
        return recommendedStrategy;
    }

    private async Task<IEnumerable<Document>> ExecuteSearchWithStrategyAsync(
        string query,
        SearchStrategy strategy,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing search with strategy: {Strategy}", strategy);

        try
        {
            return strategy switch
            {
                SearchStrategy.DirectVector => await ExecuteDirectVectorSearchAsync(query, options, cancellationToken),
                SearchStrategy.KeywordOnly => await ExecuteKeywordSearchAsync(query, options, cancellationToken),
                SearchStrategy.Hybrid => await ExecuteHybridSearchAsync(query, options, cancellationToken),
                SearchStrategy.MultiQuery => await ExecuteMultiQuerySearchAsync(query, options, cancellationToken),
                SearchStrategy.HyDE => await ExecuteHyDESearchAsync(query, options, cancellationToken),
                SearchStrategy.StepBack => await ExecuteStepBackSearchAsync(query, options, cancellationToken),
                SearchStrategy.TwoStage => await ExecuteTwoStageSearchAsync(query, options, cancellationToken),
                SearchStrategy.Adaptive => await ExecuteAdaptiveSearchAsync(query, options, cancellationToken),
                SearchStrategy.SelfRAG => await ExecuteSelfRAGSearchAsync(query, options, cancellationToken),
                _ => await ExecuteHybridSearchAsync(query, options, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Strategy {Strategy} failed, falling back to hybrid search", strategy);
            return await ExecuteHybridSearchAsync(query, options, cancellationToken);
        }
    }

    private async Task<IEnumerable<Document>> ExecuteDirectVectorSearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        return await _searchService.SearchAsync(query, options.MaxResults, cancellationToken);
    }

    private async Task<IEnumerable<Document>> ExecuteKeywordSearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        // BM25 키워드 검색 (구현 가정)
        return await _searchService.SearchAsync(query, options.MaxResults, cancellationToken);
    }

    private async Task<IEnumerable<Document>> ExecuteHybridSearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        return await _searchService.SearchAsync(query, options.MaxResults, cancellationToken);
    }

    private async Task<IEnumerable<Document>> ExecuteMultiQuerySearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        var multiQueryOptions = new QueryOrchestratorOptions
        {
            Strategy = QueryTransformStrategy.MultiQuery,
            MaxQueries = 3,
            TopK = options.MaxResults
        };

        var result = await _queryOrchestrator.ProcessQueryAsync(query, multiQueryOptions, cancellationToken);
        return result.Documents;
    }

    private async Task<IEnumerable<Document>> ExecuteHyDESearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        var hydeOptions = new QueryOrchestratorOptions
        {
            Strategy = QueryTransformStrategy.HyDE,
            TopK = options.MaxResults
        };

        var result = await _queryOrchestrator.ProcessQueryAsync(query, hydeOptions, cancellationToken);
        return result.Documents;
    }

    private async Task<IEnumerable<Document>> ExecuteStepBackSearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        var stepBackOptions = new QueryOrchestratorOptions
        {
            Strategy = QueryTransformStrategy.StepBack,
            TopK = options.MaxResults
        };

        var result = await _queryOrchestrator.ProcessQueryAsync(query, stepBackOptions, cancellationToken);
        return result.Documents;
    }

    private async Task<IEnumerable<Document>> ExecuteTwoStageSearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        var retrievalOptions = new TwoStageRetrievalOptions
        {
            Stage1TopK = Math.Min(options.MaxResults * 3, 100),
            Stage2TopK = options.MaxResults,
            MinScore = options.MinScore
        };

        return await _twoStageRetriever.RetrieveAsync(query, retrievalOptions, cancellationToken);
    }

    private async Task<IEnumerable<Document>> ExecuteAdaptiveSearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        // 적응형 검색: 여러 전략을 순차적으로 시도
        var strategies = new[] 
        { 
            SearchStrategy.Hybrid, 
            SearchStrategy.TwoStage, 
            SearchStrategy.MultiQuery 
        };

        foreach (var strategy in strategies)
        {
            try
            {
                var results = await ExecuteSearchWithStrategyAsync(query, strategy, options, cancellationToken);
                if (results.Any() && results.Count() >= Math.Min(options.MaxResults, 5))
                {
                    return results;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Strategy {Strategy} failed in adaptive search", strategy);
            }
        }

        // 모든 전략이 실패하면 기본 검색
        return await ExecuteDirectVectorSearchAsync(query, options, cancellationToken);
    }

    private async Task<IEnumerable<Document>> ExecuteSelfRAGSearchAsync(
        string query, AdaptiveSearchOptions options, CancellationToken cancellationToken)
    {
        // Self-RAG는 별도 구현 필요 (다음 단계에서 구현)
        _logger.LogWarning("Self-RAG not yet implemented, falling back to two-stage search");
        return await ExecuteTwoStageSearchAsync(query, options, cancellationToken);
    }

    private async Task PostProcessResultsAsync(
        AdaptiveSearchResult result, 
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var documents = result.Documents.ToList();
        
        // 성능 지표 계산
        result.Performance.ResultCount = documents.Count;
        
        if (documents.Any())
        {
            // 평균 관련성 점수 계산 (가정: Document에 Score 속성이 있다고 가정)
            result.Performance.AverageRelevanceScore = 0.85; // 임시값
        }

        // 신뢰도 점수 계산
        result.ConfidenceScore = CalculateConfidenceScore(result);

        // 전략 선택 이유 추가
        result.StrategyReasons.Add($"Query type: {result.QueryAnalysis.Type}");
        result.StrategyReasons.Add($"Complexity: {result.QueryAnalysis.Complexity}");
        
        if (result.UsedStrategy != _queryAnalyzer.RecommendStrategy(result.QueryAnalysis))
        {
            result.StrategyReasons.Add("Overridden by learned preferences");
        }
    }

    private double CalculateConfidenceScore(AdaptiveSearchResult result)
    {
        var confidence = 0.5; // 기본값

        // 결과 개수 기반
        if (result.Performance.ResultCount > 5) confidence += 0.2;
        if (result.Performance.ResultCount > 10) confidence += 0.1;

        // 처리 시간 기반
        if (result.Performance.TotalTime < TimeSpan.FromSeconds(1)) confidence += 0.1;

        // 쿼리 분석 신뢰도 반영
        confidence += result.QueryAnalysis.ConfidenceScore * 0.2;

        return Math.Min(confidence, 1.0);
    }

    private void UpdateOptimalStrategies(QueryType queryType, SearchStrategy usedStrategy, UserFeedback feedback)
    {
        // 만족도가 높으면 해당 쿼리 타입의 최적 전략으로 업데이트
        if (feedback.Satisfaction >= 4)
        {
            if (!_optimalStrategies.ContainsKey(queryType) || 
                _strategyMetrics[usedStrategy].AverageSatisfaction > 
                (_optimalStrategies.ContainsKey(queryType) ? _strategyMetrics[_optimalStrategies[queryType]].AverageSatisfaction : 0))
            {
                _optimalStrategies[queryType] = usedStrategy;
                _logger.LogDebug("Updated optimal strategy for {QueryType} to {Strategy}",
                    queryType, usedStrategy);
            }
        }
    }

    private async Task<IEnumerable<Document>?> CheckCacheAsync(
        string query, SearchStrategy strategy, CancellationToken cancellationToken)
    {
        if (_cache == null) return null;

        try
        {
            var cacheKey = $"adaptive_search:{strategy}:{query.GetHashCode()}";
            var cached = await _cache.GetAsync(cacheKey, 0.95f, 1, cancellationToken);
            
            if (cached is IEnumerable<Document> documents)
            {
                return documents;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache lookup failed");
        }

        return null;
    }

    private async Task UpdateCacheAsync(
        string query, SearchStrategy strategy, IEnumerable<Document> results, CancellationToken cancellationToken)
    {
        if (_cache == null) return;

        try
        {
            var cacheKey = $"adaptive_search:{strategy}:{query.GetHashCode()}";
            await _cache.SetAsync(cacheKey, results, TimeSpan.FromMinutes(30), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache update failed");
        }
    }

    private async Task UpdatePerformanceMetricsAsync(AdaptiveSearchResult result, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        lock (_lockObject)
        {
            if (_strategyMetrics.ContainsKey(result.UsedStrategy))
            {
                var metrics = _strategyMetrics[result.UsedStrategy];
                metrics.TotalUses++;
                
                // 처리 시간 이동 평균 업데이트
                var alpha = 0.1;
                var currentTime = metrics.AverageProcessingTime.TotalMilliseconds;
                var newTime = result.Performance.TotalTime.TotalMilliseconds;
                var updatedTime = currentTime * (1 - alpha) + newTime * alpha;
                metrics.AverageProcessingTime = TimeSpan.FromMilliseconds(updatedTime);
            }
        }
    }

    private async Task<ABTestInfo?> SetupABTestAsync(
        string query, SearchStrategy primaryStrategy, QueryAnalysis analysis, CancellationToken cancellationToken)
    {
        // A/B 테스트 구현 (간단한 버전)
        var random = new Random();
        if (random.NextDouble() < 0.1) // 10% 확률로 A/B 테스트
        {
            var alternativeStrategies = Enum.GetValues<SearchStrategy>()
                .Where(s => s != primaryStrategy)
                .ToList();
            
            if (alternativeStrategies.Any())
            {
                var altStrategy = alternativeStrategies[random.Next(alternativeStrategies.Count)];
                
                return new ABTestInfo
                {
                    TestId = Guid.NewGuid().ToString("N")[..8],
                    Group = "B",
                    AlternativeStrategy = altStrategy
                };
            }
        }

        return null;
    }

    private List<TrendData> GenerateTrendData(List<SearchSession> sessions)
    {
        return sessions
            .Where(s => s.Timestamp >= DateTime.UtcNow.AddDays(-7))
            .GroupBy(s => s.Timestamp.Date)
            .Select(g => new TrendData
            {
                Date = g.Key,
                SearchCount = g.Count(),
                AverageSatisfaction = g.Average(s => s.Feedback.Satisfaction),
                PrimaryStrategy = g.GroupBy(s => s.Strategy)
                    .OrderByDescending(sg => sg.Count())
                    .FirstOrDefault()?.Key ?? SearchStrategy.Hybrid
            })
            .OrderBy(t => t.Date)
            .ToList();
    }

    private void InitializeStrategyMetrics()
    {
        foreach (SearchStrategy strategy in Enum.GetValues<SearchStrategy>())
        {
            _strategyMetrics[strategy] = new StrategyMetrics
            {
                TotalUses = 0,
                SuccessRate = 0.0,
                AverageProcessingTime = TimeSpan.Zero,
                AverageSatisfaction = 0.0,
                AverageRelevance = 0.0
            };
        }
    }
}

/// <summary>
/// 검색 세션 정보
/// </summary>
internal class SearchSession
{
    public string Query { get; set; } = string.Empty;
    public SearchStrategy Strategy { get; set; }
    public QueryType QueryType { get; set; }
    public UserFeedback Feedback { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}