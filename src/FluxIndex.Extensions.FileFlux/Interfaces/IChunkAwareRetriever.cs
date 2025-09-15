using FluxIndex.Domain.Entities;

namespace FluxIndex.Extensions.FileFlux.Interfaces;

/// <summary>
/// Retriever optimized for FileFlux chunking characteristics
/// </summary>
public interface IChunkAwareRetriever
{
    /// <summary>
    /// Retrieve documents with chunk-aware optimizations
    /// </summary>
    Task<IEnumerable<Document>> RetrieveAsync(
        string query,
        ChunkingHint hint,
        int topK = 10,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update default retrieval hint based on indexed chunks
    /// </summary>
    void UpdateDefaultHint(ChunkingHint hint);
    
    /// <summary>
    /// Get adjacent chunks for context expansion
    /// </summary>
    Task<IEnumerable<Document>> GetAdjacentChunksAsync(
        Document document,
        int contextWindow = 1,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Hints for optimizing retrieval based on chunking characteristics
/// </summary>
public class ChunkingHint
{
    public string Strategy { get; set; } = "Auto";
    public bool ExpandWithOverlap { get; set; } = true;
    public bool RequiresReranking { get; set; } = true;
    public double MinQualityScore { get; set; } = 0.5;
    public int TopK { get; set; } = 20;
    public int OverlapSize { get; set; } = 50;
    public bool UseSemanticSearch { get; set; } = true;
    public bool UseKeywordSearch { get; set; } = true;
}