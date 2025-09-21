namespace FluxIndex.Domain.Models;

/// <summary>
/// 랭킹된 검색 결과
/// </summary>
public class RankedResult
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public int Rank { get; set; }
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}