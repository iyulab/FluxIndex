using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
// IMemoryCache는 Core 프로젝트에서 사용하지 않음
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Small-to-Big 검색 구현체 - 정밀 검색과 컨텍스트 확장
/// </summary>
public class SmallToBigRetriever : ISmallToBigRetriever
{
    private readonly IHybridSearchService _hybridSearchService;
    private readonly IChunkHierarchyRepository _hierarchyRepository;
    private readonly ILogger<SmallToBigRetriever> _logger;

    // 쿼리 복잡도 분석 캐시
    private readonly ConcurrentDictionary<string, QueryComplexityAnalysis> _complexityCache;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

    public SmallToBigRetriever(
        IHybridSearchService hybridSearchService,
        IChunkHierarchyRepository hierarchyRepository,
        ILogger<SmallToBigRetriever> logger)
    {
        _hybridSearchService = hybridSearchService ?? throw new ArgumentNullException(nameof(hybridSearchService));
        _hierarchyRepository = hierarchyRepository ?? throw new ArgumentNullException(nameof(hierarchyRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _complexityCache = new ConcurrentDictionary<string, QueryComplexityAnalysis>();
    }

    /// <summary>
    /// Small-to-Big 검색 실행
    /// </summary>
    public async Task<IReadOnlyList<SmallToBigResult>> SearchAsync(
        string query,
        SmallToBigOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SmallToBigResult>();

        options ??= new SmallToBigOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Small-to-Big 검색 시작: {Query}", query);

        try
        {
            // 1. 쿼리 복잡도 분석 및 전략 결정
            var complexityAnalysis = await AnalyzeQueryComplexityAsync(query, cancellationToken);
            var optimalWindowSize = options.EnableAdaptiveWindowing
                ? complexityAnalysis.RecommendedWindowSize
                : options.DefaultWindowSize;

            _logger.LogDebug("쿼리 복잡도: {Complexity:F2}, 권장 윈도우: {WindowSize}",
                complexityAnalysis.OverallComplexity, optimalWindowSize);

            // 2. Small 검색: 정밀한 매칭 (하이브리드 검색 활용)
            var preciseResults = await ExecutePreciseSearchAsync(query, options, cancellationToken);

            if (!preciseResults.Any())
            {
                _logger.LogWarning("정밀 검색에서 결과를 찾지 못함: {Query}", query);
                return Array.Empty<SmallToBigResult>();
            }

            _logger.LogDebug("정밀 검색 결과: {Count}개", preciseResults.Count);

            // 3. Big 확장: 컨텍스트 확장
            var expandedResults = new List<SmallToBigResult>();

            var expansionTasks = preciseResults.Take(options.MaxResults).Select(async result =>
            {
                return await CreateSmallToBigResultAsync(
                    query, result.Chunk, optimalWindowSize, options, complexityAnalysis, cancellationToken);
            });

            var expandedArray = await Task.WhenAll(expansionTasks);
            expandedResults.AddRange(expandedArray.Where(r => r != null));

            // 4. 결과 정렬 및 후처리
            var finalResults = expandedResults
                .Where(r => r.RelevanceScore >= options.MinRelevanceScore)
                .OrderByDescending(r => r.RelevanceScore)
                .Take(options.MaxResults)
                .ToList();

            stopwatch.Stop();
            _logger.LogInformation("Small-to-Big 검색 완료: {ResultCount}개 결과, {ElapsedMs}ms",
                finalResults.Count, stopwatch.ElapsedMilliseconds);

            return finalResults.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Small-to-Big 검색 중 오류 발생: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// 쿼리 복잡도에 따른 최적 윈도우 크기 결정
    /// </summary>
    public async Task<int> DetermineOptimalWindowSizeAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeQueryComplexityAsync(query, cancellationToken);
        return analysis.RecommendedWindowSize;
    }

    /// <summary>
    /// 쿼리 복잡도 상세 분석
    /// </summary>
    public async Task<QueryComplexityAnalysis> AnalyzeQueryComplexityAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryComplexityAnalysis
            {
                OverallComplexity = 0.0,
                RecommendedWindowSize = 1,
                AnalysisConfidence = 1.0
            };
        }

        // 캐시 확인
        var cacheKey = GenerateCacheKey(query);
        if (_complexityCache.TryGetValue(cacheKey, out var cached))
        {
            _logger.LogDebug("복잡도 분석 캐시 히트: {Query}", query);
            return cached;
        }

        await Task.CompletedTask; // 비동기 인터페이스 준수

        // 복잡도 구성요소 분석
        var components = AnalyzeComplexityComponents(query);

        // 각 차원별 복잡도 계산
        var lexicalComplexity = CalculateLexicalComplexity(components);
        var syntacticComplexity = CalculateSyntacticComplexity(components);
        var semanticComplexity = CalculateSemanticComplexity(components);
        var reasoningComplexity = CalculateReasoningComplexity(components);

        // 전체 복잡도 (가중 평균)
        var overallComplexity =
            (lexicalComplexity * 0.2) +
            (syntacticComplexity * 0.3) +
            (semanticComplexity * 0.3) +
            (reasoningComplexity * 0.2);

        // 윈도우 크기 결정
        var windowSize = DetermineWindowSizeFromComplexity(overallComplexity, components);

        var analysis = new QueryComplexityAnalysis
        {
            OverallComplexity = overallComplexity,
            LexicalComplexity = lexicalComplexity,
            SyntacticComplexity = syntacticComplexity,
            SemanticComplexity = semanticComplexity,
            ReasoningComplexity = reasoningComplexity,
            RecommendedWindowSize = windowSize,
            AnalysisConfidence = CalculateAnalysisConfidence(components),
            Components = components
        };

        // 캐시에 저장
        _complexityCache.TryAdd(cacheKey, analysis);

        _logger.LogDebug("쿼리 복잡도 분석 완료: {Query} → 복잡도 {Complexity:F2}, 윈도우 {WindowSize}",
            query, overallComplexity, windowSize);

        return analysis;
    }

    /// <summary>
    /// 청크 계층 구조 구축
    /// </summary>
    public async Task<HierarchyBuildResult> BuildChunkHierarchyAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var stopwatch = Stopwatch.StartNew();
        var hierarchyCount = 0;
        var relationshipCount = 0;
        var errors = new List<string>();

        _logger.LogInformation("청크 계층 구조 구축 시작: {ChunkCount}개 청크", chunkList.Count);

        try
        {
            // 문서별로 그룹화
            var documentGroups = chunkList.GroupBy(c => c.DocumentId);

            foreach (var group in documentGroups)
            {
                var documentChunks = group.OrderBy(c => c.ChunkIndex).ToList();

                // 1. 기본 계층 구조 생성 (순차적)
                await BuildSequentialHierarchyAsync(documentChunks, cancellationToken);
                hierarchyCount += documentChunks.Count;

                // 2. 의미적 관계 분석 및 추가
                var semanticRelationships = await AnalyzeSemanticRelationshipsAsync(documentChunks, cancellationToken);
                foreach (var relationship in semanticRelationships)
                {
                    await _hierarchyRepository.SaveRelationshipAsync(relationship, cancellationToken);
                }
                relationshipCount += semanticRelationships.Count;

                // 3. 계층적 관계 구축 (문장 → 문단 → 섹션)
                await BuildHierarchicalStructureAsync(documentChunks, cancellationToken);
            }

            stopwatch.Stop();

            var result = new HierarchyBuildResult
            {
                HierarchyCount = hierarchyCount,
                RelationshipCount = relationshipCount,
                SuccessRate = errors.Count == 0 ? 1.0 : Math.Max(0.0, 1.0 - (errors.Count / (double)chunkList.Count)),
                BuildTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                QualityScore = CalculateHierarchyQuality(hierarchyCount, relationshipCount),
                Errors = errors
            };

            _logger.LogInformation("청크 계층 구조 구축 완료: {HierarchyCount}개 계층, {RelationshipCount}개 관계, {ElapsedMs}ms",
                hierarchyCount, relationshipCount, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "청크 계층 구조 구축 중 오류 발생");
            errors.Add(ex.Message);

            return new HierarchyBuildResult
            {
                HierarchyCount = hierarchyCount,
                RelationshipCount = relationshipCount,
                SuccessRate = 0.0,
                BuildTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                QualityScore = 0.0,
                Errors = errors
            };
        }
    }

