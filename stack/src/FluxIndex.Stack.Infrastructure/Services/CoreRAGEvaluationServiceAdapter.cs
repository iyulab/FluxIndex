using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;

// Alias to resolve ambiguity with FluxImprover types
using FluxImproverRAGService = FluxIndex.SDK.Extensions.FluxImprover.Services.RAGEvaluationService;
using CoreRAGEvaluationResult = FluxIndex.Core.Domain.Models.RAGEvaluationResult;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Adapts FluxImprover's RAGEvaluationService to Core's IRAGEvaluationService interface.
/// Bridges the gap between Stack's FluxImprover integration and Core's evaluation framework.
/// </summary>
public partial class CoreRAGEvaluationServiceAdapter : IRAGEvaluationService
{
    private readonly FluxImproverRAGService? _fluxImproverService;
    private readonly ILogger<CoreRAGEvaluationServiceAdapter> _logger;

    public CoreRAGEvaluationServiceAdapter(
        ILogger<CoreRAGEvaluationServiceAdapter> logger,
        FluxImproverRAGService? fluxImproverService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fluxImproverService = fluxImproverService;
    }

    /// <inheritdoc />
    public async Task<CoreRAGEvaluationResult> EvaluateQueryAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        string generatedAnswer,
        GoldenDatasetItem goldenItem,
        EvaluationConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new CoreRAGEvaluationResult
        {
            QueryId = goldenItem.Id,
            Query = query,
            EvaluatedAt = startTime
        };

