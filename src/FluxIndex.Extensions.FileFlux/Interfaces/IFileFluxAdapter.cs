using FluxIndex.Core.Domain.Entities;
using FluxIndex.Extensions.FileFlux.Retrieval;
using FluxIndex.Extensions.FileFlux.Pipeline;

namespace FluxIndex.Extensions.FileFlux.Interfaces;

/// <summary>
/// Adapter for processing FileFlux output without direct dependency
/// </summary>
public interface IFileFluxAdapter
{
    /// <summary>
    /// Adapt FileFlux chunks to FluxIndex documents
    /// </summary>
    Task<IEnumerable<Document>> AdaptChunksAsync(
        dynamic fileFluxChunks, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Extract metadata from a FileFlux chunk
    /// </summary>
    ChunkingMetadata ExtractMetadata(dynamic chunk);
    
    /// <summary>
    /// Determine optimal indexing strategy based on chunk characteristics
    /// </summary>
    IndexingStrategy DetermineStrategy(ChunkingMetadata metadata);
}

/// <summary>
/// Metadata extracted from FileFlux chunks
/// </summary>
public class ChunkingMetadata
{
    public string? ChunkingStrategy { get; set; }
    public int ChunkIndex { get; set; }
    public int ChunkSize { get; set; }
    public int? OverlapSize { get; set; }
    public double? QualityScore { get; set; }
    public double? BoundaryQuality { get; set; }
    public double? Completeness { get; set; }
    public string? SourceFile { get; set; }
    public string? FileType { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// Indexing strategy for optimized storage
/// </summary>
public enum IndexingStrategy
{
    Standard,
    HighQuality,
    Semantic,
    Hybrid,
    Keyword,
    Compressed
}


/// <summary>
/// Interface for FileFlux integration pipeline
/// </summary>
public interface IFileFluxIntegrationPipeline
{
    /// <summary>
    /// Process FileFlux output through the complete pipeline
    /// </summary>
    Task<PipelineResult> ProcessFileFluxOutputAsync(
        dynamic fileFluxOutput,
        PipelineOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Process multiple FileFlux outputs in batch
    /// </summary>
    Task<BatchPipelineResult> ProcessBatchAsync(
        IEnumerable<dynamic> fileFluxBatches,
        BatchOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimize pipeline based on performance metrics
    /// </summary>
    Task<OptimizationResult> OptimizePipelineAsync(
        PipelineMetrics metrics,
        CancellationToken cancellationToken = default);
}