    /// <summary>
    /// 특정 청크의 컨텍스트 확장
    /// </summary>
    public async Task<ContextExpansionResult> ExpandContextAsync(
        DocumentChunk primaryChunk,
        int windowSize,
        ContextExpansionOptions? expansionOptions = null,
        CancellationToken cancellationToken = default)
    {
        expansionOptions ??= new ContextExpansionOptions();
        var stopwatch = Stopwatch.StartNew();
        var expandedChunks = new List<DocumentChunk>();
        var expansionBreakdown = new Dictionary<ExpansionMethod, int>();

        _logger.LogDebug("컨텍스트 확장 시작: {ChunkId}, 윈도우 크기: {WindowSize}", primaryChunk.Id, windowSize);

        try
        {
            // 1. 계층적 확장 (부모-자식 관계)
            if (expansionOptions.EnableHierarchicalExpansion)
            {
                var hierarchicalChunks = await ExpandHierarchicallyAsync(primaryChunk, windowSize / 3, cancellationToken);
                expandedChunks.AddRange(hierarchicalChunks);
                expansionBreakdown[ExpansionMethod.Hierarchical] = hierarchicalChunks.Count;
            }

            // 2. 순차적 확장 (인접 청크)
            if (expansionOptions.EnableSequentialExpansion)
            {
                var sequentialChunks = await ExpandSequentiallyAsync(primaryChunk, windowSize / 2, cancellationToken);
                expandedChunks.AddRange(sequentialChunks);
                expansionBreakdown[ExpansionMethod.Sequential] = sequentialChunks.Count;
            }

            // 3. 의미적 확장 (유사한 주제)
            if (expansionOptions.EnableSemanticExpansion)
            {
                var semanticChunks = await ExpandSemanticallyAsync(
                    primaryChunk, windowSize / 4, expansionOptions.SemanticSimilarityThreshold, cancellationToken);
                expandedChunks.AddRange(semanticChunks);
                expansionBreakdown[ExpansionMethod.Semantic] = semanticChunks.Count;
            }

            // 4. 중복 제거
            if (expansionOptions.EnableDeduplication)
            {
                expandedChunks = DeduplicateChunks(expandedChunks, expansionOptions.DeduplicationThreshold);
            }

            // 5. 품질 필터링
            var qualityFiltered = expandedChunks
                .Where(c => EvaluateChunkQuality(c, primaryChunk) >= expansionOptions.QualityThreshold)
                .OrderByDescending(c => EvaluateChunkQuality(c, primaryChunk))
                .Take(windowSize)
                .ToList();

            stopwatch.Stop();

            var result = new ContextExpansionResult
            {
                OriginalChunk = primaryChunk,
                ExpandedChunks = qualityFiltered,
                ExpansionBreakdown = expansionBreakdown,
                ExpansionQuality = CalculateExpansionQuality(qualityFiltered, primaryChunk),
                ExpansionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            };

            _logger.LogDebug("컨텍스트 확장 완료: {OriginalCount} → {ExpandedCount}개 청크, {ElapsedMs}ms",
                1, qualityFiltered.Count, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "컨텍스트 확장 중 오류 발생: {ChunkId}", primaryChunk.Id);
            throw;
        }
    }