        try
        {
            var chunkList = retrievedChunks.ToList();

            // Calculate retrieval metrics
            var retrievalMetrics = await CalculateRetrievalMetricsAsync(
                chunkList,
                goldenItem.RelevantChunkIds.Count > 0 ? goldenItem.RelevantChunkIds : goldenItem.RelevantDocumentIds,
                goldenItem.RelevantChunkIds.Count + goldenItem.RelevantDocumentIds.Count,
                cancellationToken);

            result.Precision = retrievalMetrics.GetValueOrDefault("Precision", 0);
            result.Recall = retrievalMetrics.GetValueOrDefault("Recall", 0);
            result.F1Score = retrievalMetrics.GetValueOrDefault("F1Score", 0);
            result.MRR = retrievalMetrics.GetValueOrDefault("MRR", 0);
            result.NDCG = retrievalMetrics.GetValueOrDefault("NDCG", 0);
            result.HitRate = retrievalMetrics.GetValueOrDefault("HitRate", 0);

            // Calculate answer quality metrics using FluxImprover if available
            if (_fluxImproverService != null && configuration?.EnableFaithfulnessEvaluation == true)
            {
                var answerMetrics = await EvaluateAnswerQualityAsync(
                    query,
                    generatedAnswer,
                    chunkList,
                    goldenItem.ExpectedAnswer,
                    cancellationToken);

                result.Faithfulness = answerMetrics.GetValueOrDefault("Faithfulness", 0);
                result.AnswerRelevancy = answerMetrics.GetValueOrDefault("AnswerRelevancy", 0);
            }

            // Calculate context quality metrics
            if (configuration?.EnableContextEvaluation == true)
            {
                var contextMetrics = await EvaluateContextQualityAsync(
                    query,
                    chunkList,
                    goldenItem.RelevantChunkIds.Count > 0 ? goldenItem.RelevantChunkIds : goldenItem.RelevantDocumentIds,
                    cancellationToken);

                result.ContextRelevancy = contextMetrics.GetValueOrDefault("ContextRelevancy", 0);
                result.ContextPrecision = contextMetrics.GetValueOrDefault("ContextPrecision", 0);
                result.ContextRecall = contextMetrics.GetValueOrDefault("ContextRecall", 0);
            }

            result.RetrievedDocumentsCount = chunkList.Count;
            result.Duration = DateTime.UtcNow - startTime;

            LogEvaluatedQuery(_logger, goldenItem.Id, result.Precision, result.Recall);
        }
        catch (Exception ex)
        {
            LogEvaluateQueryFailed(_logger, goldenItem.Id, ex);
            throw;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<BatchEvaluationResult> EvaluateBatchAsync(
        IEnumerable<GoldenDatasetItem> goldenDataset,
        EvaluationConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        var batchResult = new BatchEvaluationResult
        {
            BatchId = Guid.NewGuid().ToString(),
            StartedAt = DateTime.UtcNow,
            Configuration = System.Text.Json.JsonSerializer.Serialize(configuration ?? new EvaluationConfiguration())
        };

        var datasetList = goldenDataset.ToList();
        batchResult.TotalQueries = datasetList.Count;

        LogStartingBatchEvaluation(_logger, datasetList.Count);

        // Note: This method expects retrievedChunks and generatedAnswer to be pre-computed
        // In the full implementation, this would use IEvaluationSearchProvider to retrieve and generate
        // For now, return empty results as this is meant to be called via EvaluationJobManager

        batchResult.CompletedAt = DateTime.UtcNow;
        batchResult.TotalDuration = batchResult.CompletedAt - batchResult.StartedAt;

        return batchResult;
    }

    /// <inheritdoc />
    public Task<Dictionary<string, double>> CalculateRetrievalMetricsAsync(
        IEnumerable<DocumentChunk> retrievedChunks,
        IEnumerable<string> relevantChunkIds,
        int totalRelevantCount,
        CancellationToken cancellationToken = default)
    {
        var retrieved = retrievedChunks.ToList();
        var relevant = new HashSet<string>(relevantChunkIds);

        // Calculate precision, recall, F1
        var truePositives = retrieved.Count(c => relevant.Contains(c.Id) || relevant.Contains(c.DocumentId));
        var precision = retrieved.Count > 0 ? (double)truePositives / retrieved.Count : 0;
        var recall = totalRelevantCount > 0 ? (double)truePositives / totalRelevantCount : 0;
        var f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;

        // Calculate MRR (Mean Reciprocal Rank)
        var mrr = 0.0;
        for (int i = 0; i < retrieved.Count; i++)
        {
            if (relevant.Contains(retrieved[i].Id) || relevant.Contains(retrieved[i].DocumentId))
            {
                mrr = 1.0 / (i + 1);
                break;
            }
        }

        // Calculate NDCG
        var dcg = 0.0;
        var idcg = 0.0;
        for (int i = 0; i < retrieved.Count; i++)
        {
            var isRelevant = relevant.Contains(retrieved[i].Id) || relevant.Contains(retrieved[i].DocumentId);
            if (isRelevant)
            {
                dcg += 1.0 / Math.Log2(i + 2); // +2 because i is 0-indexed and log(1)=0
            }
        }

        // Ideal DCG - assume all relevant docs are at the top
        var idealRelevant = Math.Min(totalRelevantCount, retrieved.Count);
        for (int i = 0; i < idealRelevant; i++)
        {
            idcg += 1.0 / Math.Log2(i + 2);
        }
        var ndcg = idcg > 0 ? dcg / idcg : 0;

        // Hit rate
        var hitRate = truePositives > 0 ? 1.0 : 0.0;

        return Task.FromResult(new Dictionary<string, double>
        {
            ["Precision"] = precision,
            ["Recall"] = recall,
            ["F1Score"] = f1,
            ["MRR"] = mrr,
            ["NDCG"] = ndcg,
            ["HitRate"] = hitRate
        });
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, double>> EvaluateAnswerQualityAsync(
        string query,
        string generatedAnswer,
        IEnumerable<DocumentChunk> sourceChunks,
        string expectedAnswer,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, double>
        {
            ["Faithfulness"] = 0.0,
            ["AnswerRelevancy"] = 0.0
        };

        if (_fluxImproverService == null)
        {
            LogFluxImproverNotAvailable(_logger);
            return result;
        }

        try
        {
            var context = string.Join("\n\n", sourceChunks.Select(c => c.Content));

            // Use FluxImprover's evaluation
            var evalResult = await _fluxImproverService.EvaluateAsync(
                context,
                query,
                generatedAnswer,
                cancellationToken: cancellationToken);

            result["Faithfulness"] = evalResult.Faithfulness?.Score ?? 0.0;
            result["AnswerRelevancy"] = evalResult.Relevancy?.Score ?? 0.0;
        }
        catch (Exception ex)
        {
            LogAnswerQualityEvaluationFailed(_logger, ex);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<Dictionary<string, double>> EvaluateContextQualityAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        IEnumerable<string> relevantChunkIds,
        CancellationToken cancellationToken = default)
    {
        var chunks = retrievedChunks.ToList();
        var relevant = new HashSet<string>(relevantChunkIds);

        // Context precision: proportion of retrieved chunks that are relevant
        var relevantRetrieved = chunks.Count(c => relevant.Contains(c.Id) || relevant.Contains(c.DocumentId));
        var contextPrecision = chunks.Count > 0 ? (double)relevantRetrieved / chunks.Count : 0;

        // Context recall: proportion of relevant chunks that were retrieved
        var contextRecall = relevant.Count > 0 ? (double)relevantRetrieved / relevant.Count : 0;

        // Context relevancy (simplified): average of precision and recall
        var contextRelevancy = (contextPrecision + contextRecall) / 2;

        return Task.FromResult(new Dictionary<string, double>
        {
            ["ContextPrecision"] = contextPrecision,
            ["ContextRecall"] = contextRecall,
            ["ContextRelevancy"] = contextRelevancy
        });
    }

    /// <inheritdoc />
    public Task<bool> ValidateQualityThresholdsAsync(
        BatchEvaluationResult evaluationResult,
        FluxIndex.Core.Domain.Models.QualityThresholds thresholds,
        CancellationToken cancellationToken = default)
    {
        var passed = evaluationResult.AveragePrecision >= thresholds.MinPrecision
            && evaluationResult.AverageRecall >= thresholds.MinRecall
            && evaluationResult.AverageF1Score >= thresholds.MinF1Score
            && evaluationResult.AverageMRR >= thresholds.MinMRR
            && evaluationResult.AverageNDCG >= thresholds.MinNDCG;

        var resultStr = passed ? "PASSED" : "FAILED";
        LogQualityThresholdValidation(_logger, resultStr);

        return Task.FromResult(passed);
    }

    /// <inheritdoc />
    public Task<Dictionary<string, object>> CompareEvaluationResultsAsync(
        BatchEvaluationResult baseline,
        BatchEvaluationResult candidate,
        CancellationToken cancellationToken = default)
    {
        var comparison = new Dictionary<string, object>
        {
            ["PrecisionDelta"] = candidate.AveragePrecision - baseline.AveragePrecision,
            ["RecallDelta"] = candidate.AverageRecall - baseline.AverageRecall,
            ["F1Delta"] = candidate.AverageF1Score - baseline.AverageF1Score,
            ["MRRDelta"] = candidate.AverageMRR - baseline.AverageMRR,
            ["NDCGDelta"] = candidate.AverageNDCG - baseline.AverageNDCG,
            ["Improved"] = candidate.AverageF1Score > baseline.AverageF1Score
        };

        return Task.FromResult(comparison);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evaluated query {QueryId}: Precision={Precision:F3}, Recall={Recall:F3}")]
    private static partial void LogEvaluatedQuery(ILogger logger, string queryId, double precision, double recall);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to evaluate query: {QueryId}")]
    private static partial void LogEvaluateQueryFailed(ILogger logger, string queryId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch evaluation with {Count} queries")]
    private static partial void LogStartingBatchEvaluation(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FluxImprover RAGEvaluationService not available. Using default scores.")]
    private static partial void LogFluxImproverNotAvailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to evaluate answer quality with FluxImprover. Using default scores.")]
    private static partial void LogAnswerQualityEvaluationFailed(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Quality threshold validation: {Result}")]
    private static partial void LogQualityThresholdValidation(ILogger logger, string result);

    #endregion
}
