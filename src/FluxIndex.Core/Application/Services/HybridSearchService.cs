using FluxIndex.Core.Interfaces;
using FluxIndex.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DomainHybridSearchResult = FluxIndex.Domain.Models.HybridSearchResult;
using DomainHybridSearchOptions = FluxIndex.Domain.Models.HybridSearchOptions;
using SearchStrategy = FluxIndex.Domain.Models.SearchStrategy;

namespace FluxIndex.Core.Services;

/// <summary>
/// 하이브리드 검색 서비스 - 벡터 + 키워드 융합 검색
/// </summary>
public class HybridSearchService : IHybridSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly ISparseRetriever _sparseRetriever;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchService(
        IVectorStore vectorStore,
        ISparseRetriever sparseRetriever,
        IEmbeddingService embeddingService,
        ILogger<HybridSearchService> logger)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _sparseRetriever = sparseRetriever ?? throw new ArgumentNullException(nameof(sparseRetriever));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        _logger.LogInformation("하이브리드 검색 시작: {Query}", query);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. 검색 전략 자동 선택 (옵션 활성화 시)
            if (options.EnableAutoStrategy)
            {
                var strategy = await RecommendSearchStrategyAsync(query, cancellationToken);
                options = ApplySearchStrategy(options, strategy);
                _logger.LogInformation("자동 전략 선택: {Strategy}", strategy.Type);
            }

            // 2. 병렬로 벡터 검색과 키워드 검색 실행
            var vectorTask = ExecuteVectorSearchAsync(query, options, cancellationToken);
            var sparseTask = ExecuteSparseSearchAsync(query, options, cancellationToken);

            await Task.WhenAll(vectorTask, sparseTask);

            var vectorResults = await vectorTask;
            var sparseResults = await sparseTask;

            _logger.LogInformation("개별 검색 완료 - 벡터: {VectorCount}, 키워드: {SparseCount}",
                vectorResults.Count, sparseResults.Count);

            // 3. 결과 융합
            var fusedResults = await FuseSearchResultsAsync(vectorResults, sparseResults, options, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("하이브리드 검색 완료: {ResultCount}개 결과, {ElapsedMs}ms",
                fusedResults.Count, stopwatch.ElapsedMilliseconds);

            return fusedResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "하이브리드 검색 중 오류 발생: {Query}", query);
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

        _logger.LogInformation("배치 하이브리드 검색 시작: {QueryCount}개 쿼리", queries.Count);

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
                var strategy = options.EnableAutoStrategy
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

        _logger.LogInformation("배치 하이브리드 검색 완료: {QueryCount}개 쿼리 처리", batchResults.Count);
        return batchResults.AsReadOnly();
    }

    /// <summary>
    /// 검색 전략 추천
    /// </summary>
    public async Task<FluxIndex.Domain.Models.SearchStrategy> RecommendSearchStrategyAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // 비동기 인터페이스 준수

        var characteristics = AnalyzeQueryCharacteristics(query);
        var strategy = DetermineOptimalStrategy(characteristics);

        _logger.LogDebug("검색 전략 추천: {Query} → {Strategy}", query, strategy.Type);

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
            throw new ArgumentException("테스트 쿼리와 정답 데이터의 개수가 일치하지 않습니다.");

        _logger.LogInformation("융합 성능 평가 시작: {QueryCount}개 쿼리", testQueries.Count);

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

        _logger.LogInformation("융합 성능 평가 완료 - P: {Precision:F3}, R: {Recall:F3}, F1: {F1:F3}",
            avgPrecision, avgRecall, avgF1);

        return performanceMetrics;
    }

    #region Private Methods

    private async Task<IReadOnlyList<VectorSearchResult>> ExecuteVectorSearchAsync(
        string query,
        HybridSearchOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            // 쿼리 임베딩 생성
            var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

            // 벡터 검색 실행
            var vectorResults = await _vectorStore.SearchAsync(
                embedding,
                options.VectorOptions.MaxResults,
                (float)options.VectorOptions.MinScore,
                cancellationToken);

            // DocumentChunk 엔티티를 VectorSearchResult로 변환
            var results = vectorResults.Select((chunk, index) => new VectorSearchResult
            {
                DocumentChunk = new FluxIndex.Domain.Models.DocumentChunk
                {
                    Id = chunk.Id,
                    DocumentId = chunk.DocumentId,
                    Content = chunk.Content,
                    ChunkIndex = chunk.ChunkIndex,
                    TotalChunks = chunk.TotalChunks,
                    Embedding = chunk.Embedding,
                    Score = chunk.Score,
                    TokenCount = chunk.TokenCount,
                    Metadata = chunk.Metadata,
                    CreatedAt = chunk.CreatedAt
                },
                Score = chunk.Score,
                Rank = index + 1,
                Distance = 1.0 - chunk.Score // 점수를 거리로 변환
            }).ToList();

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "벡터 검색 실패, 빈 결과 반환");
            return Array.Empty<VectorSearchResult>();
        }
    }

    private async Task<IReadOnlyList<SparseSearchResult>> ExecuteSparseSearchAsync(
        string query,
        HybridSearchOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _sparseRetriever.SearchAsync(query, options.SparseOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "키워드 검색 실패, 빈 결과 반환");
            return Array.Empty<SparseSearchResult>();
        }
    }

    private async Task<IReadOnlyList<HybridSearchResult>> FuseSearchResultsAsync(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        HybridSearchOptions options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // 비동기 인터페이스 준수

        return options.FusionMethod switch
        {
            FusionMethod.RRF => FuseWithRRF(vectorResults, sparseResults, options),
            FusionMethod.WeightedSum => FuseWithWeightedSum(vectorResults, sparseResults, options),
            FusionMethod.Product => FuseWithProduct(vectorResults, sparseResults, options),
            FusionMethod.Maximum => FuseWithMaximum(vectorResults, sparseResults, options),
            FusionMethod.HarmonicMean => FuseWithHarmonicMean(vectorResults, sparseResults, options),
            _ => FuseWithRRF(vectorResults, sparseResults, options)
        };
    }

    private IReadOnlyList<HybridSearchResult> FuseWithRRF(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        HybridSearchOptions options)
    {
        var fusedResults = new Dictionary<string, HybridSearchResult>();
        var k = options.RrfK;

        // 벡터 결과 처리
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (k + i + 1); // RRF 점수

            fusedResults[result.DocumentChunk.Id] = new HybridSearchResult
            {
                Chunk = result.DocumentChunk,
                FusedScore = rrfScore * options.VectorWeight,
                VectorScore = result.Score,
                SparseScore = 0.0,
                VectorRank = i + 1,
                SparseRank = int.MaxValue,
                FusionMethod = FusionMethod.RRF,
                Source = SearchSource.Vector,
                MatchedTerms = Array.Empty<string>()
            };
        }

        // 키워드 결과 처리
        for (int i = 0; i < sparseResults.Count; i++)
        {
            var result = sparseResults[i];
            var rrfScore = 1.0 / (k + i + 1); // RRF 점수

            if (fusedResults.TryGetValue(result.Chunk.Id, out var existing))
            {
                // 기존 결과에 융합
                fusedResults[result.Chunk.Id] = existing with
                {
                    FusedScore = existing.FusedScore + (rrfScore * options.SparseWeight),
                    SparseScore = result.Score,
                    SparseRank = i + 1,
                    Source = SearchSource.Both,
                    MatchedTerms = result.MatchedTerms
                };
            }
            else
            {
                // 새 결과 생성
                fusedResults[result.Chunk.Id] = new HybridSearchResult
                {
                    Chunk = result.Chunk,
                    FusedScore = rrfScore * options.SparseWeight,
                    VectorScore = 0.0,
                    SparseScore = result.Score,
                    VectorRank = int.MaxValue,
                    SparseRank = i + 1,
                    FusionMethod = FusionMethod.RRF,
                    Source = SearchSource.Sparse,
                    MatchedTerms = result.MatchedTerms
                };
            }
        }

        // 최종 점수로 정렬 및 상위 결과 반환
        var sortedResults = fusedResults.Values
            .Where(r => r.FusedScore >= options.MinFusedScore)
            .OrderByDescending(r => r.FusedScore)
            .Take(options.MaxResults)
            .Select((result, index) => result with { FusedRank = index + 1 })
            .ToList();

        return sortedResults.AsReadOnly();
    }

    private IReadOnlyList<HybridSearchResult> FuseWithWeightedSum(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        HybridSearchOptions options)
    {
        var fusedResults = new Dictionary<string, HybridSearchResult>();

        // 점수 정규화를 위한 최대값 계산
        var maxVectorScore = vectorResults.Any() ? vectorResults.Max(r => r.Score) : 1.0;
        var maxSparseScore = sparseResults.Any() ? sparseResults.Max(r => r.Score) : 1.0;

        // 벡터 결과 처리
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var normalizedScore = result.Score / maxVectorScore;
            var weightedScore = normalizedScore * options.VectorWeight;

            fusedResults[result.DocumentChunk.Id] = new HybridSearchResult
            {
                Chunk = result.DocumentChunk,
                FusedScore = weightedScore,
                VectorScore = result.Score,
                SparseScore = 0.0,
                VectorRank = i + 1,
                SparseRank = int.MaxValue,
                FusionMethod = FusionMethod.WeightedSum,
                Source = SearchSource.Vector,
                MatchedTerms = Array.Empty<string>()
            };
        }

        // 키워드 결과 처리
        for (int i = 0; i < sparseResults.Count; i++)
        {
            var result = sparseResults[i];
            var normalizedScore = result.Score / maxSparseScore;
            var weightedScore = normalizedScore * options.SparseWeight;

            if (fusedResults.TryGetValue(result.Chunk.Id, out var existing))
            {
                fusedResults[result.Chunk.Id] = existing with
                {
                    FusedScore = existing.FusedScore + weightedScore,
                    SparseScore = result.Score,
                    SparseRank = i + 1,
                    Source = SearchSource.Both,
                    MatchedTerms = result.MatchedTerms
                };
            }
            else
            {
                fusedResults[result.Chunk.Id] = new HybridSearchResult
                {
                    Chunk = result.Chunk,
                    FusedScore = weightedScore,
                    VectorScore = 0.0,
                    SparseScore = result.Score,
                    VectorRank = int.MaxValue,
                    SparseRank = i + 1,
                    FusionMethod = FusionMethod.WeightedSum,
                    Source = SearchSource.Sparse,
                    MatchedTerms = result.MatchedTerms
                };
            }
        }

        return OrderAndLimitResults(fusedResults.Values, options);
    }

    private IReadOnlyList<HybridSearchResult> FuseWithProduct(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        HybridSearchOptions options)
    {
        // 곱셈 융합은 양쪽 모두에서 매칭된 결과만 유지
        var fusedResults = new List<HybridSearchResult>();
        var vectorDict = vectorResults.ToDictionary(r => r.DocumentChunk.Id, r => r);
        var sparseDict = sparseResults.ToDictionary(r => r.Chunk.Id, r => r);

        foreach (var chunkId in vectorDict.Keys.Intersect(sparseDict.Keys))
        {
            var vectorResult = vectorDict[chunkId];
            var sparseResult = sparseDict[chunkId];

            var vectorNormalized = vectorResult.Score;
            var sparseNormalized = sparseResult.Score;
            var productScore = Math.Sqrt(vectorNormalized * sparseNormalized); // 기하평균

            fusedResults.Add(new HybridSearchResult
            {
                Chunk = vectorResult.DocumentChunk,
                FusedScore = productScore,
                VectorScore = vectorResult.Score,
                SparseScore = sparseResult.Score,
                VectorRank = vectorResults.ToList().IndexOf(vectorResult) + 1,
                SparseRank = sparseResults.ToList().IndexOf(sparseResult) + 1,
                FusionMethod = FusionMethod.Product,
                Source = SearchSource.Both,
                MatchedTerms = sparseResult.MatchedTerms
            });
        }

        return OrderAndLimitResults(fusedResults, options);
    }

    private IReadOnlyList<HybridSearchResult> FuseWithMaximum(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        HybridSearchOptions options)
    {
        var fusedResults = new Dictionary<string, HybridSearchResult>();

        // 모든 결과를 통합하고 최대 점수 선택
        var allChunkIds = vectorResults.Select(r => r.DocumentChunk.Id)
            .Concat(sparseResults.Select(r => r.Chunk.Id))
            .Distinct();

        foreach (var chunkId in allChunkIds)
        {
            var vectorResult = vectorResults.FirstOrDefault(r => r.DocumentChunk.Id == chunkId);
            var sparseResult = sparseResults.FirstOrDefault(r => r.Chunk.Id == chunkId);

            var vectorScore = vectorResult?.Score ?? 0.0;
            var sparseScore = sparseResult?.Score ?? 0.0;
            var maxScore = Math.Max(vectorScore * options.VectorWeight, sparseScore * options.SparseWeight);

            var chunk = vectorResult?.DocumentChunk ?? sparseResult!.Chunk;
            var source = (vectorResult != null && sparseResult != null) ? SearchSource.Both :
                         (vectorResult != null) ? SearchSource.Vector : SearchSource.Sparse;

            fusedResults[chunkId] = new HybridSearchResult
            {
                Chunk = chunk,
                FusedScore = maxScore,
                VectorScore = vectorScore,
                SparseScore = sparseScore,
                VectorRank = vectorResult != null ? vectorResults.ToList().IndexOf(vectorResult) + 1 : int.MaxValue,
                SparseRank = sparseResult != null ? sparseResults.ToList().IndexOf(sparseResult) + 1 : int.MaxValue,
                FusionMethod = FusionMethod.Maximum,
                Source = source,
                MatchedTerms = sparseResult?.MatchedTerms ?? Array.Empty<string>()
            };
        }

        return OrderAndLimitResults(fusedResults.Values, options);
    }

    private IReadOnlyList<HybridSearchResult> FuseWithHarmonicMean(
        IReadOnlyList<VectorSearchResult> vectorResults,
        IReadOnlyList<SparseSearchResult> sparseResults,
        HybridSearchOptions options)
    {
        // 조화평균은 양쪽 모두에서 매칭된 결과만 유지
        var fusedResults = new List<HybridSearchResult>();
        var vectorDict = vectorResults.ToDictionary(r => r.DocumentChunk.Id, r => r);
        var sparseDict = sparseResults.ToDictionary(r => r.Chunk.Id, r => r);

        foreach (var chunkId in vectorDict.Keys.Intersect(sparseDict.Keys))
        {
            var vectorResult = vectorDict[chunkId];
            var sparseResult = sparseDict[chunkId];

            var vectorWeighted = vectorResult.Score * options.VectorWeight;
            var sparseWeighted = sparseResult.Score * options.SparseWeight;

            // 조화평균 계산
            var harmonicMean = (vectorWeighted + sparseWeighted) > 0
                ? 2 * vectorWeighted * sparseWeighted / (vectorWeighted + sparseWeighted)
                : 0.0;

            fusedResults.Add(new HybridSearchResult
            {
                Chunk = vectorResult.DocumentChunk,
                FusedScore = harmonicMean,
                VectorScore = vectorResult.Score,
                SparseScore = sparseResult.Score,
                VectorRank = vectorResults.ToList().IndexOf(vectorResult) + 1,
                SparseRank = sparseResults.ToList().IndexOf(sparseResult) + 1,
                FusionMethod = FusionMethod.HarmonicMean,
                Source = SearchSource.Both,
                MatchedTerms = sparseResult.MatchedTerms
            });
        }

        return OrderAndLimitResults(fusedResults, options);
    }

    private IReadOnlyList<HybridSearchResult> OrderAndLimitResults(
        IEnumerable<HybridSearchResult> results,
        HybridSearchOptions options)
    {
        return results
            .Where(r => r.FusedScore >= options.MinFusedScore)
            .OrderByDescending(r => r.FusedScore)
            .Take(options.MaxResults)
            .Select((result, index) => result with { FusedRank = index + 1 })
            .ToList()
            .AsReadOnly();
    }

    private QueryCharacteristics AnalyzeQueryCharacteristics(string query)
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

    private SearchStrategy DetermineOptimalStrategy(QueryCharacteristics characteristics)
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

    private HybridSearchOptions ApplySearchStrategy(HybridSearchOptions options, SearchStrategy strategy)
    {
        return options with
        {
            FusionMethod = strategy.RecommendedFusion,
            VectorWeight = strategy.RecommendedWeights.VectorWeight,
            SparseWeight = strategy.RecommendedWeights.SparseWeight
        };
    }

    private FluxIndex.Domain.Models.QueryType DetermineQueryType(string query)
    {
        if (query.Contains('"'))
            return FluxIndex.Domain.Models.QueryType.Phrase;
        if (query.Contains(" AND ") || query.Contains(" OR "))
            return FluxIndex.Domain.Models.QueryType.Boolean;
        if (query.Split(' ').Length <= 3)
            return FluxIndex.Domain.Models.QueryType.Keyword;
        return FluxIndex.Domain.Models.QueryType.Natural;
    }

    private double CalculateComplexity(string query, string[] tokens)
    {
        var complexity = 0.0;
        complexity += Math.Min(tokens.Length / 10.0, 1.0); // 길이 기준
        complexity += query.Count(c => char.IsPunctuation(c)) / 10.0; // 구두점 기준
        return Math.Min(complexity, 1.0);
    }

    private bool ContainsNamedEntities(string query)
    {
        // 간단한 대문자 패턴 검사
        return query.Split(' ').Any(token => char.IsUpper(token.FirstOrDefault()));
    }

    private bool ContainsTechnicalTerms(string[] tokens)
    {
        // 간단한 기술 용어 패턴 검사
        var technicalPatterns = new[] { "API", "HTTP", "JSON", "SQL", "AI", "ML" };
        return tokens.Any(token => technicalPatterns.Any(pattern =>
            token.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    private QueryMetrics CalculateQueryMetrics(IReadOnlyList<HybridSearchResult> results, IReadOnlyList<string> groundTruth)
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

    private double CalculateNDCG(IReadOnlyList<HybridSearchResult> results, HashSet<string> groundTruth)
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
    public async Task<FluxIndex.Domain.Entities.DocumentChunk?> GetChunkByIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            return null;

        try
        {
            // VectorStore를 통해 청크 조회
            var chunks = await _vectorStore.GetChunksByIdsAsync(new[] { chunkId }, cancellationToken);
            return chunks.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "청크 조회 실패: {ChunkId}", chunkId);
            return null;
        }
    }
}

#region Helper Classes

/// <summary>
/// 쿼리별 메트릭
/// </summary>
internal class QueryMetrics
{
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1Score { get; init; }
    public double MRR { get; init; }
    public double NDCG { get; init; }
}

#endregion