    /// <summary>
    /// 확장 전략 추천
    /// </summary>
    public async Task<ExpansionStrategy> RecommendExpansionStrategyAsync(
        string query,
        DocumentChunk primaryChunk,
        CancellationToken cancellationToken = default)
    {
        var complexityAnalysis = await AnalyzeQueryComplexityAsync(query, cancellationToken);

        // 복잡도에 따른 전략 결정
        var strategyType = complexityAnalysis.OverallComplexity switch
        {
            var c when c <= 0.3 => ExpansionStrategyType.Conservative,
            var c when c <= 0.6 => ExpansionStrategyType.Balanced,
            var c when c <= 0.8 => ExpansionStrategyType.Aggressive,
            _ => ExpansionStrategyType.Adaptive
        };

        // 사용할 확장 방법 결정
        var methods = new List<ExpansionMethod> { ExpansionMethod.Sequential }; // 기본적으로 순차적 확장

        if (complexityAnalysis.SemanticComplexity > 0.5)
            methods.Add(ExpansionMethod.Semantic);

        if (complexityAnalysis.Components.NamedEntityCount > 0)
            methods.Add(ExpansionMethod.Entity);

        if (complexityAnalysis.ReasoningComplexity > 0.6)
            methods.Add(ExpansionMethod.Hierarchical);

        var strategy = new ExpansionStrategy
        {
            Type = strategyType,
            Methods = methods,
            Confidence = complexityAnalysis.AnalysisConfidence,
            Reasoning = $"쿼리 복잡도 {complexityAnalysis.OverallComplexity:F2}에 기반한 {strategyType} 전략",
            ExpectedOutcome = new ExpectedOutcome
            {
                ExpectedPrecisionGain = EstimatePrecisionGain(strategyType),
                ExpectedContextRichness = EstimateContextRichness(strategyType),
                ExpectedLatencyIncrease = EstimateLatencyIncrease(methods.Count),
                ExpectedUserSatisfaction = EstimateUserSatisfaction(strategyType, complexityAnalysis.OverallComplexity)
            }
        };

        return strategy;
    }

