using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DomainHybridSearchResult = FluxIndex.Core.Domain.Models.HybridSearchResult;
using DomainHybridSearchOptions = FluxIndex.Core.Domain.Models.HybridSearchOptions;
using SearchStrategy = FluxIndex.Core.Domain.Models.SearchStrategy;

namespace FluxIndex.Core.Services;

/// <summary>
/// 하이브리드 검색 서비스 - 벡터 + 키워드 융합 검색
/// </summary>
public partial class HybridSearchService : IHybridSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IKeywordSearchService _keywordSearchService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorQuantizer? _quantizer;
    private readonly IDynamicFusionService? _dynamicFusion;
    private readonly ILogger<HybridSearchService> _logger;

    /// <summary>
    /// 기본 생성자 (양자화 없음)
    /// </summary>
    public HybridSearchService(
        IVectorStore vectorStore,
        IKeywordSearchService keywordSearchService,
        IEmbeddingService embeddingService,
        ILogger<HybridSearchService> logger)
        : this(vectorStore, keywordSearchService, embeddingService, null, null, logger)
    {
    }

    /// <summary>
    /// 양자화 지원 생성자
    /// </summary>
    public HybridSearchService(
        IVectorStore vectorStore,
        IKeywordSearchService keywordSearchService,
        IEmbeddingService embeddingService,
        IVectorQuantizer? quantizer,
        ILogger<HybridSearchService> logger)
        : this(vectorStore, keywordSearchService, embeddingService, quantizer, null, logger)
    {
    }

    /// <summary>
    /// Dynamic Alpha Tuning 지원 생성자 (권장)
    /// </summary>
    public HybridSearchService(
        IVectorStore vectorStore,
        IKeywordSearchService keywordSearchService,
        IEmbeddingService embeddingService,
        IVectorQuantizer? quantizer,
        IDynamicFusionService? dynamicFusion,
        ILogger<HybridSearchService> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _keywordSearchService = keywordSearchService ?? throw new ArgumentNullException(nameof(keywordSearchService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _quantizer = quantizer;
        _dynamicFusion = dynamicFusion;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 양자화 검색 지원 여부
    /// </summary>
    public bool SupportsQuantizedSearch => _quantizer != null && _vectorStore is IQuantizedVectorStore;

    /// <summary>
    /// 하이브리드 검색 실행
    /// </summary>
    public async Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        HybridSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<HybridSearchResult>();

        options ??= new HybridSearchOptions();

        if (_logger.IsEnabled(LogLevel.Information))
            LogHybridSearch16(_logger, query);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. 융합 결정 — 호출자가 지정한 값은 그대로, 비운 값만 DAT 또는 쿼리 휴리스틱이 채운다
            (options, var fusion) = await ResolveFusionAsync(query, options, cancellationToken);

            // 2. 병렬로 벡터 검색과 키워드 검색 실행
            var vectorTask = ExecuteVectorSearchAsync(query, options, cancellationToken);
            var sparseTask = ExecuteSparseSearchAsync(query, options, cancellationToken);

            await Task.WhenAll(vectorTask, sparseTask);

            var vectorResults = await vectorTask;
            var sparseResults = await sparseTask;

            LogHybridSearch13(_logger, vectorResults.Count, sparseResults.Count);

            // 3. 결과 융합
            var fusedResults = HybridFusion.Fuse(vectorResults, sparseResults, fusion, options.MaxResults, options.MinFusedScore);

            stopwatch.Stop();
            LogHybridSearch12(_logger, fusedResults.Count, stopwatch.ElapsedMilliseconds);

            return fusedResults;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogHybridSearch11(_logger, ex, query);
            throw;
        }
    }

    /// <summary>
    /// 배치 하이브리드 검색
    /// </summary>
    public async Task<IReadOnlyList<BatchHybridSearchResult>> SearchBatchAsync(
        IReadOnlyList<string> queries,
        HybridSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!queries.Any())
            return Array.Empty<BatchHybridSearchResult>();

        options ??= new HybridSearchOptions();

        LogHybridSearch10(_logger, queries.Count);

        var batchResults = new List<BatchHybridSearchResult>();

        // 배치 크기로 나누어 처리
        const int batchSize = 5;
        for (int i = 0; i < queries.Count; i += batchSize)
        {
            var batch = queries.Skip(i).Take(batchSize).ToList();
            var batchTasks = batch.Select(async query =>
            {
                var stopwatch = Stopwatch.StartNew();
                var results = await SearchAsync(query, options, cancellationToken);
                var strategy = LeavesFusionToService(options)
                    ? await RecommendSearchStrategyAsync(query, cancellationToken)
                    : new SearchStrategy { Type = SearchStrategyType.Balanced };

                stopwatch.Stop();

                return new BatchHybridSearchResult
                {
                    Query = query,
                    Results = results,
                    SearchTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                    Strategy = strategy
                };
            });

            var batchResult = await Task.WhenAll(batchTasks);
            batchResults.AddRange(batchResult);

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        LogHybridSearch9(_logger, batchResults.Count);
        return batchResults.AsReadOnly();
    }

    /// <summary>
    /// 검색 전략 추천
    /// </summary>
    public async Task<FluxIndex.Core.Domain.Models.SearchStrategy> RecommendSearchStrategyAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // 비동기 인터페이스 준수

        var characteristics = AnalyzeQueryCharacteristics(query);
        var strategy = DetermineOptimalStrategy(characteristics);

        if (_logger.IsEnabled(LogLevel.Information))
            LogHybridSearch8(_logger, query, strategy.Type);

        return strategy;
    }

    /// <summary>
    /// 융합 성능 평가
    /// </summary>
    public async Task<FusionPerformanceMetrics> EvaluateFusionPerformanceAsync(
        IReadOnlyList<string> testQueries,
        IReadOnlyList<IReadOnlyList<string>> groundTruth,
        CancellationToken cancellationToken = default)
    {
        if (testQueries.Count != groundTruth.Count)
            throw new ArgumentException("The number of test queries does not match the number of ground-truth entries.");

        LogHybridSearch7(_logger, testQueries.Count);

        var metrics = new List<QueryMetrics>();
        var searchTimes = new List<double>();
        var fusionMethodMetrics = new Dictionary<FusionMethod, List<double>>();

        for (int i = 0; i < testQueries.Count; i++)
        {
            var query = testQueries[i];
            var truth = groundTruth[i];

            var stopwatch = Stopwatch.StartNew();

            // 기본 옵션으로 검색
            var results = await SearchAsync(query, new HybridSearchOptions(), cancellationToken);

            stopwatch.Stop();
            searchTimes.Add(stopwatch.Elapsed.TotalMilliseconds);

            // 메트릭 계산
            var queryMetrics = CalculateQueryMetrics(results, truth);
            metrics.Add(queryMetrics);

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        // 전체 메트릭 집계
        var avgPrecision = metrics.Average(m => m.Precision);
        var avgRecall = metrics.Average(m => m.Recall);
        var avgF1 = metrics.Average(m => m.F1Score);
        var avgMRR = metrics.Average(m => m.MRR);
        var avgNDCG = metrics.Average(m => m.NDCG);

        var performanceMetrics = new FusionPerformanceMetrics
        {
            Precision = avgPrecision,
            Recall = avgRecall,
            F1Score = avgF1,
            MRR = avgMRR,
            NDCG = avgNDCG,
            AverageSearchTimeMs = searchTimes.Average(),
            ContributionRatio = (0.7, 0.3) // 기본 가중치 기반
        };

        if (_logger.IsEnabled(LogLevel.Information))
            LogHybridSearch6(_logger, avgPrecision, avgRecall, avgF1);

        return performanceMetrics;
    }

    #region Private Methods

    private async Task<IReadOnlyList<VectorSearchResult>> ExecuteVectorSearchAsync(
        string query,
        HybridSearchOptions options,
        CancellationToken cancellationToken)
    {
        // Keyword-only context: no vector leg to run. Skipped explicitly rather than through the failure handler below,
        // which exists for a vector backend that errors, not for one that was never configured.
        if (FluxIndex.Core.Application.Services.NoEmbeddingService.IsKeywordOnly(_embeddingService))
            return [];

        try
        {
            // 쿼리 임베딩 생성
            var embedding = await _embeddingService.GenerateQueryEmbeddingAsync(query, cancellationToken);

            IEnumerable<(Domain.Entities.DocumentChunk Chunk, float Score)>? searchResults = null;

            // 양자화 검색 사용 여부 확인
            if (options.UseQuantizedSearch && SupportsQuantizedSearch)
            {
                searchResults = await ExecuteQuantizedVectorSearchAsync(
                    embedding,
                    options,
                    cancellationToken);
            }

            // 양자화 검색 미사용 또는 지원하지 않는 경우 일반 검색
            if (searchResults == null)
            {
                var filters = options.EffectiveVectorFilters is { Count: > 0 } effective
                    ? new Dictionary<string, object>(effective)
                    : null;
                var vectorResults = await _vectorStore.SearchAsync(
                    embedding,
                    VectorLegSize(options),
                    (float)options.VectorOptions.MinScore,
                    filters,
                    cancellationToken);

                searchResults = vectorResults.Select(chunk => (chunk, chunk.Score ?? 0f));
            }

            // DocumentChunk 엔티티를 VectorSearchResult로 변환
            var results = searchResults.Select((item, index) => new VectorSearchResult
            {
                DocumentChunk = item.Chunk,
                Score = item.Score,
                Rank = index + 1,
                Distance = 1.0 - item.Score // 점수를 거리로 변환
            }).ToList();

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogHybridSearch5(_logger, ex);
            return Array.Empty<VectorSearchResult>();
        }
    }

    /// <summary>
    /// 양자화 검색 실행 (Two-Stage: 양자화 검색 → 리랭킹)
    /// </summary>
    private async Task<IEnumerable<(Domain.Entities.DocumentChunk Chunk, float Score)>?> ExecuteQuantizedVectorSearchAsync(
        float[] embedding,
        HybridSearchOptions options,
        CancellationToken cancellationToken)
    {
        if (_quantizer == null || _vectorStore is not IQuantizedVectorStore quantizedStore)
        {
            return null;
        }

        try
        {
            // 쿼리 벡터 양자화
            var quantizedQuery = await _quantizer.QuantizeAsync(embedding, cancellationToken);

            var legSize = VectorLegSize(options);
            if (_logger.IsEnabled(LogLevel.Information))
                LogHybridSearch4(_logger, legSize, options.QuantizedCandidateMultiplier);

            // Two-Stage 검색: 양자화 검색 후 원본 벡터로 리랭킹
            var results = await quantizedStore.SearchWithRerankAsync(
                embedding,
                quantizedQuery,
                legSize,
                options.QuantizedCandidateMultiplier,
                options.QuantizedMinScore,
                cancellationToken);

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogHybridSearch3(_logger, ex);
            return null;
        }
    }

    private async Task<IReadOnlyList<SparseSearchResult>> ExecuteSparseSearchAsync(
        string query,
        HybridSearchOptions options,
        CancellationToken cancellationToken)
    {
        var keywordOptions = ToKeywordSearchOptions(options);

        try
        {
            var keywordResults = await _keywordSearchService.SearchAsync(
                query, keywordOptions, cancellationToken);

            return keywordResults.Select(result => new SparseSearchResult
            {
                Chunk = result.Chunk,
                Score = result.Score,
                MatchedTerms = result.MatchedTerms,
                TermFrequencies = result.TermFrequencies
            }).ToArray();
        }
        catch (ArgumentException)
        {
            // A malformed filter is a caller error, not a backend outage. Degrading to vector-only
            // here would answer a scoped query with unscoped results and report success — the caller
            // would never learn their scope was discarded. Backend failures still degrade below;
            // this one has to reach the caller.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogHybridSearch2(_logger, ex);
            return Array.Empty<SparseSearchResult>();
        }
    }

    /// <summary>
    /// How many candidates a leg fetches: its own <c>MaxResults</c>, but never fewer than the fused list
    /// is asked to return. The legs default to 10, so a caller that set only
    /// <see cref="HybridSearchOptions.MaxResults"/> got two lists of 10 fused into at most 20 results —
    /// about 15 after duplicates — whatever it asked for.
    /// </summary>
    private static int VectorLegSize(HybridSearchOptions options)
        => Math.Max(options.VectorOptions.MaxResults, options.MaxResults);

    /// <summary>
    /// Adapts the fusion-facing sparse options onto the keyword service contract.
    /// The two records exist because <c>HybridSearchOptions.SparseOptions</c> is public shape while
    /// <see cref="IKeywordSearchService"/> is the backend contract; only the interface was unified.
    /// </summary>
    private static KeywordSearchOptions ToKeywordSearchOptions(HybridSearchOptions options)
    {
        var sparseOptions = options.SparseOptions ?? new SparseSearchOptions();
        var filters = options.EffectiveSparseFilters;

        return new KeywordSearchOptions
        {
            MaxResults = Math.Max(sparseOptions.MaxResults, options.MaxResults),
            MinScore = sparseOptions.MinScore,
            K1 = sparseOptions.K1,
            B = sparseOptions.B,
            EnableTermExpansion = sparseOptions.EnableTermExpansion,
            // Was dropped here while the vector leg honoured its own equivalent — an option the
            // contract declares and the adapter discards is the same silent-ignore class as the
            // missing filter below.
            EnablePhraseSearch = sparseOptions.EnablePhraseSearch,
            MetadataFilter = filters is { Count: > 0 } ? filters : null
        };
    }

    private static QueryCharacteristics AnalyzeQueryCharacteristics(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var length = tokens.Length;

        // 간단한 쿼리 유형 분석
        var queryType = DetermineQueryType(query);
        var complexity = CalculateComplexity(query, tokens);
        var containsEntities = ContainsNamedEntities(query);
        var containsTechnical = ContainsTechnicalTerms(tokens);

        return new QueryCharacteristics
        {
            Length = length,
            Type = queryType,
            Complexity = complexity,
            ContainsNamedEntities = containsEntities,
            ContainsTechnicalTerms = containsTechnical,
            Sentiment = SentimentPolarity.Neutral // 기본값
        };
    }

    private static SearchStrategy DetermineOptimalStrategy(QueryCharacteristics characteristics)
    {
        var strategyType = characteristics.Length switch
        {
            <= 2 => SearchStrategyType.SparseFirst, // 짧은 키워드
            <= 5 => SearchStrategyType.Balanced,    // 중간 길이
            _ => SearchStrategyType.VectorFirst     // 긴 자연어 쿼리
        };

        var fusionMethod = characteristics.ContainsTechnicalTerms
            ? FusionMethod.WeightedSum  // 전문용어는 가중합
            : FusionMethod.RRF;         // 일반적으로는 RRF

        var weights = strategyType switch
        {
            SearchStrategyType.VectorFirst => (0.8, 0.2),
            SearchStrategyType.SparseFirst => (0.3, 0.7),
            SearchStrategyType.Balanced => (0.6, 0.4),
            _ => (0.7, 0.3)
        };

        return new SearchStrategy
        {
            Type = strategyType,
            RecommendedFusion = fusionMethod,
            RecommendedWeights = weights,
            Confidence = 0.8,
            Reasoning = $"쿼리 길이: {characteristics.Length}, 유형: {characteristics.Type}",
            QueryCharacteristics = characteristics
        };
    }

    /// <summary>
    /// True when the caller left any of the fusion method or the two weights for the service to choose.
    /// </summary>
    private static bool LeavesFusionToService(HybridSearchOptions options) =>
        options.FusionMethod is null || options.VectorWeight is null || options.SparseWeight is null;

    /// <summary>
    /// Decides the fusion for one query. A value the caller set is used as is; only the unset ones are filled — by
    /// Dynamic Alpha Tuning when it is enabled and registered, otherwise by the query heuristic. The returned options
    /// carry the DAT quantization recommendation, if any.
    /// </summary>
    private async Task<(HybridSearchOptions Options, AppliedFusion Fusion)> ResolveFusionAsync(
        string query,
        HybridSearchOptions options,
        CancellationToken cancellationToken)
    {
        if (!LeavesFusionToService(options))
        {
            return (options, new AppliedFusion(
                options.FusionMethod!.Value, options.VectorWeight!.Value, options.SparseWeight!.Value, options.RrfK,
                FusionSelection.Caller));
        }

        if (options.EnableDynamicAlphaTuning && _dynamicFusion != null)
        {
            var datConfig = await _dynamicFusion.CalculateDynamicWeightsAsync(query, cancellationToken);
            if (_logger.IsEnabled(LogLevel.Information))
                LogHybridSearch15(_logger, datConfig.VectorWeight, datConfig.SparseWeight, datConfig.RecommendedFusion, datConfig.QueryType);

            return (
                options with { UseQuantizedSearch = datConfig.UseQuantizedSearch || options.UseQuantizedSearch },
                new AppliedFusion(
                    options.FusionMethod ?? datConfig.RecommendedFusion,
                    options.VectorWeight ?? datConfig.VectorWeight,
                    options.SparseWeight ?? datConfig.SparseWeight,
                    options.RrfK,
                    FusionSelection.DynamicAlphaTuning));
        }

        var strategy = await RecommendSearchStrategyAsync(query, cancellationToken);
        if (_logger.IsEnabled(LogLevel.Information))
            LogHybridSearch14(_logger, strategy.Type);

        return (options, new AppliedFusion(
            options.FusionMethod ?? strategy.RecommendedFusion,
            options.VectorWeight ?? strategy.RecommendedWeights.VectorWeight,
            options.SparseWeight ?? strategy.RecommendedWeights.SparseWeight,
            options.RrfK,
            FusionSelection.QueryHeuristic));
    }

    private static FluxIndex.Core.Domain.Models.QueryType DetermineQueryType(string query)
    {
        if (query.Contains('"'))
            return FluxIndex.Core.Domain.Models.QueryType.Phrase;
        if (query.Contains(" AND ") || query.Contains(" OR "))
            return FluxIndex.Core.Domain.Models.QueryType.Boolean;
        if (query.Split(' ').Length <= 3)
            return FluxIndex.Core.Domain.Models.QueryType.Keyword;
        return FluxIndex.Core.Domain.Models.QueryType.Natural;
    }

    private static double CalculateComplexity(string query, string[] tokens)
    {
        var complexity = 0.0;
        complexity += Math.Min(tokens.Length / 10.0, 1.0); // 길이 기준
        complexity += query.Count(c => char.IsPunctuation(c)) / 10.0; // 구두점 기준
        return Math.Min(complexity, 1.0);
    }

    private static bool ContainsNamedEntities(string query)
    {
        // 간단한 대문자 패턴 검사
        return query.Split(' ').Any(token => char.IsUpper(token.FirstOrDefault()));
    }

    private static readonly char[] TokenPunctuation = ['.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\''];

    private static bool ContainsTechnicalTerms(string[] tokens)
    {
        // Whole tokens only. A substring test read "email", "maintain" and "html" as the technical
        // terms AI and ML, which moved those queries to weighted-sum fusion — a different ranking and a
        // different score scale from the rank fusion every other query gets.
        var technicalTerms = new[] { "API", "HTTP", "JSON", "SQL", "AI", "ML" };
        return tokens.Any(token => technicalTerms.Any(term =>
            token.Trim(TokenPunctuation).Equals(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static QueryMetrics CalculateQueryMetrics(IReadOnlyList<HybridSearchResult> results, IReadOnlyList<string> groundTruth)
    {
        var resultIds = results.Select(r => r.Chunk.Id).ToHashSet();
        var truthSet = groundTruth.ToHashSet();

        var tp = resultIds.Intersect(truthSet).Count(); // True Positives
        var fp = resultIds.Except(truthSet).Count();    // False Positives
        var fn = truthSet.Except(resultIds).Count();    // False Negatives

        var precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0.0;
        var recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0.0;
        var f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0.0;

        // MRR 계산
        var mrr = 0.0;
        for (int i = 0; i < results.Count; i++)
        {
            if (truthSet.Contains(results[i].Chunk.Id))
            {
                mrr = 1.0 / (i + 1);
                break;
            }
        }

        return new QueryMetrics
        {
            Precision = precision,
            Recall = recall,
            F1Score = f1,
            MRR = mrr,
            NDCG = CalculateNDCG(results, truthSet)
        };
    }

    private static double CalculateNDCG(IReadOnlyList<HybridSearchResult> results, HashSet<string> groundTruth)
    {
        // 간단한 NDCG 계산
        double dcg = 0.0;
        double idcg = 0.0;

        for (int i = 0; i < Math.Min(results.Count, 10); i++)
        {
            var relevance = groundTruth.Contains(results[i].Chunk.Id) ? 1.0 : 0.0;
            dcg += relevance / Math.Log2(i + 2);
        }

        for (int i = 0; i < Math.Min(groundTruth.Count, 10); i++)
        {
            idcg += 1.0 / Math.Log2(i + 2);
        }

        return idcg > 0 ? dcg / idcg : 0.0;
    }

    #endregion

    /// <summary>
    /// ID로 청크 조회 (Small-to-Big 컨텍스트 확장용)
    /// </summary>
    public async Task<FluxIndex.Core.Domain.Entities.DocumentChunk?> GetChunkByIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            return null;

        try
        {
            // VectorStore를 통해 청크 조회
            var chunks = await _vectorStore.GetChunksByIdsAsync(new[] { chunkId }, cancellationToken);
            return chunks.FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                LogHybridSearch1(_logger, ex, chunkId);
            return null;
        }
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Hybrid search started: {Query}")]
    private static partial void LogHybridSearch16(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Information, Message = "DAT applied: Vector={VectorWeight:F2}, Sparse={SparseWeight:F2}, Fusion={Fusion}, Type={QueryType}")]
    private static partial void LogHybridSearch15(ILogger logger, double vectorWeight, double sparseWeight, FusionMethod fusion, FluxIndex.Core.Application.Interfaces.QueryType queryType);
    [LoggerMessage(Level = LogLevel.Information, Message = "Auto strategy selected: {Strategy}")]
    private static partial void LogHybridSearch14(ILogger logger, SearchStrategyType strategy);
    [LoggerMessage(Level = LogLevel.Information, Message = "Individual searches completed - vector: {VectorCount}, keyword: {SparseCount}")]
    private static partial void LogHybridSearch13(ILogger logger, int vectorCount, int sparseCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Hybrid search completed: {ResultCount} results, {ElapsedMs}ms")]
    private static partial void LogHybridSearch12(ILogger logger, int resultCount, long elapsedMs);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error during hybrid search: {Query}")]
    private static partial void LogHybridSearch11(ILogger logger, Exception exception, string query);
    [LoggerMessage(Level = LogLevel.Information, Message = "Batch hybrid search started: {QueryCount} queries")]
    private static partial void LogHybridSearch10(ILogger logger, int queryCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Batch hybrid search completed: {QueryCount} queries processed")]
    private static partial void LogHybridSearch9(ILogger logger, int queryCount);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Search strategy recommended: {Query} -> {Strategy}")]
    private static partial void LogHybridSearch8(ILogger logger, string query, SearchStrategyType strategy);
    [LoggerMessage(Level = LogLevel.Information, Message = "Fusion performance evaluation started: {QueryCount} queries")]
    private static partial void LogHybridSearch7(ILogger logger, int queryCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Fusion performance evaluation completed - P: {Precision:F3}, R: {Recall:F3}, F1: {F1:F3}")]
    private static partial void LogHybridSearch6(ILogger logger, double precision, double recall, double f1);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Vector search failed, returning empty results")]
    private static partial void LogHybridSearch5(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Quantized two-stage search running: TopK={TopK}, CandidateMultiplier={Multiplier}")]
    private static partial void LogHybridSearch4(ILogger logger, int topK, int multiplier);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Quantized search failed, falling back to regular search")]
    private static partial void LogHybridSearch3(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Keyword search failed, returning empty results")]
    private static partial void LogHybridSearch2(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Chunk retrieval failed: {ChunkId}")]
    private static partial void LogHybridSearch1(ILogger logger, Exception exception, string chunkId);

    #endregion
}

#region Helper Classes

/// <summary>
/// 쿼리별 메트릭
/// </summary>
internal sealed class QueryMetrics
{
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1Score { get; init; }
    public double MRR { get; init; }
    public double NDCG { get; init; }
}

#endregion
