using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Domain.Entities;
using EvaluationThresholds = FluxIndex.Core.Domain.Models.QualityThresholds;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// RAG 시스템 품질 평가 서비스 인터페이스
/// </summary>
public interface IRAGEvaluationService
{
    /// <summary>
    /// 단일 쿼리에 대한 RAG 시스템 평가
    /// </summary>
    Task<RAGEvaluationResult> EvaluateQueryAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        string generatedAnswer,
        GoldenDatasetItem goldenItem,
        EvaluationConfiguration? configuration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 골든 데이터셋에 대한 배치 평가
    /// </summary>
    Task<BatchEvaluationResult> EvaluateBatchAsync(
        IEnumerable<GoldenDatasetItem> goldenDataset,
        EvaluationConfiguration? configuration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 검색 품질 지표 계산 (Precision, Recall, F1, MRR, NDCG)
    /// </summary>
    Task<Dictionary<string, double>> CalculateRetrievalMetricsAsync(
        IEnumerable<DocumentChunk> retrievedChunks,
        IEnumerable<string> relevantChunkIds,
        int totalRelevantCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// LLM 기반 답변 품질 평가 (Faithfulness, Answer Relevancy)
    /// </summary>
    Task<Dictionary<string, double>> EvaluateAnswerQualityAsync(
        string query,
        string generatedAnswer,
        IEnumerable<DocumentChunk> sourceChunks,
        string expectedAnswer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 컨텍스트 품질 평가 (Context Relevancy, Context Precision)
    /// </summary>
    Task<Dictionary<string, double>> EvaluateContextQualityAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        IEnumerable<string> relevantChunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 임계값 검증
    /// </summary>
    Task<bool> ValidateQualityThresholdsAsync(
        BatchEvaluationResult evaluationResult,
        EvaluationThresholds thresholds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 평가 결과 비교 (두 시스템 간 성능 비교)
    /// </summary>
    Task<Dictionary<string, object>> CompareEvaluationResultsAsync(
        BatchEvaluationResult baseline,
        BatchEvaluationResult candidate,
        CancellationToken cancellationToken = default);
}

