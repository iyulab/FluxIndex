using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service interface for enriching document chunks with AI-generated metadata.
/// Integrates with FluxImprover pipeline for QA generation and semantic enrichment.
/// </summary>
public interface IChunkEnrichmentService
{
    /// <summary>
    /// Gets whether enrichment capabilities are available.
    /// Returns true if LLM service is configured and FluxImprover is enabled.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Enriches a batch of chunks with AI-generated metadata.
    /// </summary>
    /// <param name="chunks">Chunks to enrich.</param>
    /// <param name="document">Parent document for context.</param>
    /// <param name="options">Enrichment options.</param>
    /// <param name="progressCallback">Optional progress callback (processed, total).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enrichment result with statistics.</returns>
    Task<ChunkEnrichmentResult> EnrichChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        Document document,
        ChunkEnrichmentOptions? options = null,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for chunk enrichment.
/// </summary>
public class ChunkEnrichmentOptions
{
    /// <summary>
    /// Enable QA pair generation for each chunk.
    /// </summary>
    public bool GenerateQAPairs { get; set; } = true;

    /// <summary>
    /// Maximum number of QA pairs per chunk.
    /// </summary>
    public int MaxQAPairsPerChunk { get; set; } = 3;

    /// <summary>
    /// Enable keyword extraction.
    /// </summary>
    public bool ExtractKeywords { get; set; } = true;

    /// <summary>
    /// Enable summary generation for each chunk.
    /// </summary>
    public bool GenerateSummary { get; set; } = false;

    /// <summary>
    /// Enable RAG quality evaluation for generated QA pairs.
    /// </summary>
    public bool EvaluateQuality { get; set; } = false;

    /// <summary>
    /// Minimum quality score threshold (0-1). QA pairs below this are filtered.
    /// </summary>
    public double MinQualityScore { get; set; } = 0.6;
}

/// <summary>
/// Result of chunk enrichment operation.
/// </summary>
public class ChunkEnrichmentResult
{
    /// <summary>
    /// Total chunks processed.
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Successfully enriched chunks.
    /// </summary>
    public int EnrichedChunks { get; set; }

    /// <summary>
    /// Failed chunk count.
    /// </summary>
    public int FailedChunks { get; set; }

    /// <summary>
    /// Total QA pairs generated.
    /// </summary>
    public int TotalQAPairs { get; set; }

    /// <summary>
    /// Average quality score of generated QA pairs.
    /// </summary>
    public double? AverageQualityScore { get; set; }

    /// <summary>
    /// Processing duration in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Error messages for failed chunks.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Success rate (0-1).
    /// </summary>
    public double SuccessRate => TotalChunks > 0 ? (double)EnrichedChunks / TotalChunks : 0;
}
