using Flux.Abstractions;
using FluxIndex.Core.Application.Models;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 청크 분류 서비스 인터페이스
/// </summary>
public interface IChunkClassificationService
{
    /// <summary>
    /// 단일 청크 분류
    /// </summary>
    Task<ChunkClassification> ClassifyAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 청크 분류 (비용 최적화)
    /// </summary>
    Task<Dictionary<string, ChunkClassification>> ClassifyBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        string? documentSummary = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 분류 검증 서비스 인터페이스
/// </summary>
public interface IClassificationValidationService
{
    /// <summary>
    /// LLM 분류 필요 여부 검증
    /// </summary>
    Task<ClassificationValidationResult> ValidateAsync(
        IEnrichedChunk chunk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 배치 검증
    /// </summary>
    Task<Dictionary<string, ClassificationValidationResult>> ValidateBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// LLM 출력 검증
    /// </summary>
    bool ValidateOutput(ChunkClassification classification);
}

/// <summary>
/// 분류 캐시 서비스 인터페이스
/// </summary>
public interface IClassificationCacheService
{
    /// <summary>
    /// 캐시에서 분류 조회
    /// </summary>
    Task<ChunkClassification?> GetAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 캐시에 분류 저장
    /// </summary>
    Task SetAsync(string chunkId, ChunkClassification classification, CancellationToken cancellationToken = default);

    /// <summary>
    /// 유사 청크의 분류 조회
    /// </summary>
    Task<ChunkClassification?> GetSimilarAsync(
        string contentHash,
        double similarityThreshold,
        CancellationToken cancellationToken = default);
}
