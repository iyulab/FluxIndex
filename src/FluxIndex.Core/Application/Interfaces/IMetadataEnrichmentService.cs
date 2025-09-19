using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Core.Application.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 텍스트 청크로부터 구조화된 메타데이터를 추출하는 서비스 인터페이스
/// 테스트 가능성을 위한 명확한 계약과 의존성 분리
/// </summary>
public interface IMetadataEnrichmentService
{
    /// <summary>
    /// 단일 텍스트 청크에서 메타데이터 추출
    /// </summary>
    /// <param name="content">추출할 텍스트 내용</param>
    /// <param name="context">추가 컨텍스트 정보 (선택사항)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 메타데이터</returns>
    /// <exception cref="ArgumentException">내용이 비어있을 때</exception>
    /// <exception cref="MetadataExtractionException">추출 실패 시</exception>
    Task<ChunkMetadata> ExtractMetadataAsync(
        string content,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 텍스트 청크를 배치로 처리하여 메타데이터 추출
    /// 효율성과 비용 절감을 위한 배치 처리
    /// </summary>
    /// <param name="contents">추출할 텍스트 목록</param>
    /// <param name="options">배치 처리 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 메타데이터 목록 (입력 순서 유지)</returns>
    /// <exception cref="ArgumentException">내용 목록이 비어있을 때</exception>
    Task<IReadOnlyList<ChunkMetadata>> ExtractBatchAsync(
        IReadOnlyList<string> contents,
        BatchProcessingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 사용자 정의 스키마를 사용한 메타데이터 추출
    /// 특정 도메인에 맞춤화된 메타데이터 추출
    /// </summary>
    /// <typeparam name="T">스키마 타입</typeparam>
    /// <param name="content">추출할 텍스트 내용</param>
    /// <param name="schema">사용자 정의 스키마</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추출된 메타데이터</returns>
    Task<ChunkMetadata> ExtractWithSchemaAsync<T>(
        string content,
        T schema,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 서비스 상태 확인 (헬스체크용)
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>서비스가 정상 작동하는지 여부</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 서비스 사용 통계 조회 (모니터링용)
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>사용 통계 정보</returns>
    Task<MetadataExtractionStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 메타데이터 추출 과정에서 발생하는 예외
/// </summary>
public class MetadataExtractionException : Exception
{
    /// <summary>
    /// 오류 타입
    /// </summary>
    public MetadataExtractionErrorType ErrorType { get; }

    /// <summary>
    /// 재시도 가능 여부
    /// </summary>
    public bool IsRetryable { get; }

    public MetadataExtractionException(
        string message,
        MetadataExtractionErrorType errorType = MetadataExtractionErrorType.Unknown,
        bool isRetryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorType = errorType;
        IsRetryable = isRetryable;
    }

    /// <summary>
    /// 테스트용 예외 생성
    /// </summary>
    public static MetadataExtractionException CreateForTesting(
        string message = "Test extraction error",
        bool isRetryable = false) =>
        new(message, MetadataExtractionErrorType.ServiceUnavailable, isRetryable);
}

/// <summary>
/// 메타데이터 추출 오류 타입
/// </summary>
public enum MetadataExtractionErrorType
{
    Unknown = 0,
    InvalidInput = 1,
    ServiceUnavailable = 2,
    TimeoutError = 3,
    AuthenticationError = 4,
    QuotaExceeded = 5,
    InvalidResponse = 6,
    NetworkError = 7
}

/// <summary>
/// 메타데이터 추출 서비스 사용 통계
/// </summary>
public record MetadataExtractionStatistics
{
    /// <summary>
    /// 총 처리된 청크 수
    /// </summary>
    public long TotalProcessedChunks { get; init; }

    /// <summary>
    /// 성공한 추출 수
    /// </summary>
    public long SuccessfulExtractions { get; init; }

    /// <summary>
    /// 실패한 추출 수
    /// </summary>
    public long FailedExtractions { get; init; }

    /// <summary>
    /// 평균 처리 시간 (밀리초)
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }

    /// <summary>
    /// 평균 품질 점수
    /// </summary>
    public float AverageQualityScore { get; init; }

    /// <summary>
    /// 총 비용 (USD)
    /// </summary>
    public decimal TotalCostUsd { get; init; }

    /// <summary>
    /// 배치 처리로 절약된 비용 (USD)
    /// </summary>
    public decimal CostSavingsFromBatchingUsd { get; init; }

    /// <summary>
    /// 성공률 (0.0 ~ 1.0)
    /// </summary>
    public float SuccessRate =>
        TotalProcessedChunks > 0 ? (float)SuccessfulExtractions / TotalProcessedChunks : 0f;

    /// <summary>
    /// 테스트용 통계 생성
    /// </summary>
    public static MetadataExtractionStatistics CreateForTesting() => new()
    {
        TotalProcessedChunks = 100,
        SuccessfulExtractions = 95,
        FailedExtractions = 5,
        AverageProcessingTimeMs = 1250.5,
        AverageQualityScore = 0.82f,
        TotalCostUsd = 2.45m,
        CostSavingsFromBatchingUsd = 0.98m
    };
}

/// <summary>
/// 메타데이터 추출 결과 (배치 처리용)
/// </summary>
public record MetadataExtractionResult
{
    /// <summary>
    /// 원본 텍스트 인덱스
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// 추출된 메타데이터 (성공 시)
    /// </summary>
    public ChunkMetadata? Metadata { get; init; }

    /// <summary>
    /// 오류 정보 (실패 시)
    /// </summary>
    public MetadataExtractionException? Error { get; init; }

    /// <summary>
    /// 처리 시간 (밀리초)
    /// </summary>
    public double ProcessingTimeMs { get; init; }

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool IsSuccess => Metadata is not null && Error is null;

    /// <summary>
    /// 성공 결과 생성
    /// </summary>
    public static MetadataExtractionResult Success(int index, ChunkMetadata metadata, double processingTimeMs) =>
        new()
        {
            Index = index,
            Metadata = metadata,
            ProcessingTimeMs = processingTimeMs
        };

    /// <summary>
    /// 실패 결과 생성
    /// </summary>
    public static MetadataExtractionResult Failure(int index, MetadataExtractionException error, double processingTimeMs) =>
        new()
        {
            Index = index,
            Error = error,
            ProcessingTimeMs = processingTimeMs
        };
}