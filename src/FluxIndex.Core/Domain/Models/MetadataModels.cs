using System;
using System.Collections.Generic;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// 메타데이터 추출 통계
/// </summary>
public class MetadataExtractionStatistics
{
    /// <summary>
    /// 처리된 청크 수
    /// </summary>
    public int ProcessedChunkCount { get; set; }

    /// <summary>
    /// 성공적으로 추출된 메타데이터 수
    /// </summary>
    public int SuccessfulExtractions { get; set; }

    /// <summary>
    /// 실패한 추출 수
    /// </summary>
    public int FailedExtractions { get; set; }

    /// <summary>
    /// 전체 처리 시간
    /// </summary>
    public TimeSpan TotalProcessingTime { get; set; }

    /// <summary>
    /// 평균 처리 시간 (청크당)
    /// </summary>
    public TimeSpan AverageProcessingTime => ProcessedChunkCount > 0
        ? TimeSpan.FromTicks(TotalProcessingTime.Ticks / ProcessedChunkCount)
        : TimeSpan.Zero;

    /// <summary>
    /// 성공률
    /// </summary>
    public float SuccessRate => ProcessedChunkCount > 0
        ? (float)SuccessfulExtractions / ProcessedChunkCount
        : 0f;

    /// <summary>
    /// 평균 품질 점수
    /// </summary>
    public float AverageQualityScore { get; set; }

    /// <summary>
    /// 사용된 총 토큰 수
    /// </summary>
    public int TotalTokensUsed { get; set; }

    /// <summary>
    /// 오류 정보
    /// </summary>
    public Dictionary<MetadataExtractionErrorType, int> ErrorCounts { get; set; } = new();
}

/// <summary>
/// 청크 메타데이터
/// </summary>
public class ChunkMetadata
{
    /// <summary>
    /// 주제
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// 카테고리
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 키워드 목록
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// 요약
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 중요도 점수 (0-1)
    /// </summary>
    public float ImportanceScore { get; set; }

    /// <summary>
    /// 복잡도 수준
    /// </summary>
    public string ComplexityLevel { get; set; } = string.Empty;

    /// <summary>
    /// 언어
    /// </summary>
    public string Language { get; set; } = "ko";

    /// <summary>
    /// 감정 점수 (-1 ~ 1)
    /// </summary>
    public float SentimentScore { get; set; }

    /// <summary>
    /// 개체명 목록
    /// </summary>
    public List<string> NamedEntities { get; set; } = new();

    /// <summary>
    /// 추가 속성
    /// </summary>
    public Dictionary<string, object> AdditionalProperties { get; set; } = new();

    /// <summary>
    /// 추출 시간
    /// </summary>
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 품질 점수
    /// </summary>
    public float QualityScore { get; set; }
}

/// <summary>
/// 메타데이터 추출 옵션
/// </summary>
public class MetadataExtractionOptions
{
    /// <summary>
    /// 키워드 추출 활성화
    /// </summary>
    public bool ExtractKeywords { get; set; } = true;

    /// <summary>
    /// 최대 키워드 수
    /// </summary>
    public int MaxKeywords { get; set; } = 10;

    /// <summary>
    /// 요약 생성 활성화
    /// </summary>
    public bool GenerateSummary { get; set; } = true;

    /// <summary>
    /// 최대 요약 길이
    /// </summary>
    public int MaxSummaryLength { get; set; } = 200;

    /// <summary>
    /// 감정 분석 활성화
    /// </summary>
    public bool AnalyzeSentiment { get; set; } = false;

    /// <summary>
    /// 개체명 인식 활성화
    /// </summary>
    public bool ExtractEntities { get; set; } = true;

    /// <summary>
    /// 중요도 계산 활성화
    /// </summary>
    public bool CalculateImportance { get; set; } = true;

    /// <summary>
    /// 언어 감지 활성화
    /// </summary>
    public bool DetectLanguage { get; set; } = true;

    /// <summary>
    /// 병렬 처리 활성화
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;

    /// <summary>
    /// 최대 동시 요청 수
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 3;

    /// <summary>
    /// 품질 임계값
    /// </summary>
    public float QualityThreshold { get; set; } = 0.5f;
}

/// <summary>
/// 배치 처리 옵션
/// </summary>
public class BatchProcessingOptions
{
    /// <summary>
    /// 배치 크기
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// 최대 재시도 횟수
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 재시도 지연 시간
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 진행률 보고 활성화
    /// </summary>
    public bool ReportProgress { get; set; } = true;

    /// <summary>
    /// 오류 시 중단 여부
    /// </summary>
    public bool StopOnError { get; set; } = false;
}

/// <summary>
/// 메타데이터 추출 오류 타입
/// </summary>
public enum MetadataExtractionErrorType
{
    /// <summary>
    /// API 호출 실패
    /// </summary>
    ApiCallFailed,

    /// <summary>
    /// 응답 파싱 실패
    /// </summary>
    ResponseParsingFailed,

    /// <summary>
    /// 타임아웃
    /// </summary>
    Timeout,

    /// <summary>
    /// 품질 기준 미달
    /// </summary>
    QualityTooLow,

    /// <summary>
    /// 입력 데이터 오류
    /// </summary>
    InvalidInput,

    /// <summary>
    /// 시스템 오류
    /// </summary>
    SystemError
}