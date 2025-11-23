namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Contextual Header 생성기 인터페이스
/// 청크 앞에 추가할 문맥 정보를 생성
/// </summary>
public interface IContextualHeaderGenerator
{
    /// <summary>
    /// Contextual Header 생성
    /// </summary>
    /// <param name="chunk">증강할 청크</param>
    /// <param name="documentSummary">문서 요약 (LLM 생성 시 사용)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 Contextual Header</returns>
    Task<string> GenerateAsync(
        IEnrichedChunk chunk,
        string? documentSummary = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 청크의 Contextual Header 일괄 생성
    /// </summary>
    /// <param name="chunks">증강할 청크 목록</param>
    /// <param name="documentSummary">문서 요약</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>ChunkId와 Contextual Header 매핑</returns>
    Task<Dictionary<string, string>> GenerateBatchAsync(
        IEnumerable<IEnrichedChunk> chunks,
        string? documentSummary = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Contextual Header 생성 옵션
/// </summary>
public class ContextualHeaderOptions
{
    /// <summary>
    /// LLM 호출 임계값 (ContextDependency가 이 값 이상이면 LLM 사용)
    /// </summary>
    public double LlmThreshold { get; set; } = 0.7;

    /// <summary>
    /// 최대 헤더 길이 (문자 수)
    /// </summary>
    public int MaxHeaderLength { get; set; } = 200;

    /// <summary>
    /// 페이지 정보 포함 여부
    /// </summary>
    public bool IncludePageInfo { get; set; } = true;

    /// <summary>
    /// 문서 제목 포함 여부
    /// </summary>
    public bool IncludeDocumentTitle { get; set; } = true;

    /// <summary>
    /// HeadingPath 포함 여부
    /// </summary>
    public bool IncludeHeadingPath { get; set; } = true;

    /// <summary>
    /// 프롬프트 캐싱 사용 여부
    /// </summary>
    public bool UsePromptCaching { get; set; } = true;
}
