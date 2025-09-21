using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// RAG 시스템 품질 평가 서비스
/// </summary>
public class RAGEvaluationService : IRAGEvaluationService
{
    private readonly ITextCompletionService _textCompletionService;
    private readonly ILogger<RAGEvaluationService> _logger;

    public RAGEvaluationService(
        ITextCompletionService textCompletionService,
        ILogger<RAGEvaluationService> logger)
    {
        _textCompletionService = textCompletionService ?? throw new ArgumentNullException(nameof(textCompletionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 단일 쿼리에 대한 RAG 시스템 평가
    /// </summary>
    public async Task<RAGEvaluationResult> EvaluateQueryAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        string generatedAnswer,
        GoldenDatasetItem goldenItem,
        EvaluationConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        var config = configuration ?? new EvaluationConfiguration();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("평가 시작: Query={Query}, ChunkCount={ChunkCount}",
                query, retrievedChunks.Count());

            var result = new RAGEvaluationResult
            {
                QueryId = goldenItem.Id,
                Query = query,
                EvaluatedAt = startTime,
                RetrievedDocumentsCount = retrievedChunks.Count(),
                RelevantDocumentsCount = goldenItem.RelevantChunkIds.Count,
                TotalRelevantDocuments = goldenItem.RelevantChunkIds.Count,
                SearchMethod = "hybrid" // TODO: 동적으로 설정
            };

            // 1. 검색 품질 지표 계산
            var retrievalMetrics = await CalculateRetrievalMetricsAsync(
                retrievedChunks,
                goldenItem.RelevantChunkIds,
                goldenItem.RelevantChunkIds.Count,
                cancellationToken);

            result.Precision = retrievalMetrics.GetValueOrDefault("precision", 0.0);
            result.Recall = retrievalMetrics.GetValueOrDefault("recall", 0.0);
            result.F1Score = retrievalMetrics.GetValueOrDefault("f1_score", 0.0);
            result.MRR = retrievalMetrics.GetValueOrDefault("mrr", 0.0);
            result.NDCG = retrievalMetrics.GetValueOrDefault("ndcg", 0.0);
            result.HitRate = retrievalMetrics.GetValueOrDefault("hit_rate", 0.0);

            // 2. LLM 기반 답변 품질 평가
            if (config.EnableFaithfulnessEvaluation || config.EnableAnswerRelevancyEvaluation)
            {
                var answerMetrics = await EvaluateAnswerQualityAsync(
                    query,
                    generatedAnswer,
                    retrievedChunks,
                    goldenItem.ExpectedAnswer,
                    cancellationToken);

                result.Faithfulness = answerMetrics.GetValueOrDefault("faithfulness", 0.0);
                result.AnswerRelevancy = answerMetrics.GetValueOrDefault("answer_relevancy", 0.0);
            }

            // 3. 컨텍스트 품질 평가
            if (config.EnableContextEvaluation)
            {
                var contextMetrics = await EvaluateContextQualityAsync(
                    query,
                    retrievedChunks,
                    goldenItem.RelevantChunkIds,
                    cancellationToken);

                result.ContextRelevancy = contextMetrics.GetValueOrDefault("context_relevancy", 0.0);
                result.ContextPrecision = contextMetrics.GetValueOrDefault("context_precision", 0.0);
                result.ContextRecall = contextMetrics.GetValueOrDefault("context_recall", 0.0);
            }

            result.Duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("평가 완료: Query={Query}, Precision={Precision:F3}, Recall={Recall:F3}, F1={F1:F3}",
                query, result.Precision, result.Recall, result.F1Score);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "평가 중 오류 발생: Query={Query}", query);
            throw;
        }
    }