    /// <summary>
    /// Small-to-Big 성능 평가
    /// </summary>
    public async Task<SmallToBigPerformanceMetrics> EvaluatePerformanceAsync(
        IReadOnlyList<string> testQueries,
        IReadOnlyList<IReadOnlyList<string>> groundTruth,
        CancellationToken cancellationToken = default)
    {
        if (testQueries.Count != groundTruth.Count)
            throw new ArgumentException("테스트 쿼리와 정답 데이터의 개수가 일치하지 않습니다.");

        _logger.LogInformation("Small-to-Big 성능 평가 시작: {QueryCount}개 쿼리", testQueries.Count);

        var responseTimes = new List<double>();
        var precisionScores = new List<double>();
        var recallScores = new List<double>();
        var contextQualityScores = new List<double>();

        for (int i = 0; i < testQueries.Count; i++)
        {
            var query = testQueries[i];
            var truth = groundTruth[i];

            var stopwatch = Stopwatch.StartNew();
            var results = await SearchAsync(query, new SmallToBigOptions(), cancellationToken);
            stopwatch.Stop();

            responseTimes.Add(stopwatch.Elapsed.TotalMilliseconds);

            // 정밀도/재현율 계산
            var resultIds = results.Select(r => r.PrimaryChunk.Id).ToHashSet();
            var truthSet = truth.ToHashSet();

            var tp = resultIds.Intersect(truthSet).Count();
            var fp = resultIds.Except(truthSet).Count();
            var fn = truthSet.Except(resultIds).Count();

            var precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0.0;
            var recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0.0;

            precisionScores.Add(precision);
            recallScores.Add(recall);

            // 컨텍스트 품질 평가
            var avgContextQuality = results.Any() ? results.Average(r => r.ContextQuality) : 0.0;
            contextQualityScores.Add(avgContextQuality);

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        var metrics = new SmallToBigPerformanceMetrics
        {
            Precision = precisionScores.Average(),
            Recall = recallScores.Average(),
            F1Score = CalculateF1Score(precisionScores.Average(), recallScores.Average()),
            ContextQuality = contextQualityScores.Average(),
            AverageResponseTime = responseTimes.Average(),
            ExpansionEfficiency = CalculateExpansionEfficiency(contextQualityScores, responseTimes)
        };

        _logger.LogInformation("Small-to-Big 성능 평가 완료 - P: {Precision:F3}, R: {Recall:F3}, F1: {F1:F3}",
            metrics.Precision, metrics.Recall, metrics.F1Score);

        return metrics;
    }

    #region Private Methods

    private async Task<List<HybridSearchResult>> ExecutePreciseSearchAsync(
        string query,
        SmallToBigOptions options,
        CancellationToken cancellationToken)
    {
        // 하이브리드 검색으로 정밀한 매칭 수행
        var hybridOptions = new HybridSearchOptions
        {
            MaxResults = Math.Max(options.MaxResults * 2, 20), // 확장을 위해 더 많은 결과 요청
            FusionMethod = FusionMethod.RRF,
            VectorWeight = 0.7,
            SparseWeight = 0.3,
            EnableAutoStrategy = true
        };

        var hybridResults = await _hybridSearchService.SearchAsync(query, hybridOptions, cancellationToken);
        return hybridResults.ToList();
    }

    private async Task<SmallToBigResult> CreateSmallToBigResultAsync(
        string query,
        DocumentChunk primaryChunk,
        int windowSize,
        SmallToBigOptions options,
        QueryComplexityAnalysis complexityAnalysis,
        CancellationToken cancellationToken)
    {
        // 확장 전략 결정
        var strategy = await RecommendExpansionStrategyAsync(query, primaryChunk, cancellationToken);

        // 컨텍스트 확장
        var expansionOptions = new ContextExpansionOptions
        {
            EnableHierarchicalExpansion = options.EnableHierarchicalExpansion,
            EnableSequentialExpansion = options.EnableSequentialExpansion,
            EnableSemanticExpansion = options.EnableSemanticExpansion,
            QualityThreshold = options.ContextQualityThreshold,
            DeduplicationThreshold = options.DeduplicationThreshold
        };

        var expansionResult = await ExpandContextAsync(primaryChunk, windowSize, expansionOptions, cancellationToken);

        // Small-to-Big 결과 구성
        var result = new SmallToBigResult
        {
            PrimaryChunk = primaryChunk,
            ContextChunks = expansionResult.ExpandedChunks,
            RelevanceScore = CalculateOverallRelevance(primaryChunk, expansionResult.ExpandedChunks, query),
            WindowSize = windowSize,
            ExpansionReason = strategy.Reasoning,
            Strategy = strategy,
            Metadata = new SmallToBigMetadata
            {
                SearchTimeMs = expansionResult.ExpansionTimeMs,
                ContextQualityScore = expansionResult.ExpansionQuality,
                ExpansionEfficiency = CalculateExpansionEfficiency(expansionResult),
                MethodContributions = expansionResult.ExpansionBreakdown.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (double)kvp.Value / Math.Max(1, expansionResult.ExpandedChunks.Count)),
                ContextDiversity = CalculateContextDiversity(expansionResult.ExpandedChunks),
                InformationRedundancy = CalculateInformationRedundancy(expansionResult.ExpandedChunks)
            }
        };

        return result;
    }

