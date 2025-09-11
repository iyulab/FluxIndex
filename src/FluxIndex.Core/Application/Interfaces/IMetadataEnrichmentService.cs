using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 메타데이터 풍부화 서비스 인터페이스
/// </summary>
public interface IMetadataEnrichmentService
{
    /// <summary>
    /// 청크 메타데이터 풍부화
    /// </summary>
    Task<ChunkMetadata> EnrichMetadataAsync(
        string content,
        int chunkIndex,
        string? previousChunkContent = null,
        string? nextChunkContent = null,
        Dictionary<string, object>? documentMetadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 청크 관계 분석
    /// </summary>
    Task<List<ChunkRelationship>> AnalyzeRelationshipsAsync(
        DocumentChunk sourceChunk,
        IEnumerable<DocumentChunk> candidateChunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 청크 품질 평가
    /// </summary>
    Task<ChunkQuality> EvaluateQualityAsync(
        DocumentChunk chunk,
        string? query = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 텍스트 분석 서비스 인터페이스
/// </summary>
public interface ITextAnalysisService
{
    Task<double> CalculateSimilarityAsync(string text1, string text2, CancellationToken cancellationToken = default);
    Task<double> EvaluateCoherenceAsync(string content, CancellationToken cancellationToken = default);
    Task<double> CalculateRelevanceAsync(string content, string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// 엔터티 추출 서비스 인터페이스
/// </summary>
public interface IEntityExtractionService
{
    Task<List<string>> ExtractEntitiesAsync(string content, CancellationToken cancellationToken = default);
}

/// <summary>
/// 키워드 추출 서비스 인터페이스
/// </summary>
public interface IKeywordExtractionService
{
    Task<List<string>> ExtractKeywordsAsync(string content, CancellationToken cancellationToken = default);
}