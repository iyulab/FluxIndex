namespace FluxIndex.Domain.Entities;

/// <summary>
/// 검색 결과를 나타내는 엔터티
/// </summary>
public class SearchResult
{
    /// <summary>
    /// 검색 결과 ID (청크 ID)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 문서 ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 청크 내용
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 검색 점수 (유사도)
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// 청크 인덱스 (문서 내 순서)
    /// </summary>
    public int ChunkIndex { get; set; }
}