    private ComplexityComponents AnalyzeComplexityComponents(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = tokens.Select(t => t.ToLowerInvariant()).Distinct().ToList();

        return new ComplexityComponents
        {
            TokenCount = tokens.Length,
            UniqueWordCount = uniqueWords.Count,
            AverageWordLength = tokens.Any() ? tokens.Average(t => t.Length) : 0,
            PunctuationDensity = (double)query.Count(char.IsPunctuation) / Math.Max(1, query.Length),
            TechnicalTermCount = CountTechnicalTerms(tokens),
            NamedEntityCount = CountNamedEntities(tokens),
            QuestionWordCount = CountQuestionWords(tokens),
            LogicalOperatorCount = CountLogicalOperators(tokens)
        };
    }

    private double CalculateLexicalComplexity(ComplexityComponents components)
    {
        var complexity = 0.0;

        // 어휘 다양성
        if (components.TokenCount > 0)
            complexity += (double)components.UniqueWordCount / components.TokenCount;

        // 평균 단어 길이 (정규화)
        complexity += Math.Min(1.0, components.AverageWordLength / 10.0);

        // 전문 용어 밀도
        if (components.TokenCount > 0)
            complexity += Math.Min(1.0, (double)components.TechnicalTermCount / components.TokenCount * 3);

        return Math.Min(1.0, complexity / 3.0);
    }

    private double CalculateSyntacticComplexity(ComplexityComponents components)
    {
        var complexity = 0.0;

        // 구두점 밀도
        complexity += Math.Min(1.0, components.PunctuationDensity * 10);

        // 토큰 수 (정규화)
        complexity += Math.Min(1.0, (double)components.TokenCount / 20.0);

        // 논리 연산자
        if (components.TokenCount > 0)
            complexity += Math.Min(1.0, (double)components.LogicalOperatorCount / components.TokenCount * 5);

        return Math.Min(1.0, complexity / 3.0);
    }

    private double CalculateSemanticComplexity(ComplexityComponents components)
    {
        var complexity = 0.0;

        // 개체명 밀도
        if (components.TokenCount > 0)
            complexity += Math.Min(1.0, (double)components.NamedEntityCount / components.TokenCount * 2);

        // 전문 용어 비율
        if (components.TokenCount > 0)
            complexity += Math.Min(1.0, (double)components.TechnicalTermCount / components.TokenCount * 3);

        // 어휘 다양성
        if (components.TokenCount > 0)
            complexity += (double)components.UniqueWordCount / components.TokenCount;

        return Math.Min(1.0, complexity / 3.0);
    }

    private double CalculateReasoningComplexity(ComplexityComponents components)
    {
        var complexity = 0.0;

        // 의문사 비율 (추론 필요 가능성)
        if (components.TokenCount > 0)
            complexity += Math.Min(1.0, (double)components.QuestionWordCount / components.TokenCount * 5);

        // 논리 연산자 (추론 복잡도)
        if (components.TokenCount > 0)
            complexity += Math.Min(1.0, (double)components.LogicalOperatorCount / components.TokenCount * 3);

        // 쿼리 길이 (긴 쿼리는 복잡한 추론 필요)
        complexity += Math.Min(1.0, (double)components.TokenCount / 15.0);

        return Math.Min(1.0, complexity / 3.0);
    }

    private int DetermineWindowSizeFromComplexity(double complexity, ComplexityComponents components)
    {
        var baseSize = complexity switch
        {
            var c when c <= 0.2 => 1,  // 매우 간단
            var c when c <= 0.4 => 2,  // 간단
            var c when c <= 0.6 => 4,  // 보통
            var c when c <= 0.8 => 6,  // 복잡
            _ => 8                      // 매우 복잡
        };

        // 조정 요인들
        if (components.TechnicalTermCount > 0) baseSize += 1;
        if (components.NamedEntityCount > 2) baseSize += 1;
        if (components.QuestionWordCount > 0) baseSize += 1;
        if (components.LogicalOperatorCount > 0) baseSize += 2;

        return Math.Min(10, Math.Max(1, baseSize));
    }

