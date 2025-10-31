using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Models;

/// <summary>
/// 배치 메타데이터 추출 요청
/// </summary>
public class BatchMetadataExtractionRequest
{
    /// <summary>
    /// 배치 ID
    /// </summary>
    public string BatchId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 추출할 문서 목록
    /// </summary>
    public List<MetadataExtractionItem> Items { get; set; } = new();

    /// <summary>
    /// 병렬 처리 수준 (기본값: 4)
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// 부분 실패 시에도 계속 진행할지 여부
    /// </summary>
    public bool ContinueOnError { get; set; } = true;
}

/// <summary>
/// 메타데이터 추출 항목
/// </summary>
public class MetadataExtractionItem
{
    /// <summary>
    /// 문서 ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 문서 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 추출 스키마 (null이면 요청의 기본값 사용)
    /// </summary>
    public MetadataSchema? Schema { get; set; }

    /// <summary>
    /// 추출 전략 (null이면 요청의 기본값 사용)
    /// </summary>
    public MetadataExtractionStrategy? Strategy { get; set; }

    /// <summary>
    /// 커스텀 메타데이터
    /// </summary>
    public Dictionary<string, object> CustomMetadata { get; set; } = new();
}

/// <summary>
/// 배치 메타데이터 추출 결과
/// </summary>
public class BatchMetadataExtractionResult
{
    /// <summary>
    /// 배치 ID
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// 시작 시간
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 완료 시간
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 총 처리 시간
    /// </summary>
    public TimeSpan ProcessingTime => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : DateTime.UtcNow - StartedAt;

    /// <summary>
    /// 총 항목 수
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// 성공한 항목 수
    /// </summary>
    public int SuccessfulItems { get; set; }

    /// <summary>
    /// 실패한 항목 수
    /// </summary>
    public int FailedItems { get; set; }

    /// <summary>
    /// 건너뛴 항목 수
    /// </summary>
    public int SkippedItems { get; set; }

    /// <summary>
    /// 항목별 결과
    /// </summary>
    public List<MetadataExtractionItemResult> ItemResults { get; set; } = new();

    /// <summary>
    /// 전체 통계
    /// </summary>
    public BatchMetadataStatistics Statistics { get; set; } = new();
}

/// <summary>
/// 항목별 메타데이터 추출 결과
/// </summary>
public class MetadataExtractionItemResult
{
    /// <summary>
    /// 문서 ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 추출된 메타데이터 (성공 시)
    /// </summary>
    public ExtractedMetadata? Metadata { get; set; }

    /// <summary>
    /// 오류 메시지 (실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 처리 시간
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }

    /// <summary>
    /// 타임스탬프
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 배치 메타데이터 추출 통계
/// </summary>
public class BatchMetadataStatistics
{
    /// <summary>
    /// 평균 신뢰도
    /// </summary>
    public float AverageConfidence { get; set; }

    /// <summary>
    /// 평균 처리 시간
    /// </summary>
    public TimeSpan AverageProcessingTime { get; set; }

    /// <summary>
    /// 가장 많이 나타난 주제 (상위 10개)
    /// </summary>
    public Dictionary<string, int> TopTopics { get; set; } = new();

    /// <summary>
    /// 가장 많이 나타난 키워드 (상위 20개)
    /// </summary>
    public Dictionary<string, int> TopKeywords { get; set; } = new();

    /// <summary>
    /// 문서 타입별 분포
    /// </summary>
    public Dictionary<string, int> DocumentTypeDistribution { get; set; } = new();

    /// <summary>
    /// 언어별 분포
    /// </summary>
    public Dictionary<string, int> LanguageDistribution { get; set; } = new();

    /// <summary>
    /// 추출 방법별 분포
    /// </summary>
    public Dictionary<string, int> ExtractionMethodDistribution { get; set; } = new();
}

/// <summary>
/// 배치 메타데이터 추출 진행 상황
/// </summary>
public class BatchMetadataExtractionProgress
{
    /// <summary>
    /// 배치 ID
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// 현재 처리 중인 항목 인덱스
    /// </summary>
    public int CurrentItemIndex { get; set; }

    /// <summary>
    /// 총 항목 수
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// 진행률 (0-100)
    /// </summary>
    public float ProgressPercentage => TotalItems > 0
        ? (float)CurrentItemIndex / TotalItems * 100
        : 0;

    /// <summary>
    /// 현재 상태
    /// </summary>
    public BatchExtractionStatus Status { get; set; } = BatchExtractionStatus.Pending;

    /// <summary>
    /// 성공한 항목 수
    /// </summary>
    public int SuccessfulItems { get; set; }

    /// <summary>
    /// 실패한 항목 수
    /// </summary>
    public int FailedItems { get; set; }

    /// <summary>
    /// 현재 처리 중인 문서 ID
    /// </summary>
    public string? CurrentDocumentId { get; set; }

    /// <summary>
    /// 메시지
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 타임스탬프
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 예상 남은 시간
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}

/// <summary>
/// 배치 추출 상태
/// </summary>
public enum BatchExtractionStatus
{
    /// <summary>
    /// 대기 중
    /// </summary>
    Pending,

    /// <summary>
    /// 처리 중
    /// </summary>
    Processing,

    /// <summary>
    /// 완료
    /// </summary>
    Completed,

    /// <summary>
    /// 실패
    /// </summary>
    Failed,

    /// <summary>
    /// 취소됨
    /// </summary>
    Cancelled,

    /// <summary>
    /// 부분 완료 (일부 실패)
    /// </summary>
    PartiallyCompleted
}
