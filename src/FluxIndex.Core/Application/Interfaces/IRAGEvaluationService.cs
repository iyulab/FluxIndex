using FluxIndex.Domain.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Interfaces;

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
        QualityThresholds thresholds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 평가 결과 비교 (두 시스템 간 성능 비교)
    /// </summary>
    Task<Dictionary<string, object>> CompareEvaluationResultsAsync(
        BatchEvaluationResult baseline,
        BatchEvaluationResult candidate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 골든 데이터셋 관리 서비스 인터페이스
/// </summary>
public interface IGoldenDatasetManager
{
    /// <summary>
    /// 골든 데이터셋 로드
    /// </summary>
    Task<IEnumerable<GoldenDatasetItem>> LoadDatasetAsync(
        string datasetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 골든 데이터셋 저장
    /// </summary>
    Task SaveDatasetAsync(
        string datasetId,
        IEnumerable<GoldenDatasetItem> dataset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 데이터셋 생성 (기존 검색 로그에서)
    /// </summary>
    Task<IEnumerable<GoldenDatasetItem>> CreateDatasetFromLogsAsync(
        IEnumerable<QueryLog> queryLogs,
        double minRelevanceScore = 0.8,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 데이터셋 검증 및 품질 확인
    /// </summary>
    Task<DatasetValidationResult> ValidateDatasetAsync(
        IEnumerable<GoldenDatasetItem> dataset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 데이터셋 통계 정보
    /// </summary>
    Task<DatasetStatistics> GetDatasetStatisticsAsync(
        string datasetId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 품질 게이트 서비스 인터페이스 (CI/CD 통합용)
/// </summary>
public interface IQualityGateService
{
    /// <summary>
    /// 품질 게이트 실행
    /// </summary>
    Task<QualityGateResult> ExecuteQualityGateAsync(
        string systemVersion,
        string datasetId,
        QualityThresholds thresholds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 이전 버전과 성능 비교
    /// </summary>
    Task<PerformanceComparisonResult> CompareWithBaselineAsync(
        string currentVersion,
        string baselineVersion,
        string datasetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 품질 회귀 감지
    /// </summary>
    Task<bool> DetectQualityRegressionAsync(
        BatchEvaluationResult currentResult,
        BatchEvaluationResult baselineResult,
        double regressionThreshold = 0.05,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 평가 작업 관리 서비스 인터페이스
/// </summary>
public interface IEvaluationJobManager
{
    /// <summary>
    /// 평가 작업 생성
    /// </summary>
    Task<string> CreateEvaluationJobAsync(
        string name,
        string datasetId,
        EvaluationConfiguration configuration,
        QualityThresholds thresholds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 평가 작업 실행
    /// </summary>
    Task<BatchEvaluationResult> ExecuteEvaluationJobAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 평가 작업 상태 확인
    /// </summary>
    Task<EvaluationJob> GetJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 실행 중인 작업 취소
    /// </summary>
    Task CancelJobAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 작업 목록 조회
    /// </summary>
    Task<IEnumerable<EvaluationJob>> GetJobsAsync(
        EvaluationStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 쿼리 로그 모델 (참고용)
/// </summary>
public class QueryLog
{
    public string Id { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<string> RetrievedChunkIds { get; set; } = new();
    public List<double> RelevanceScores { get; set; } = new();
    public string GeneratedAnswer { get; set; } = string.Empty;
    public double UserRating { get; set; }
    public bool UserAccepted { get; set; }
}

/// <summary>
/// 데이터셋 검증 결과
/// </summary>
public class DatasetValidationResult
{
    public bool IsValid { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int TotalItems { get; set; }
    public int ValidItems { get; set; }
    public Dictionary<string, int> CategoryDistribution { get; set; } = new();
    public Dictionary<EvaluationDifficulty, int> DifficultyDistribution { get; set; } = new();
}

/// <summary>
/// 데이터셋 통계
/// </summary>
public class DatasetStatistics
{
    public string DatasetId { get; set; } = string.Empty;
    public int TotalQueries { get; set; }
    public int TotalRelevantDocuments { get; set; }
    public double AverageQueriesPerDocument { get; set; }
    public Dictionary<string, int> CategoryCounts { get; set; } = new();
    public Dictionary<EvaluationDifficulty, int> DifficultyCounts { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// 품질 게이트 결과
/// </summary>
public class QualityGateResult
{
    public bool Passed { get; set; }
    public string SystemVersion { get; set; } = string.Empty;
    public BatchEvaluationResult EvaluationResult { get; set; } = new();
    public QualityThresholds AppliedThresholds { get; set; } = new();
    public List<string> FailedCriteria { get; set; } = new();
    public Dictionary<string, object> Summary { get; set; } = new();
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 성능 비교 결과
/// </summary>
public class PerformanceComparisonResult
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string BaselineVersion { get; set; } = string.Empty;
    public Dictionary<string, double> CurrentMetrics { get; set; } = new();
    public Dictionary<string, double> BaselineMetrics { get; set; } = new();
    public Dictionary<string, double> Improvements { get; set; } = new();
    public Dictionary<string, double> Regressions { get; set; } = new();
    public double OverallImprovement { get; set; }
    public bool HasSignificantRegression { get; set; }
}