    /// <summary>
    /// 골든 데이터셋에 대한 배치 평가
    /// </summary>
    public async Task<BatchEvaluationResult> EvaluateBatchAsync(
        IEnumerable<GoldenDatasetItem> goldenDataset,
        EvaluationConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        var config = configuration ?? new EvaluationConfiguration();
        var batchResult = new BatchEvaluationResult
        {
            BatchId = Guid.NewGuid().ToString(),
            StartedAt = DateTime.UtcNow,
            Configuration = System.Text.Json.JsonSerializer.Serialize(config)
        };

        try
        {
            var dataset = goldenDataset.ToList();
            batchResult.TotalQueries = dataset.Count;

            _logger.LogInformation("배치 평가 시작: TotalQueries={TotalQueries}", batchResult.TotalQueries);

            var results = new List<RAGEvaluationResult>();
            var failedCount = 0;

            foreach (var item in dataset)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // TODO: 실제 검색 및 답변 생성 로직 호출
                    // 현재는 모의 데이터 사용
                    var mockRetrievedChunks = CreateMockRetrievedChunks(item);
                    var mockGeneratedAnswer = $"Mock answer for: {item.Query}";

                    var result = await EvaluateQueryAsync(
                        item.Query,
                        mockRetrievedChunks,
                        mockGeneratedAnswer,
                        item,
                        config,
                        cancellationToken);

                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "개별 쿼리 평가 실패: QueryId={QueryId}", item.Id);
                    failedCount++;
                }
            }

            batchResult.Results = results;
            batchResult.SuccessfulQueries = results.Count;
            batchResult.FailedQueries = failedCount;
            batchResult.SuccessRate = batchResult.TotalQueries > 0
                ? (double)batchResult.SuccessfulQueries / batchResult.TotalQueries
                : 0.0;

            // 집계 지표 계산
            if (results.Any())
            {
                batchResult.AveragePrecision = results.Average(r => r.Precision);
                batchResult.AverageRecall = results.Average(r => r.Recall);
                batchResult.AverageF1Score = results.Average(r => r.F1Score);
                batchResult.AverageMRR = results.Average(r => r.MRR);
                batchResult.AverageNDCG = results.Average(r => r.NDCG);
                batchResult.AverageHitRate = results.Average(r => r.HitRate);
                batchResult.AverageFaithfulness = results.Average(r => r.Faithfulness);
                batchResult.AverageAnswerRelevancy = results.Average(r => r.AnswerRelevancy);
                batchResult.AverageContextRelevancy = results.Average(r => r.ContextRelevancy);
                batchResult.AverageQueryDuration = results.Average(r => r.Duration.TotalMilliseconds);
            }

            batchResult.CompletedAt = DateTime.UtcNow;
            batchResult.TotalDuration = batchResult.CompletedAt - batchResult.StartedAt;

            _logger.LogInformation("배치 평가 완료: SuccessfulQueries={SuccessfulQueries}, FailedQueries={FailedQueries}, AvgPrecision={AvgPrecision:F3}",
                batchResult.SuccessfulQueries, batchResult.FailedQueries, batchResult.AveragePrecision);

            return batchResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "배치 평가 중 오류 발생");
            throw;
        }
    }

    /// <summary>
    /// 검색 품질 지표 계산 (Precision, Recall, F1, MRR, NDCG)
    /// </summary>
    public async Task<Dictionary<string, double>> CalculateRetrievalMetricsAsync(
        IEnumerable<DocumentChunk> retrievedChunks,
        IEnumerable<string> relevantChunkIds,
        int totalRelevantCount,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // 비동기 시뮬레이션

        var retrievedIds = retrievedChunks.Select(c => c.Id).ToHashSet();
        var relevantIds = relevantChunkIds.ToHashSet();

        // True Positives: 검색된 것 중 관련성 있는 것
        var truePositives = retrievedIds.Intersect(relevantIds).Count();

        // Precision = TP / (TP + FP) = TP / Retrieved
        var precision = retrievedIds.Count > 0 ? (double)truePositives / retrievedIds.Count : 0.0;

        // Recall = TP / (TP + FN) = TP / Relevant
        var recall = totalRelevantCount > 0 ? (double)truePositives / totalRelevantCount : 0.0;

        // F1 Score = 2 * (Precision * Recall) / (Precision + Recall)
        var f1Score = (precision + recall) > 0 ? 2 * (precision * recall) / (precision + recall) : 0.0;

        // Mean Reciprocal Rank (MRR)
        var mrr = CalculateMRR(retrievedChunks.Select(c => c.Id), relevantIds);

        // Normalized Discounted Cumulative Gain (NDCG)
        var ndcg = CalculateNDCG(retrievedChunks.Select(c => c.Id), relevantIds);

        // Hit Rate (at least one relevant document in top-K)
        var hitRate = truePositives > 0 ? 1.0 : 0.0;

        return new Dictionary<string, double>
        {
            ["precision"] = precision,
            ["recall"] = recall,
            ["f1_score"] = f1Score,
            ["mrr"] = mrr,
            ["ndcg"] = ndcg,
            ["hit_rate"] = hitRate
        };
    }

    /// <summary>
    /// LLM 기반 답변 품질 평가 (Faithfulness, Answer Relevancy)
    /// </summary>
    public async Task<Dictionary<string, double>> EvaluateAnswerQualityAsync(
        string query,
        string generatedAnswer,
        IEnumerable<DocumentChunk> sourceChunks,
        string expectedAnswer,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, double>();

        try
        {
            // Faithfulness 평가: 답변이 소스 문서에 근거하는 정도
            var faithfulnessPrompt = $@"
다음 질문, 답변, 그리고 소스 문서들을 평가해주세요.

질문: {query}

생성된 답변: {generatedAnswer}

소스 문서들:
{string.Join("\n\n", sourceChunks.Select((c, i) => $"문서 {i + 1}: {c.Content}"))}

답변이 소스 문서들에 근거하여 작성되었는지 평가하고, 0.0에서 1.0 사이의 점수를 제공해주세요.
1.0 = 완전히 소스에 근거함, 0.0 = 전혀 근거하지 않음

점수만 숫자로 응답해주세요.";

            var faithfulnessResponse = await _textCompletionService.CompleteAsync(faithfulnessPrompt, cancellationToken);
            if (double.TryParse(faithfulnessResponse.Trim(), out var faithfulness))
            {
                results["faithfulness"] = Math.Clamp(faithfulness, 0.0, 1.0);
            }

            // Answer Relevancy 평가: 답변이 질문과 관련된 정도
            var relevancyPrompt = $@"
다음 질문과 답변을 평가해주세요.

질문: {query}

생성된 답변: {generatedAnswer}

기대 답변: {expectedAnswer}

답변이 질문과 얼마나 관련이 있는지 평가하고, 0.0에서 1.0 사이의 점수를 제공해주세요.
1.0 = 완전히 관련됨, 0.0 = 전혀 관련 없음

점수만 숫자로 응답해주세요.";

            var relevancyResponse = await _textCompletionService.CompleteAsync(relevancyPrompt, cancellationToken);
            if (double.TryParse(relevancyResponse.Trim(), out var relevancy))
            {
                results["answer_relevancy"] = Math.Clamp(relevancy, 0.0, 1.0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM 기반 답변 품질 평가 중 오류 발생");
            // 기본값 설정
            results["faithfulness"] = 0.5;
            results["answer_relevancy"] = 0.5;
        }

        return results;
    }

    /// <summary>
    /// 컨텍스트 품질 평가 (Context Relevancy, Context Precision)
    /// </summary>
    public async Task<Dictionary<string, double>> EvaluateContextQualityAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        IEnumerable<string> relevantChunkIds,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // 비동기 시뮬레이션

        var retrievedIds = retrievedChunks.Select(c => c.Id).ToList();
        var relevantIds = relevantChunkIds.ToHashSet();

        // Context Precision: 검색된 컨텍스트 중 관련 있는 비율
        var relevantRetrievedCount = retrievedIds.Count(id => relevantIds.Contains(id));
        var contextPrecision = retrievedIds.Count > 0 ? (double)relevantRetrievedCount / retrievedIds.Count : 0.0;

        // Context Recall: 관련 컨텍스트 중 검색된 비율
        var contextRecall = relevantIds.Count > 0 ? (double)relevantRetrievedCount / relevantIds.Count : 0.0;

        // Context Relevancy: LLM 기반 평가 (simplified)
        var contextRelevancy = (contextPrecision + contextRecall) / 2.0;

        return new Dictionary<string, double>
        {
            ["context_relevancy"] = contextRelevancy,
            ["context_precision"] = contextPrecision,
            ["context_recall"] = contextRecall
        };
    }

    /// <summary>
    /// 품질 임계값 검증
    /// </summary>
    public async Task<bool> ValidateQualityThresholdsAsync(
        BatchEvaluationResult evaluationResult,
        QualityThresholds thresholds,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);

        return evaluationResult.AveragePrecision >= thresholds.MinPrecision &&
               evaluationResult.AverageRecall >= thresholds.MinRecall &&
               evaluationResult.AverageF1Score >= thresholds.MinF1Score &&
               evaluationResult.AverageMRR >= thresholds.MinMRR &&
               evaluationResult.AverageNDCG >= thresholds.MinNDCG &&
               evaluationResult.AverageHitRate >= thresholds.MinHitRate &&
               evaluationResult.AverageFaithfulness >= thresholds.MinFaithfulness &&
               evaluationResult.AverageAnswerRelevancy >= thresholds.MinAnswerRelevancy &&
               evaluationResult.AverageContextRelevancy >= thresholds.MinContextRelevancy &&
               evaluationResult.AverageQueryDuration <= thresholds.MaxAcceptableLatency;
    }

    /// <summary>
    /// 평가 결과 비교 (두 시스템 간 성능 비교)
    /// </summary>
    public async Task<Dictionary<string, object>> CompareEvaluationResultsAsync(
        BatchEvaluationResult baseline,
        BatchEvaluationResult candidate,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);

        var comparison = new Dictionary<string, object>
        {
            ["precision_improvement"] = candidate.AveragePrecision - baseline.AveragePrecision,
            ["recall_improvement"] = candidate.AverageRecall - baseline.AverageRecall,
            ["f1_improvement"] = candidate.AverageF1Score - baseline.AverageF1Score,
            ["mrr_improvement"] = candidate.AverageMRR - baseline.AverageMRR,
            ["ndcg_improvement"] = candidate.AverageNDCG - baseline.AverageNDCG,
            ["hit_rate_improvement"] = candidate.AverageHitRate - baseline.AverageHitRate,
            ["faithfulness_improvement"] = candidate.AverageFaithfulness - baseline.AverageFaithfulness,
            ["answer_relevancy_improvement"] = candidate.AverageAnswerRelevancy - baseline.AverageAnswerRelevancy,
            ["context_relevancy_improvement"] = candidate.AverageContextRelevancy - baseline.AverageContextRelevancy,
            ["latency_improvement"] = baseline.AverageQueryDuration - candidate.AverageQueryDuration
        };

        var overallImprovement = new[]
        {
            (double)comparison["precision_improvement"],
            (double)comparison["recall_improvement"],
            (double)comparison["f1_improvement"],
            (double)comparison["mrr_improvement"],
            (double)comparison["ndcg_improvement"]
        }.Average();

        comparison["overall_improvement"] = overallImprovement;
        comparison["is_better"] = overallImprovement > 0;

        return comparison;
    }

    #region Private Helper Methods

    private double CalculateMRR(IEnumerable<string> retrievedIds, HashSet<string> relevantIds)
    {
        var retrieved = retrievedIds.ToList();
        for (int i = 0; i < retrieved.Count; i++)
        {
            if (relevantIds.Contains(retrieved[i]))
            {
                return 1.0 / (i + 1);
            }
        }
        return 0.0;
    }

    private double CalculateNDCG(IEnumerable<string> retrievedIds, HashSet<string> relevantIds, int k = 10)
    {
        var retrieved = retrievedIds.Take(k).ToList();

        // DCG 계산
        var dcg = 0.0;
        for (int i = 0; i < retrieved.Count; i++)
        {
            var relevance = relevantIds.Contains(retrieved[i]) ? 1.0 : 0.0;
            dcg += relevance / Math.Log2(i + 2);
        }

        // IDCG 계산 (이상적인 순서)
        var idealRelevanceCount = Math.Min(k, relevantIds.Count);
        var idcg = 0.0;
        for (int i = 0; i < idealRelevanceCount; i++)
        {
            idcg += 1.0 / Math.Log2(i + 2);
        }

        return idcg > 0 ? dcg / idcg : 0.0;
    }

    private IEnumerable<DocumentChunk> CreateMockRetrievedChunks(GoldenDatasetItem item)
    {
        // TODO: 실제 검색 시스템 호출로 대체
        var mockChunks = new List<DocumentChunk>();

        for (int i = 0; i < Math.Min(5, item.RelevantChunkIds.Count); i++)
        {
            mockChunks.Add(new DocumentChunk
            {
                Id = item.RelevantChunkIds[i],
                Content = $"Mock content for chunk {item.RelevantChunkIds[i]}",
                DocumentId = $"doc_{i}",
                ChunkIndex = i,
                Embedding = new float[384] // Mock embedding
            });
        }

        return mockChunks;
    }

    #endregion
}