    private int CountTechnicalTerms(string[] tokens)
    {
        var technicalPatterns = new[]
        {
            @"API", @"HTTP", @"JSON", @"SQL", @"AI", @"ML", @"CNN", @"RNN", @"LSTM",
            @"알고리즘", @"데이터베이스", @"프레임워크", @"라이브러리", @"인터페이스"
        };

        return tokens.Count(token => technicalPatterns.Any(pattern =>
            Regex.IsMatch(token, pattern, RegexOptions.IgnoreCase)));
    }

    private int CountNamedEntities(string[] tokens)
    {
        // 간단한 개체명 감지 (대문자로 시작하는 단어)
        return tokens.Count(token => char.IsUpper(token.FirstOrDefault()) && token.Length > 1);
    }

    private int CountQuestionWords(string[] tokens)
    {
        var questionWords = new[] { "what", "who", "when", "where", "why", "how", "무엇", "누구", "언제", "어디", "왜", "어떻게" };
        return tokens.Count(token => questionWords.Contains(token.ToLowerInvariant()));
    }

    private int CountLogicalOperators(string[] tokens)
    {
        var operators = new[] { "and", "or", "not", "그리고", "또는", "아니", "AND", "OR", "NOT" };
        return tokens.Count(token => operators.Contains(token));
    }

    private double CalculateAnalysisConfidence(ComplexityComponents components)
    {
        var confidence = 0.8; // 기본 신뢰도

        if (components.TokenCount >= 3) confidence += 0.1;
        if (components.UniqueWordCount >= 2) confidence += 0.1;

        return Math.Min(1.0, confidence);
    }

    private string GenerateCacheKey(string query)
    {
        return $"complexity_{query.GetHashCode():X}";
    }

