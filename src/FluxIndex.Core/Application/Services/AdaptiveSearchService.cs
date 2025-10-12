using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 적응형 검색 서비스 구현체 - 쿼리 복잡도에 따른 동적 전략 선택
/// </summary>
public class AdaptiveSearchService : IAdaptiveSearchService
{
    private readonly IHybridSearchService _hybridSearchService;
    private readonly ISmallToBigRetriever _smallToBigRetriever;
    private readonly IQueryComplexityAnalyzer _queryAnalyzer;
    private readonly ISemanticCacheService? _semanticCache;
    private readonly ILogger<AdaptiveSearchService> _logger;

    // 전략별 성능 통계 캐시
    private readonly ConcurrentDictionary<SearchStrategy, StrategyMetrics> _strategyMetrics;
    private readonly ConcurrentDictionary<QueryType, SearchStrategy> _optimalStrategies;
    private readonly ConcurrentDictionary<string, AdaptiveSearchResult> _searchCache;

    // 캐시 성능 통계
    private long _totalSearches = 0;
    private long _cacheHits = 0;

    public AdaptiveSearchService(
        IHybridSearchService hybridSearchService,
        ISmallToBigRetriever smallToBigRetriever,
        IQueryComplexityAnalyzer queryAnalyzer,
        ILogger<AdaptiveSearchService> logger,
        ISemanticCacheService? semanticCache = null)
    {
        _hybridSearchService = hybridSearchService ?? throw new ArgumentNullException(nameof(hybridSearchService));
        _smallToBigRetriever = smallToBigRetriever ?? throw new ArgumentNullException(nameof(smallToBigRetriever));
        _queryAnalyzer = queryAnalyzer ?? throw new ArgumentNullException(nameof(queryAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _semanticCache = semanticCache;

        _strategyMetrics = new ConcurrentDictionary<SearchStrategy, StrategyMetrics>();
        _optimalStrategies = new ConcurrentDictionary<QueryType, SearchStrategy>();
        _searchCache = new ConcurrentDictionary<string, AdaptiveSearchResult>();

        InitializeDefaultStrategies();

        if (_semanticCache != null)
        {
            _logger.LogInformation("시맨틱 캐시 활성화됨");
        }
    }

    /// <summary>
    /// 쿼리 분석 기반 적응형 검색
    /// </summary>
    public async Task<AdaptiveSearchResult> SearchAsync(
        string query,
        AdaptiveSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        options ??= new AdaptiveSearchOptions();
        var totalStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("적응형 검색 시작: {Query}", query);

        try
        {
            Interlocked.Increment(ref _totalSearches);

            // 1. 시맨틱 캐시 확인 (0.95 유사도 임계값)
            if (options.UseCache && _semanticCache != null)
            {
                try
                {
                    var cachedResult = await _semanticCache.GetCachedResultAsync(
                        query,
                        similarityThreshold: 0.95f,
                        cancellationToken);

                    if (cachedResult != null)
                    {
                        Interlocked.Increment(ref _cacheHits);

                        var hitRate = (double)_cacheHits / _totalSearches;
                        _logger.LogInformation("🎯 시맨틱 캐시 히트: {Query} (유사도: {Similarity:F3}, 히트율: {HitRate:P1})",
                            query, cachedResult.SimilarityScore, hitRate);

                        // 캐시된 DocumentChunk를 Document 객체로 변환
                        var documents = cachedResult.Results
                            .Select(chunk => CreateDocumentFromChunk(chunk))
                            .ToList();

                        // 전략 결정 (메타데이터에서 가져오거나 기본값 사용)
                        var cachedStrategy = cachedResult.Metadata?.SearchAlgorithm switch
                        {
                            "DirectVector" => SearchStrategy.DirectVector,
                            "KeywordOnly" => SearchStrategy.KeywordOnly,
                            "Hybrid" => SearchStrategy.Hybrid,
                            "TwoStage" => SearchStrategy.TwoStage,
                            _ => SearchStrategy.Hybrid
                        };

                        return new AdaptiveSearchResult
                        {
                            Documents = documents,
                            UsedStrategy = cachedStrategy,
                            QueryAnalysis = new QueryAnalysis
                            {
                                Type = QueryType.SimpleKeyword,
                                Complexity = ComplexityLevel.Simple,
                                ConfidenceScore = cachedResult.SimilarityScore
                            },
                            Performance = new SearchPerformanceMetrics
                            {
                                TotalTime = TimeSpan.FromMilliseconds(5), // 캐시 히트는 매우 빠름
                                AnalysisTime = TimeSpan.Zero,
                                SearchTime = TimeSpan.Zero,
                                PostProcessingTime = TimeSpan.Zero,
                                ResultCount = documents.Count,
                                AverageRelevanceScore = 0.95,
                                CacheHit = true,
                                ResourceUsage = new Dictionary<string, object>
                                {
                                    ["cache_hit"] = true,
                                    ["cached_query"] = cachedResult.OriginalQuery,
                                    ["similarity_score"] = cachedResult.SimilarityScore,
                                    ["cache_hit_rate"] = hitRate
                                }
                            },
                            StrategyReasons = new List<string>
                            {
                                $"시맨틱 캐시 히트 (유사도: {cachedResult.SimilarityScore:F3})",
                                $"원본 쿼리: {cachedResult.OriginalQuery}"
                            },
                            ConfidenceScore = cachedResult.SimilarityScore,
                            Metadata = new Dictionary<string, object>
                            {
                                ["cached"] = true,
                                ["cache_age"] = (DateTime.UtcNow - cachedResult.CachedAt).TotalSeconds
                            }
                        };
                    }

                    var missRate = 1.0 - ((double)_cacheHits / _totalSearches);
                    _logger.LogDebug("시맨틱 캐시 미스: {Query} (현재 히트율: {HitRate:P1})", query, 1.0 - missRate);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "시맨틱 캐시 조회 실패, 일반 검색 진행: {Query}", query);
                }
            }
            else if (options.UseCache && _semanticCache == null)
            {
                _logger.LogDebug("시맨틱 캐시가 구성되지 않음, in-memory 캐시 사용");

                // Fallback: in-memory 캐시
                var cacheKey = GenerateCacheKey(query, options);
                if (_searchCache.TryGetValue(cacheKey, out var memCachedResult))
                {
                    _logger.LogDebug("In-memory 캐시 히트: {Query}", query);
                    return memCachedResult;
                }
            }

            // 2. 쿼리 복잡도 분석
            var analysisStopwatch = Stopwatch.StartNew();
            var queryAnalysis = await _queryAnalyzer.AnalyzeAsync(query, cancellationToken);
            analysisStopwatch.Stop();

            _logger.LogDebug("쿼리 분석 완료: {Type}, {Complexity}, {ConfidenceScore:F3}",
                queryAnalysis.Type, queryAnalysis.Complexity, queryAnalysis.ConfidenceScore);

            // 3. 검색 전략 결정
            var strategy = options.ForceStrategy ?? DetermineOptimalStrategy(queryAnalysis);
            var strategyReasons = new List<string>();

            if (options.ForceStrategy.HasValue)
            {
                strategyReasons.Add($"강제 지정된 전략: {strategy}");
            }
            else
            {
                strategyReasons.Add($"복잡도 {queryAnalysis.Complexity}에 따른 자동 선택: {strategy}");
                strategyReasons.Add($"쿼리 유형: {queryAnalysis.Type}");
                strategyReasons.Add($"분석 신뢰도: {queryAnalysis.ConfidenceScore:F3}");
            }

            _logger.LogDebug("선택된 전략: {Strategy}, 이유: {Reasons}",
                strategy, string.Join(", ", strategyReasons));

            // 4. 검색 실행 (Fallback 전략 포함)
            var searchStopwatch = Stopwatch.StartNew();
            var searchResults = await ExecuteSearchWithFallback(query, strategy, options, strategyReasons, cancellationToken);
            searchStopwatch.Stop();

            // 5. A/B 테스트 처리
            ABTestInfo? abTestInfo = null;
            if (options.EnableABTest)
            {
                abTestInfo = await PerformABTest(query, strategy, queryAnalysis, options, cancellationToken);
            }

            // 6. 결과 구성
            totalStopwatch.Stop();

            var result = new AdaptiveSearchResult
            {
                Documents = searchResults,
                UsedStrategy = strategy,
                QueryAnalysis = queryAnalysis,
                Performance = new SearchPerformanceMetrics
                {
                    TotalTime = totalStopwatch.Elapsed,
                    AnalysisTime = analysisStopwatch.Elapsed,
                    SearchTime = searchStopwatch.Elapsed,
                    PostProcessingTime = TimeSpan.Zero,
                    ResultCount = searchResults.Count(),
                    AverageRelevanceScore = searchResults.Any() ? searchResults.Average(r => r.Metadata.GetValueOrDefault("relevance_score", 0.0) as double? ?? 0.0) : 0.0,
                    CacheHit = false,
                    ResourceUsage = new Dictionary<string, object>
                    {
                        ["memory_usage"] = GC.GetTotalMemory(false),
                        ["strategy"] = strategy.ToString()
                    }
                },
                StrategyReasons = strategyReasons,
                ABTestInfo = abTestInfo,
                ConfidenceScore = queryAnalysis.ConfidenceScore,
                Metadata = new Dictionary<string, object>
                {
                    ["query_hash"] = query.GetHashCode(),
                    ["timestamp"] = DateTime.UtcNow,
                    ["options"] = options
                }
            };

            // 7. 캐시 저장
            if (options.UseCache)
            {
                if (_semanticCache != null && result.Performance.ResultCount > 0)
                {
                    try
                    {
                        // DocumentChunk 목록 생성 (Document → DocumentChunk 변환)
                        var documentChunks = searchResults
                            .Select(doc => new FluxIndex.Domain.Models.DocumentChunk
                            {
                                Id = doc.Metadata.ContainsKey("chunk_id")
                                    ? doc.Metadata["chunk_id"]?.ToString() ?? doc.Id
                                    : doc.Id,
                                DocumentId = doc.Id,
                                Content = doc.Metadata.ContainsKey("chunk_content")
                                    ? doc.Metadata["chunk_content"]?.ToString() ?? ""
                                    : "",
                                Score = doc.Metadata.ContainsKey("relevance_score")
                                    ? Convert.ToSingle(doc.Metadata["relevance_score"])
                                    : 0.0f,
                                Metadata = doc.Metadata
                            })
                            .ToList();

                        // 시맨틱 캐시에 저장 (TTL 1시간)
                        await _semanticCache.SetCachedResultAsync(
                            query,
                            documentChunks.AsReadOnly(),
                            new SearchMetadata
                            {
                                SearchTimeMs = (long)result.Performance.SearchTime.TotalMilliseconds,
                                TotalDocuments = result.Performance.ResultCount,
                                SearchAlgorithm = strategy.ToString(),
                                QualityScore = (float)result.Performance.AverageRelevanceScore
                            },
                            TimeSpan.FromHours(1),
                            cancellationToken);

                        _logger.LogDebug("시맨틱 캐시 저장 완료: {Query}, {Count}개 결과", query, result.Performance.ResultCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "시맨틱 캐시 저장 실패: {Query}", query);
                    }
                }
                else
                {
                    // Fallback: in-memory 캐시
                    var cacheKey = GenerateCacheKey(query, options);
                    _searchCache.TryAdd(cacheKey, result);
                }
            }

            // 8. 성능 통계 업데이트
            await UpdateStrategyMetricsAsync(strategy, result);

            _logger.LogInformation("적응형 검색 완료: {Strategy}, {ResultCount}개 결과, {ElapsedMs}ms",
                strategy, result.Performance.ResultCount, totalStopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "적응형 검색 중 오류 발생: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// 검색 전략 강제 지정
    /// </summary>
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

    /// <summary>
    /// 성능 피드백 업데이트
    /// </summary>
    public async Task UpdateFeedbackAsync(
        string query,
        AdaptiveSearchResult result,
        UserFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("사용자 피드백 업데이트: {Query}, 만족도: {Satisfaction}", query, feedback.Satisfaction);

        // 전략별 통계에 피드백 반영
        if (_strategyMetrics.TryGetValue(result.UsedStrategy, out var metrics))
        {
            // 가중 평균으로 만족도 업데이트
            var totalUses = metrics.TotalUses;
            metrics.AverageSatisfaction = ((metrics.AverageSatisfaction * totalUses) + feedback.Satisfaction) / (totalUses + 1);

            // 관련성 점수도 피드백 반영
            if (feedback.Relevance > 0)
            {
                metrics.AverageRelevance = ((metrics.AverageRelevance * totalUses) + feedback.Relevance) / (totalUses + 1);
            }

            _strategyMetrics.TryUpdate(result.UsedStrategy, metrics, metrics);
        }

        // 쿼리 유형별 최적 전략 재평가
        await ReoptimizeStrategyForQueryType(result.QueryAnalysis.Type);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 검색 전략 성능 통계 조회
    /// </summary>
    public async Task<StrategyPerformanceReport> GetPerformanceReportAsync(CancellationToken cancellationToken = default)
    {
        var report = new StrategyPerformanceReport
        {
            StrategyMetrics = new Dictionary<SearchStrategy, StrategyMetrics>(_strategyMetrics),
            OptimalStrategies = new Dictionary<QueryType, SearchStrategy>(_optimalStrategies),
            Overall = CalculateOverallStatistics(),
            Trends = GenerateTrendData(),
            GeneratedAt = DateTime.UtcNow
        };

        await Task.CompletedTask;
        return report;
    }

    /// <summary>
    /// 시맨틱 캐시 통계 조회
    /// </summary>
    public async Task<Dictionary<string, object>> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, object>
        {
            ["total_searches"] = _totalSearches,
            ["cache_hits"] = _cacheHits,
            ["cache_misses"] = _totalSearches - _cacheHits,
            ["hit_rate"] = _totalSearches > 0 ? (double)_cacheHits / _totalSearches : 0.0,
            ["semantic_cache_enabled"] = _semanticCache != null
        };

        // Redis 캐시 통계 추가
        if (_semanticCache != null)
        {
            try
            {
                var redisCacheStats = await _semanticCache.GetCacheStatisticsAsync(cancellationToken);
                stats["redis_cache_statistics"] = redisCacheStats;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis 캐시 통계 조회 실패");
                stats["redis_cache_error"] = ex.Message;
            }
        }

        return stats;
    }

    #region Private Methods

    private void InitializeDefaultStrategies()
    {
        // 쿼리 유형별 기본 최적 전략
        _optimalStrategies[QueryType.SimpleKeyword] = SearchStrategy.DirectVector;
        _optimalStrategies[QueryType.NaturalQuestion] = SearchStrategy.DirectVector;
        _optimalStrategies[QueryType.ComplexSearch] = SearchStrategy.Hybrid;
        _optimalStrategies[QueryType.ReasoningQuery] = SearchStrategy.Adaptive;
        _optimalStrategies[QueryType.ComparisonQuery] = SearchStrategy.MultiQuery;
        _optimalStrategies[QueryType.TemporalQuery] = SearchStrategy.TwoStage;
        _optimalStrategies[QueryType.MultiHopQuery] = SearchStrategy.SelfRAG;

        // 전략별 기본 통계 초기화
        foreach (SearchStrategy strategy in Enum.GetValues<SearchStrategy>())
        {
            _strategyMetrics[strategy] = new StrategyMetrics
            {
                TotalUses = 0,
                SuccessRate = 0.5,
                AverageProcessingTime = TimeSpan.FromMilliseconds(1000),
                AverageSatisfaction = 3.0,
                AverageRelevance = 0.5,
                UsageRank = 0,
                PerformanceRank = 0
            };
        }
    }

    private SearchStrategy DetermineOptimalStrategy(QueryAnalysis analysis)
    {
        // 1. 쿼리 유형별 최적 전략 확인
        if (_optimalStrategies.TryGetValue(analysis.Type, out var preferredStrategy))
        {
            // 신뢰도가 높으면 선호 전략 사용
            if (analysis.ConfidenceScore >= 0.8)
                return preferredStrategy;
        }

        // 2. QueryComplexityAnalyzer의 추천 전략 사용
        return _queryAnalyzer.RecommendStrategy(analysis);
    }


    /// <summary>
    /// Fallback 전략을 적용한 검색 실행
    /// Vector → Hybrid → Keyword 순차 시도로 Zero-Result 방지
    /// </summary>
    private async Task<IEnumerable<Document>> ExecuteSearchWithFallback(
        string query,
        SearchStrategy primaryStrategy,
        AdaptiveSearchOptions options,
        List<string> strategyReasons,
        CancellationToken cancellationToken)
    {
        // 1차 시도: 주 전략으로 검색
        var results = await ExecuteSearchWithStrategy(query, primaryStrategy, options, cancellationToken);
        var resultCount = results.Count();

        // 결과가 충분하면 반환
        int minResults = Math.Max(1, options.MaxResults / 3); // 최소 목표: MaxResults의 1/3
        if (resultCount >= minResults)
        {
            _logger.LogDebug("주 전략 성공: {Strategy}, {Count}개 결과", primaryStrategy, resultCount);
            return results;
        }

        _logger.LogWarning("주 전략 결과 부족: {Strategy}, {Count}/{MinCount}개",
            primaryStrategy, resultCount, minResults);

        // 2차 시도: Fallback 전략 정의
        var fallbackStrategies = GetFallbackStrategies(primaryStrategy);

        foreach (var fallbackStrategy in fallbackStrategies)
        {
            try
            {
                _logger.LogInformation("Fallback 시도: {Strategy}", fallbackStrategy);

                var fallbackResults = await ExecuteSearchWithStrategy(query, fallbackStrategy, options, cancellationToken);
                var fallbackCount = fallbackResults.Count();

                if (fallbackCount > resultCount)
                {
                    _logger.LogInformation("Fallback 성공: {Strategy}, {Count}개 결과",
                        fallbackStrategy, fallbackCount);

                    strategyReasons.Add($"Fallback 적용: {primaryStrategy} → {fallbackStrategy} ({resultCount} → {fallbackCount}개)");
                    return fallbackResults;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback 전략 실패: {Strategy}", fallbackStrategy);
            }
        }

        // 3차 시도: Zero-Result 방지 - minScore 완화하여 재시도
        if (resultCount == 0 && options.MinScore > 0.0)
        {
            _logger.LogWarning("Zero-Result 감지, minScore 완화 재시도");

            var relaxedOptions = new AdaptiveSearchOptions
            {
                MaxResults = options.MaxResults,
                MinScore = 0.0f, // 임계값 제거
                UseCache = false,
                ForceStrategy = SearchStrategy.Hybrid // 하이브리드로 강제
            };

            var relaxedResults = await ExecuteHybridSearch(query, relaxedOptions, cancellationToken);
            if (relaxedResults.Any())
            {
                strategyReasons.Add($"Zero-Result 방지: minScore 완화 ({options.MinScore:F2} → 0.0)");
                _logger.LogInformation("Zero-Result 방지 성공: {Count}개 결과", relaxedResults.Count());
                return relaxedResults;
            }
        }

        // 모든 시도 실패: 원본 결과 반환 (빈 결과 포함)
        _logger.LogWarning("모든 Fallback 전략 실패, 원본 결과 반환: {Count}개", resultCount);
        strategyReasons.Add($"Fallback 실패: {resultCount}개 결과만 반환");
        return results;
    }

    /// <summary>
    /// 전략별 Fallback 체인 정의
    /// </summary>
    private List<SearchStrategy> GetFallbackStrategies(SearchStrategy primary)
    {
        return primary switch
        {
            SearchStrategy.DirectVector => new List<SearchStrategy>
            {
                SearchStrategy.Hybrid,      // Vector 실패 → Hybrid
                SearchStrategy.KeywordOnly  // Hybrid 실패 → Keyword
            },
            SearchStrategy.KeywordOnly => new List<SearchStrategy>
            {
                SearchStrategy.Hybrid,      // Keyword 실패 → Hybrid
                SearchStrategy.DirectVector // Hybrid 실패 → Vector
            },
            SearchStrategy.Hybrid => new List<SearchStrategy>
            {
                SearchStrategy.DirectVector, // Hybrid 실패 → Vector
                SearchStrategy.KeywordOnly   // Vector 실패 → Keyword
            },
            SearchStrategy.TwoStage => new List<SearchStrategy>
            {
                SearchStrategy.Hybrid,
                SearchStrategy.DirectVector
            },
            SearchStrategy.MultiQuery => new List<SearchStrategy>
            {
                SearchStrategy.Hybrid,
                SearchStrategy.DirectVector
            },
            _ => new List<SearchStrategy>
            {
                SearchStrategy.Hybrid
            }
        };
    }

    private async Task<IEnumerable<Document>> ExecuteSearchWithStrategy(
        string query,
        SearchStrategy strategy,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        return strategy switch
        {
            SearchStrategy.DirectVector => await ExecuteVectorSearch(query, options, cancellationToken),
            SearchStrategy.KeywordOnly => await ExecuteKeywordSearch(query, options, cancellationToken),
            SearchStrategy.Hybrid => await ExecuteHybridSearch(query, options, cancellationToken),
            SearchStrategy.MultiQuery => await ExecuteMultiQuerySearch(query, options, cancellationToken),
            SearchStrategy.TwoStage => await ExecuteTwoStageSearch(query, options, cancellationToken),
            SearchStrategy.Adaptive => await ExecuteAdaptiveSearch(query, options, cancellationToken),
            _ => await ExecuteHybridSearch(query, options, cancellationToken)
        };
    }

    private async Task<IEnumerable<Document>> ExecuteVectorSearch(
        string query,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        var hybridOptions = new FluxIndex.Domain.Models.HybridSearchOptions
        {
            MaxResults = options.MaxResults,
            VectorWeight = 1.0f,
            SparseWeight = 0.0f
        };

        var results = await _hybridSearchService.SearchAsync(query, hybridOptions, cancellationToken);
        return results.Select(r => CreateDocumentFromChunk(r.Chunk));
    }

    private async Task<IEnumerable<Document>> ExecuteKeywordSearch(
        string query,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        var hybridOptions = new FluxIndex.Domain.Models.HybridSearchOptions
        {
            MaxResults = options.MaxResults,
            VectorWeight = 0.0f,
            SparseWeight = 1.0f
        };

        var results = await _hybridSearchService.SearchAsync(query, hybridOptions, cancellationToken);
        return results.Select(r => CreateDocumentFromChunk(r.Chunk));
    }

    private async Task<IEnumerable<Document>> ExecuteHybridSearch(
        string query,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        var hybridOptions = new FluxIndex.Domain.Models.HybridSearchOptions
        {
            MaxResults = options.MaxResults,
            VectorWeight = 0.6f,  // 0.7 → 0.6으로 재조정 (벡터/키워드 밸런싱)
            SparseWeight = 0.4f   // 0.3 → 0.4로 증가 (키워드 검색 강화)
        };

        var results = await _hybridSearchService.SearchAsync(query, hybridOptions, cancellationToken);
        return results.Select(r => CreateDocumentFromChunk(r.Chunk));
    }

    private async Task<IEnumerable<Document>> ExecuteMultiQuerySearch(
        string query,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        // 다중 쿼리 확장: 원본 쿼리의 변형들로 검색
        var expandedQueries = GenerateQueryExpansions(query);
        var allResults = new List<Document>();

        foreach (var expandedQuery in expandedQueries.Take(3))
        {
            var results = await ExecuteHybridSearch(expandedQuery, options, cancellationToken);
            allResults.AddRange(results);
        }

        // 중복 제거 및 스코어 기반 정렬
        return allResults
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .Take(options.MaxResults);
    }

    private async Task<IEnumerable<Document>> ExecuteTwoStageSearch(
        string query,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        // 1단계: Small-to-Big으로 정밀 검색
        var smallToBigOptions = new FluxIndex.Domain.Models.SmallToBigOptions
        {
            MaxResults = Math.Min(options.MaxResults * 2, 20),
            EnableAdaptiveWindowing = true,
            EnableSemanticExpansion = true
        };

        var smallToBigResults = await _smallToBigRetriever.SearchAsync(query, smallToBigOptions, cancellationToken);

        // 2단계: 확장된 컨텍스트로 재검색 (현재는 결과 그대로 반환)
        return smallToBigResults.Select(r => CreateDocumentFromChunk(r.PrimaryChunk)).Take(options.MaxResults);
    }

    private async Task<IEnumerable<Document>> ExecuteAdaptiveSearch(
        string query,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        // 재귀 방지를 위해 하이브리드 검색으로 폴백
        return await ExecuteHybridSearch(query, options, cancellationToken);
    }

    private Document CreateDocumentFromChunk(FluxIndex.Domain.Models.DocumentChunk chunk)
    {
        var document = Document.Create(chunk.DocumentId);
        document.Metadata = chunk.Metadata ?? new Dictionary<string, object>();
        document.Metadata["chunk_id"] = chunk.Id;
        document.Metadata["chunk_content"] = chunk.Content;
        document.Metadata["relevance_score"] = chunk.Score;
        return document;
    }

    private List<string> GenerateQueryExpansions(string query)
    {
        // 간단한 쿼리 확장 로직
        var expansions = new List<string> { query };

        // 동의어 및 관련 용어 추가 (실제로는 더 정교한 로직 필요)
        if (query.Contains("machine learning"))
        {
            expansions.Add(query.Replace("machine learning", "ML"));
            expansions.Add(query.Replace("machine learning", "artificial intelligence"));
        }

        if (query.Contains("neural network"))
        {
            expansions.Add(query.Replace("neural network", "deep learning"));
            expansions.Add(query.Replace("neural network", "artificial neural network"));
        }

        return expansions;
    }

    private async Task<ABTestInfo?> PerformABTest(
        string query,
        SearchStrategy primaryStrategy,
        QueryAnalysis analysis,
        AdaptiveSearchOptions options,
        CancellationToken cancellationToken)
    {
        // 대안 전략 선택
        var alternativeStrategy = GetAlternativeStrategy(primaryStrategy);
        var testId = Guid.NewGuid().ToString("N")[..8];

        try
        {
            // 대안 전략으로도 검색 수행
            var alternativeResults = await ExecuteSearchWithStrategy(query, alternativeStrategy, options, cancellationToken);

            return new ABTestInfo
            {
                TestId = testId,
                Group = "A", // 주 전략
                AlternativeStrategy = alternativeStrategy,
                AlternativeResults = alternativeResults,
                PerformanceComparison = new Dictionary<string, double>
                {
                    ["primary_strategy_score"] = 1.0,
                    ["alternative_strategy_score"] = 0.9 // 실제로는 성능 지표 기반 계산
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A/B 테스트 중 오류: {TestId}", testId);
            return null;
        }
    }

    private SearchStrategy GetAlternativeStrategy(SearchStrategy primary)
    {
        return primary switch
        {
            SearchStrategy.DirectVector => SearchStrategy.Hybrid,
            SearchStrategy.KeywordOnly => SearchStrategy.DirectVector,
            SearchStrategy.Hybrid => SearchStrategy.TwoStage,
            SearchStrategy.MultiQuery => SearchStrategy.Hybrid,
            SearchStrategy.TwoStage => SearchStrategy.MultiQuery,
            _ => SearchStrategy.Hybrid
        };
    }

    private async Task UpdateStrategyMetricsAsync(SearchStrategy strategy, AdaptiveSearchResult result)
    {
        if (_strategyMetrics.TryGetValue(strategy, out var metrics))
        {
            var totalUses = metrics.TotalUses;

            metrics.TotalUses = totalUses + 1;
            metrics.AverageProcessingTime = TimeSpan.FromMilliseconds(
                ((metrics.AverageProcessingTime.TotalMilliseconds * totalUses) + result.Performance.TotalTime.TotalMilliseconds) / (totalUses + 1));

            metrics.AverageRelevance = ((metrics.AverageRelevance * totalUses) + result.Performance.AverageRelevanceScore) / (totalUses + 1);

            _strategyMetrics.TryUpdate(strategy, metrics, metrics);
        }

        await Task.CompletedTask;
    }

    private async Task ReoptimizeStrategyForQueryType(QueryType queryType)
    {
        // 쿼리 유형별 전략들의 성능을 비교하여 최적 전략 업데이트
        var candidateStrategies = new[]
        {
            SearchStrategy.DirectVector,
            SearchStrategy.KeywordOnly,
            SearchStrategy.Hybrid,
            SearchStrategy.TwoStage
        };

        var bestStrategy = candidateStrategies
            .Where(s => _strategyMetrics.ContainsKey(s))
            .OrderByDescending(s => _strategyMetrics[s].AverageSatisfaction)
            .ThenBy(s => _strategyMetrics[s].AverageProcessingTime)
            .FirstOrDefault();

        if (bestStrategy != default)
        {
            _optimalStrategies.AddOrUpdate(queryType, bestStrategy, (key, oldValue) => bestStrategy);
        }

        await Task.CompletedTask;
    }

    private OverallStatistics CalculateOverallStatistics()
    {
        var allMetrics = _strategyMetrics.Values;
        if (!allMetrics.Any()) return new OverallStatistics();

        var totalSearches = allMetrics.Sum(m => m.TotalUses);
        var mostUsedStrategy = _strategyMetrics
            .OrderByDescending(kvp => kvp.Value.TotalUses)
            .FirstOrDefault().Key;

        var bestPerformingStrategy = _strategyMetrics
            .OrderByDescending(kvp => kvp.Value.AverageSatisfaction)
            .FirstOrDefault().Key;

        // 실제 캐시 히트율 계산
        var actualCacheHitRate = _totalSearches > 0
            ? (double)_cacheHits / _totalSearches
            : 0.0;

        return new OverallStatistics
        {
            TotalSearches = totalSearches,
            AverageProcessingTime = TimeSpan.FromMilliseconds(allMetrics.Average(m => m.AverageProcessingTime.TotalMilliseconds)),
            CacheHitRate = actualCacheHitRate,
            OverallSatisfaction = allMetrics.Average(m => m.AverageSatisfaction),
            MostUsedStrategy = mostUsedStrategy,
            BestPerformingStrategy = bestPerformingStrategy
        };
    }

    private List<TrendData> GenerateTrendData()
    {
        // 실제로는 시계열 데이터로부터 생성
        var trends = new List<TrendData>();
        var now = DateTime.UtcNow;

        for (int i = 7; i >= 0; i--)
        {
            trends.Add(new TrendData
            {
                Date = now.AddDays(-i),
                SearchCount = 100 + (i * 10),
                AverageSatisfaction = 3.5 + (i * 0.1),
                PrimaryStrategy = SearchStrategy.Hybrid
            });
        }

        return trends;
    }

    private string GenerateCacheKey(string query, AdaptiveSearchOptions options)
    {
        var keyParts = new[]
        {
            query.GetHashCode().ToString(),
            options.MaxResults.ToString(),
            options.MinScore.ToString("F2"),
            options.ForceStrategy?.ToString() ?? "auto"
        };

        return string.Join("_", keyParts);
    }

    #endregion
}