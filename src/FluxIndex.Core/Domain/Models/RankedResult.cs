namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// Ranked search result used for rank fusion algorithms.
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

    /// <summary>
    /// Creates a unique key for deduplication.
    /// </summary>
    public string GetUniqueKey() => $"{DocumentId}:{ChunkId}";
}