    private async Task BuildSequentialHierarchyAsync(List<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var hierarchy = new ChunkHierarchy
            {
                ChunkId = chunk.Id,
                HierarchyLevel = 0, // 기본적으로 문장 레벨
                Boundary = new ChunkBoundary
                {
                    StartPosition = i * 100, // 예시 위치
                    EndPosition = (i + 1) * 100 - 1,
                    Type = BoundaryType.Sentence
                },
                RecommendedWindowSize = 3
            };

            await _hierarchyRepository.SaveHierarchyAsync(hierarchy, cancellationToken);
        }
    }

    private async Task<List<ChunkRelationshipExtended>> AnalyzeSemanticRelationshipsAsync(
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        var relationships = new List<ChunkRelationshipExtended>();

        for (int i = 0; i < chunks.Count; i++)
        {
            for (int j = i + 1; j < chunks.Count; j++)
            {
                var similarity = CalculateSemanticSimilarity(chunks[i].Content, chunks[j].Content);

                if (similarity > 0.7) // 임계값
                {
                    relationships.Add(new ChunkRelationshipExtended
                    {
                        SourceChunkId = chunks[i].Id,
                        TargetChunkId = chunks[j].Id,
                        Type = RelationshipType.Semantic,
                        Strength = similarity,
                        Description = $"의미적 유사도: {similarity:F2}"
                    });
                }
            }
        }

        await Task.CompletedTask;
        return relationships;
    }

    private async Task BuildHierarchicalStructureAsync(List<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        // 문장 → 문단 → 섹션 계층 구조 구축
        // 현재는 간단한 구현, 실제로는 더 정교한 알고리즘 필요

        var paragraphChunks = new List<DocumentChunk>();

        // 3-5개 문장을 하나의 문단으로 그룹화
        for (int i = 0; i < chunks.Count; i += 4)
        {
            var paragraphChunks_subset = chunks.Skip(i).Take(4).ToList();
            if (paragraphChunks_subset.Any())
            {
                // 문단 레벨 계층 생성
                foreach (var chunk in paragraphChunks_subset)
                {
                    var hierarchy = await _hierarchyRepository.GetHierarchyAsync(chunk.Id, cancellationToken);
                    if (hierarchy != null)
                    {
                        hierarchy.HierarchyLevel = 1; // 문단 레벨
                        hierarchy.UpdatedAt = DateTime.UtcNow;
                        await _hierarchyRepository.SaveHierarchyAsync(hierarchy, cancellationToken);
                    }
                }
            }
        }
    }

    private double CalculateSemanticSimilarity(string content1, string content2)
    {
        // 간단한 단어 기반 유사도 계산
        var words1 = content1.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = content2.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private double CalculateHierarchyQuality(int hierarchyCount, int relationshipCount)
    {
        if (hierarchyCount == 0) return 0.0;

        var relationshipRatio = (double)relationshipCount / hierarchyCount;
        return Math.Min(1.0, relationshipRatio / 2.0); // 청크당 평균 2개 관계가 최적
    }

    private async Task<List<DocumentChunk>> ExpandHierarchicallyAsync(
        DocumentChunk primaryChunk,
        int maxChunks,
        CancellationToken cancellationToken)
    {
        var expandedChunks = new List<DocumentChunk>();

        try
        {
            var hierarchy = await _hierarchyRepository.GetHierarchyAsync(primaryChunk.Id, cancellationToken);
            if (hierarchy != null)
            {
                // 부모 청크 추가
                if (!string.IsNullOrEmpty(hierarchy.ParentChunkId))
                {
                    var parentChunk = await _hybridSearchService.GetChunkByIdAsync(hierarchy.ParentChunkId, cancellationToken);
                    if (parentChunk != null)
                        expandedChunks.Add(parentChunk);
                }

                // 자식 청크들 추가
                foreach (var childId in hierarchy.ChildChunkIds.Take(maxChunks - expandedChunks.Count))
                {
                    var childChunk = await _hybridSearchService.GetChunkByIdAsync(childId, cancellationToken);
                    if (childChunk != null)
                        expandedChunks.Add(childChunk);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "계층적 확장 중 오류: {ChunkId}", primaryChunk.Id);
        }

        return expandedChunks;
    }

    private async Task<List<DocumentChunk>> ExpandSequentiallyAsync(
        DocumentChunk primaryChunk,
        int maxChunks,
        CancellationToken cancellationToken)
    {
        var expandedChunks = new List<DocumentChunk>();

        try
        {
            // GetChunksByDocumentIdAsync는 현재 IHybridSearchService에 없으므로 단순 구현
            var allChunks = new List<DocumentChunk> { primaryChunk };
            var sortedChunks = allChunks.OrderBy(c => c.ChunkIndex).ToList();

            var primaryIndex = sortedChunks.FindIndex(c => c.Id == primaryChunk.Id);
            if (primaryIndex >= 0)
            {
                var windowSize = maxChunks / 2;
                var startIndex = Math.Max(0, primaryIndex - windowSize);
                var endIndex = Math.Min(sortedChunks.Count - 1, primaryIndex + windowSize);

                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (i != primaryIndex && expandedChunks.Count < maxChunks)
                    {
                        expandedChunks.Add(sortedChunks[i]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "순차적 확장 중 오류: {ChunkId}", primaryChunk.Id);
        }

        return expandedChunks;
    }

    private async Task<List<DocumentChunk>> ExpandSemanticallyAsync(
        DocumentChunk primaryChunk,
        int maxChunks,
        double similarityThreshold,
        CancellationToken cancellationToken)
    {
        var expandedChunks = new List<DocumentChunk>();

        try
        {
            var relationships = await _hierarchyRepository.GetRelationshipsAsync(
                primaryChunk.Id,
                new[] { RelationshipType.Semantic },
                cancellationToken);

            var semanticChunks = relationships
                .Where(r => r.Strength >= similarityThreshold)
                .OrderByDescending(r => r.Strength)
                .Take(maxChunks)
                .Select(r => r.TargetChunkId);

            foreach (var chunkId in semanticChunks)
            {
                var chunk = await _hybridSearchService.GetChunkByIdAsync(chunkId, cancellationToken);
                if (chunk != null)
                    expandedChunks.Add(chunk);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "의미적 확장 중 오류: {ChunkId}", primaryChunk.Id);
        }

        return expandedChunks;
    }

    private List<DocumentChunk> DeduplicateChunks(List<DocumentChunk> chunks, double threshold)
    {
        var deduplicated = new List<DocumentChunk>();

        foreach (var chunk in chunks)
        {
            var isDuplicate = deduplicated.Any(existing =>
                CalculateSemanticSimilarity(chunk.Content, existing.Content) >= threshold);

            if (!isDuplicate)
                deduplicated.Add(chunk);
        }

        return deduplicated;
    }

    private double EvaluateChunkQuality(DocumentChunk chunk, DocumentChunk primaryChunk)
    {
        var similarity = CalculateSemanticSimilarity(chunk.Content, primaryChunk.Content);
        var lengthScore = Math.Min(1.0, chunk.Content.Length / 500.0); // 적절한 길이 선호
        var freshnessScore = Math.Max(0.1, 1.0 - (DateTime.UtcNow - chunk.CreatedAt).TotalDays / 365.0);

        return (similarity * 0.5) + (lengthScore * 0.3) + (freshnessScore * 0.2);
    }

    private double CalculateExpansionQuality(List<DocumentChunk> expandedChunks, DocumentChunk primaryChunk)
    {
        if (!expandedChunks.Any()) return 0.0;

        var avgQuality = expandedChunks.Average(c => EvaluateChunkQuality(c, primaryChunk));
        var diversityScore = CalculateContextDiversity(expandedChunks);

        return (avgQuality * 0.7) + (diversityScore * 0.3);
    }

    private double CalculateOverallRelevance(DocumentChunk primaryChunk, List<DocumentChunk> contextChunks, string query)
    {
        var primaryRelevance = CalculateQueryRelevance(primaryChunk.Content, query);
        var contextRelevance = contextChunks.Any()
            ? contextChunks.Average(c => CalculateQueryRelevance(c.Content, query))
            : 0.0;

        return (primaryRelevance * 0.7) + (contextRelevance * 0.3);
    }

    private double CalculateQueryRelevance(string content, string query)
    {
        var contentWords = content.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var queryWords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var matchCount = queryWords.Count(qw => contentWords.Contains(qw));
        return (double)matchCount / Math.Max(1, queryWords.Length);
    }

    private double CalculateExpansionEfficiency(ContextExpansionResult result)
    {
        if (result.ExpansionTimeMs <= 0 || !result.ExpandedChunks.Any())
            return 0.0;

        return result.ExpansionQuality / (result.ExpansionTimeMs / 1000.0); // 품질/초
    }

    private double CalculateExpansionEfficiency(List<double> qualityScores, List<double> responseTimes)
    {
        if (!qualityScores.Any() || !responseTimes.Any())
            return 0.0;

        var avgQuality = qualityScores.Average();
        var avgTime = responseTimes.Average() / 1000.0; // 초 단위

        return avgQuality / Math.Max(0.1, avgTime);
    }

    private double CalculateContextDiversity(List<DocumentChunk> chunks)
    {
        if (chunks.Count <= 1) return 0.0;

        var totalSimilarity = 0.0;
        var comparisons = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            for (int j = i + 1; j < chunks.Count; j++)
            {
                totalSimilarity += CalculateSemanticSimilarity(chunks[i].Content, chunks[j].Content);
                comparisons++;
            }
        }

        var avgSimilarity = comparisons > 0 ? totalSimilarity / comparisons : 0.0;
        return 1.0 - avgSimilarity; // 유사도가 낮을수록 다양성이 높음
    }

    private double CalculateInformationRedundancy(List<DocumentChunk> chunks)
    {
        if (chunks.Count <= 1) return 0.0;

        var allWords = chunks.SelectMany(c => c.Content.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();

        var uniqueWords = allWords.Distinct().Count();
        var totalWords = allWords.Count;

        return totalWords > 0 ? 1.0 - ((double)uniqueWords / totalWords) : 0.0;
    }

    private double EstimatePrecisionGain(ExpansionStrategyType strategyType)
    {
        return strategyType switch
        {
            ExpansionStrategyType.Conservative => 0.02,
            ExpansionStrategyType.Balanced => 0.05,
            ExpansionStrategyType.Aggressive => 0.08,
            ExpansionStrategyType.Adaptive => 0.06,
            _ => 0.03
        };
    }

    private double EstimateContextRichness(ExpansionStrategyType strategyType)
    {
        return strategyType switch
        {
            ExpansionStrategyType.Conservative => 0.3,
            ExpansionStrategyType.Balanced => 0.6,
            ExpansionStrategyType.Aggressive => 0.9,
            ExpansionStrategyType.Adaptive => 0.7,
            _ => 0.5
        };
    }

    private double EstimateLatencyIncrease(int methodCount)
    {
        return methodCount * 0.1; // 방법당 10% 지연 증가 예상
    }

    private double EstimateUserSatisfaction(ExpansionStrategyType strategyType, double complexity)
    {
        var baseScore = strategyType switch
        {
            ExpansionStrategyType.Conservative => 0.7,
            ExpansionStrategyType.Balanced => 0.8,
            ExpansionStrategyType.Aggressive => 0.85,
            ExpansionStrategyType.Adaptive => 0.9,
            _ => 0.75
        };

        // 복잡한 쿼리일수록 확장된 컨텍스트의 가치 증가
        return Math.Min(1.0, baseScore + (complexity * 0.1));
    }

    private double CalculateF1Score(double precision, double recall)
    {
        return precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0.0;
    }

    #endregion
}