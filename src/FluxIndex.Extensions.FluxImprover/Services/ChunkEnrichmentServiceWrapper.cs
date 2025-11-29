using FluxImprover.Models;
using FluxImprover.Options;
using FluxImprover.Enrichment;
using FluxIndex.Extensions.FluxImprover.Adapters;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;
using FluxImproverChunk = FluxImprover.Models.IEnrichedChunk;

namespace FluxIndex.Extensions.FluxImprover.Services;

/// <summary>
/// Wraps FluxImprover's ChunkEnrichmentService to work with FluxIndex IEnrichedChunk.
/// Provides LLM-powered summarization and keyword extraction for FluxIndex chunks.
/// </summary>
public sealed class ChunkEnrichmentServiceWrapper
{
    private readonly ChunkEnrichmentService _enrichmentService;

    /// <summary>
    /// Creates a new wrapper around FluxImprover's ChunkEnrichmentService.
    /// </summary>
    /// <param name="enrichmentService">The FluxImprover enrichment service to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when enrichmentService is null.</exception>
    public ChunkEnrichmentServiceWrapper(ChunkEnrichmentService enrichmentService)
    {
        _enrichmentService = enrichmentService ?? throw new ArgumentNullException(nameof(enrichmentService));
    }

    /// <summary>
    /// Enriches a FluxIndex chunk with LLM-generated summary and keywords.
    /// </summary>
    /// <param name="fluxIndexChunk">The FluxIndex chunk to enrich.</param>
    /// <param name="options">Optional enrichment options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An enriched chunk with summary and keywords, preserving FluxIndex metadata.</returns>
    public async Task<FluxImproverChunk> EnrichAsync(
        FluxIndexChunk fluxIndexChunk,
        EnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fluxIndexChunk);

        // Convert FluxIndex chunk to FluxImprover chunk format
        var adapter = new EnrichedChunkAdapter(fluxIndexChunk);
        var inputChunk = CreateChunkFromAdapter(adapter);

        // Enrich using FluxImprover service
        var enrichedResult = await _enrichmentService.EnrichAsync(inputChunk, options, cancellationToken);

        // Merge FluxIndex metadata with enriched result
        return MergeWithFluxIndexMetadata(enrichedResult, adapter);
    }

    /// <summary>
    /// Enriches multiple FluxIndex chunks in batch.
    /// </summary>
    /// <param name="fluxIndexChunks">The FluxIndex chunks to enrich.</param>
    /// <param name="options">Optional enrichment options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enriched chunks with summaries and keywords.</returns>
    public async Task<IReadOnlyList<FluxImproverChunk>> EnrichBatchAsync(
        IEnumerable<FluxIndexChunk> fluxIndexChunks,
        EnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FluxImproverChunk>();

        foreach (var chunk in fluxIndexChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enriched = await EnrichAsync(chunk, options, cancellationToken);
            results.Add(enriched);
        }

        return results;
    }

    /// <summary>
    /// Creates a FluxImprover Chunk from the EnrichedChunkAdapter.
    /// </summary>
    private static Chunk CreateChunkFromAdapter(EnrichedChunkAdapter adapter)
    {
        return new Chunk
        {
            Id = adapter.Id,
            Content = adapter.Text,
            Metadata = adapter.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    /// <summary>
    /// Merges FluxImprover enriched result with FluxIndex metadata.
    /// </summary>
    private static FluxImproverChunk MergeWithFluxIndexMetadata(
        EnrichedChunk enrichedResult,
        EnrichedChunkAdapter adapter)
    {
        // Merge metadata from both sources
        var mergedMetadata = new Dictionary<string, object>();

        // Add FluxIndex metadata first
        if (adapter.Metadata != null)
        {
            foreach (var kvp in adapter.Metadata)
            {
                mergedMetadata[kvp.Key] = kvp.Value;
            }
        }

        // Add any additional metadata from enrichment result
        if (enrichedResult.Metadata != null)
        {
            foreach (var kvp in enrichedResult.Metadata)
            {
                if (!mergedMetadata.ContainsKey(kvp.Key))
                {
                    mergedMetadata[kvp.Key] = kvp.Value;
                }
            }
        }

        return new EnrichedChunk
        {
            Id = adapter.Id,
            Text = adapter.Text,
            SourceId = adapter.SourceId,
            HeadingPath = adapter.HeadingPath,
            Summary = enrichedResult.Summary,
            Keywords = enrichedResult.Keywords,
            Metadata = mergedMetadata
        };
    }
}
