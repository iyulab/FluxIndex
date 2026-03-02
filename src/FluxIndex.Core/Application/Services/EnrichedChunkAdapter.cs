using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// IEnrichedChunk을 AugmentedChunk으로 변환하는 어댑터
/// FileFlux/WebFlux의 청크를 FluxIndex 내부 모델로 변환
/// </summary>
public static class EnrichedChunkAdapter
{
    /// <summary>
    /// IEnrichedChunk을 AugmentedChunk으로 변환
    /// </summary>
    /// <param name="chunk">FileFlux 또는 WebFlux의 청크</param>
    /// <returns>FluxIndex 내부 모델</returns>
    public static AugmentedChunk ToAugmentedChunk(IEnrichedChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        return new AugmentedChunk
        {
            Content = chunk.Content,
            ChunkId = chunk.ChunkId,
            ChunkIndex = chunk.ChunkIndex,
            HeadingPath = chunk.HeadingPath?.ToList() ?? [],
            SectionTitle = chunk.SectionTitle,
            StartPage = chunk.StartPage,
            EndPage = chunk.EndPage,
            Quality = chunk.Quality,
            ContextDependency = chunk.ContextDependency,
            TokenCount = chunk.TokenCount,
            Source = ToSourceMetadata(chunk.Source)
        };
    }

    /// <summary>
    /// ISourceMetadata를 SourceMetadata로 변환
    /// </summary>
    /// <param name="source">소스 메타데이터 인터페이스</param>
    /// <returns>소스 메타데이터 구현</returns>
    public static SourceMetadata ToSourceMetadata(ISourceMetadata source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SourceMetadata
        {
            SourceId = source.SourceId,
            SourceType = source.SourceType,
            Title = source.Title,
            FilePath = source.FilePath,
            Url = source.Url,
            CreatedAt = source.CreatedAt,
            Language = source.Language,
            LanguageConfidence = source.LanguageConfidence,
            WordCount = source.WordCount,
            ChunkCount = source.ChunkCount,
            PageCount = source.PageCount,
            PublishedAt = source.PublishedAt,
            Author = source.Author,
            Keywords = source.Keywords
        };
    }

    /// <summary>
    /// 여러 IEnrichedChunk을 AugmentedChunk 목록으로 변환
    /// </summary>
    /// <param name="chunks">청크 목록</param>
    /// <returns>변환된 청크 목록</returns>
    public static List<AugmentedChunk> ToAugmentedChunks(IEnumerable<IEnrichedChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        return chunks.Select(ToAugmentedChunk).ToList();
    }

    /// <summary>
    /// Contextual Header가 포함된 AugmentedChunk 생성
    /// </summary>
    /// <param name="chunk">원본 청크</param>
    /// <param name="contextualHeader">생성된 Contextual Header</param>
    /// <returns>증강된 청크</returns>
    public static AugmentedChunk WithContextualHeader(IEnrichedChunk chunk, string contextualHeader)
    {
        var augmented = ToAugmentedChunk(chunk);
        augmented.ContextualHeader = contextualHeader;
        return augmented;
    }

    /// <summary>
    /// 기존 AugmentedChunk에 증강 데이터 추가
    /// </summary>
    /// <param name="chunk">기존 청크</param>
    /// <param name="contextualHeader">Contextual Header</param>
    /// <param name="summary">요약</param>
    /// <param name="topics">토픽 목록</param>
    /// <param name="potentialQuestions">예상 질문 목록</param>
    /// <returns>증강된 청크</returns>
    public static AugmentedChunk WithAugmentation(
        AugmentedChunk chunk,
        string? contextualHeader = null,
        string? summary = null,
        List<string>? topics = null,
        List<string>? potentialQuestions = null)
    {
        if (contextualHeader != null)
            chunk.ContextualHeader = contextualHeader;
        if (summary != null)
            chunk.Summary = summary;
        if (topics != null)
            chunk.Topics = topics;
        if (potentialQuestions != null)
            chunk.PotentialQuestions = potentialQuestions;

        return chunk;
    }
}

/// <summary>
/// IEnrichedChunk 확장 메서드
/// </summary>
public static class EnrichedChunkExtensions
{
    /// <summary>
    /// IEnrichedChunk을 AugmentedChunk으로 변환
    /// </summary>
    public static AugmentedChunk ToAugmentedChunk(this IEnrichedChunk chunk)
        => EnrichedChunkAdapter.ToAugmentedChunk(chunk);

    /// <summary>
    /// 여러 IEnrichedChunk을 AugmentedChunk 목록으로 변환
    /// </summary>
    public static List<AugmentedChunk> ToAugmentedChunks(this IEnumerable<IEnrichedChunk> chunks)
        => EnrichedChunkAdapter.ToAugmentedChunks(chunks);

    /// <summary>
    /// Contextual Header와 함께 AugmentedChunk 생성
    /// </summary>
    public static AugmentedChunk WithContextualHeader(this IEnrichedChunk chunk, string contextualHeader)
        => EnrichedChunkAdapter.WithContextualHeader(chunk, contextualHeader);
}
