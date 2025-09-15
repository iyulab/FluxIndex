using FluxIndex.Domain.Entities;

namespace FluxIndex.Extensions.FileFlux.Interfaces;

/// <summary>
/// Smart indexer that optimizes indexing based on chunk characteristics
/// </summary>
public interface ISmartIndexer
{
    /// <summary>
    /// Index document with strategy-specific optimizations
    /// </summary>
    Task IndexWithStrategyAsync(
        Document document, 
        IndexingStrategy strategy,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Batch index documents with the same strategy
    /// </summary>
    Task BatchIndexAsync(
        IEnumerable<Document> documents,
        IndexingStrategy strategy,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update indexing configuration based on chunk characteristics
    /// </summary>
    Task UpdateIndexingConfigAsync(
        ChunkingMetadata metadata,
        CancellationToken cancellationToken = default);
}