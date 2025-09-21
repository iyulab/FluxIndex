using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// RAG 평가 결과 모델
/// </summary>
public class RAGEvaluationResult
{
    public string QueryId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }

    // 검색 품질 지표
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1Score { get; set; }
    public double MRR { get; set; } // Mean Reciprocal Rank
    public double NDCG { get; set; } // Normalized Discounted Cumulative Gain
    public double HitRate { get; set; }

    // 답변 품질 지표
    public double Faithfulness { get; set; } // 답변이 검색된 문서에 근거하는 정도
    public double AnswerRelevancy { get; set; } // 답변이 질문과 관련된 정도
    public double ContextRelevancy { get; set; } // 검색된 컨텍스트가 질문과 관련된 정도
    public double ContextPrecision { get; set; } // 검색된 컨텍스트의 정밀도
    public double ContextRecall { get; set; } // 검색된 컨텍스트의 재현율

    // 추가 메타데이터
    public int RetrievedDocumentsCount { get; set; }
    public int RelevantDocumentsCount { get; set; }
    public int TotalRelevantDocuments { get; set; }
    public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
    public string SearchMethod { get; set; } = string.Empty;
}

/// <summary>
/// 골든 데이터셋 항목
/// </summary>
public class GoldenDatasetItem
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Query { get; set; } = string.Empty;

    [Required]
    public string ExpectedAnswer { get; set; } = string.Empty;

    public List<string> RelevantDocumentIds { get; set; } = new();
    public List<string> RelevantChunkIds { get; set; } = new();

    // 평가 가중치
    public double Weight { get; set; } = 1.0;
    public EvaluationDifficulty Difficulty { get; set; } = EvaluationDifficulty.Medium;
    public List<string> Categories { get; set; } = new();

    // 메타데이터
    public string Source { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 평가 난이도
/// </summary>
public enum EvaluationDifficulty
{
    Easy = 1,
    Medium = 2,
    Hard = 3,
    Expert = 4
}

/// <summary>
/// 평가 설정
/// </summary>
public class EvaluationConfiguration
{
    public int MaxRetrievedDocuments { get; set; } = 10;
    public double MinRelevanceThreshold { get; set; } = 0.5;
    public bool EnableFaithfulnessEvaluation { get; set; } = true;
    public bool EnableAnswerRelevancyEvaluation { get; set; } = true;
    public bool EnableContextEvaluation { get; set; } = true;
    public string LLMModel { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.1;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}

/// <summary>
/// 배치 평가 결과
/// </summary>
public class BatchEvaluationResult
{
    public string BatchId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public List<RAGEvaluationResult> Results { get; set; } = new();

    // 집계 지표
    public double AveragePrecision { get; set; }
    public double AverageRecall { get; set; }
    public double AverageF1Score { get; set; }
    public double AverageMRR { get; set; }
    public double AverageNDCG { get; set; }
    public double AverageHitRate { get; set; }
    public double AverageFaithfulness { get; set; }
    public double AverageAnswerRelevancy { get; set; }
    public double AverageContextRelevancy { get; set; }

    // 성능 통계
    public int TotalQueries { get; set; }
    public int SuccessfulQueries { get; set; }
    public int FailedQueries { get; set; }
    public double SuccessRate { get; set; }
    public double AverageQueryDuration { get; set; }

    public string Configuration { get; set; } = string.Empty;
    public Dictionary<string, object> Summary { get; set; } = new();
}

/// <summary>
/// 평가 기준 정의
/// </summary>
public class EvaluationCriteria
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Weight { get; set; } = 1.0;
    public double MinAcceptableScore { get; set; } = 0.7;
    public string EvaluationPrompt { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// 품질 임계값 설정
/// </summary>
public class QualityThresholds
{
    public double MinPrecision { get; set; } = 0.7;
    public double MinRecall { get; set; } = 0.7;
    public double MinF1Score { get; set; } = 0.7;
    public double MinMRR { get; set; } = 0.7;
    public double MinNDCG { get; set; } = 0.7;
    public double MinHitRate { get; set; } = 0.8;
    public double MinFaithfulness { get; set; } = 0.8;
    public double MinAnswerRelevancy { get; set; } = 0.7;
    public double MinContextRelevancy { get; set; } = 0.7;
    public double MaxAcceptableLatency { get; set; } = 2000; // milliseconds
}

/// <summary>
/// 평가 상태
/// </summary>
public enum EvaluationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 평가 작업 정보
/// </summary>
public class EvaluationJob
{
    public string JobId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EvaluationStatus Status { get; set; } = EvaluationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string DatasetId { get; set; } = string.Empty;
    public EvaluationConfiguration Configuration { get; set; } = new();
    public QualityThresholds Thresholds { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
    public int Progress { get; set; } = 0; // 0-100
    public Dictionary<string, object> Metadata { get; set; } = new();
}