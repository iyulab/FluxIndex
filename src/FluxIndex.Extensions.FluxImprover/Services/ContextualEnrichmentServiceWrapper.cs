using FluxImprover.ContextualRetrieval;
using FluxImprover.Models;
using FluxImprover.Options;
using FluxIndex.Extensions.FluxImprover.Adapters;
using FluxIndexChunk = FluxIndex.Core.Application.Interfaces.IEnrichedChunk;

namespace FluxIndex.Extensions.FluxImprover.Services;

/// <summary>
/// Wraps FluxImprover's ContextualEnrichmentService to work with FluxIndex IEnrichedChunk.
/// Implements Anthropic's Contextual Retrieval pattern which reduces failed retrievals by 49% (67% with reranking).
/// </summary>
/// <remarks>
/// <para>
/// Contextual Retrieval prepends LLM-generated context to each chunk before embedding,
/// providing document-level context that improves semantic search accuracy.
/// </para>
/// <para>
/// Reference: https://www.anthropic.com/news/contextual-retrieval
/// </para>
/// </remarks>
public sealed class ContextualEnrichmentServiceWrapper
{
    private readonly IContextualEnrichmentService _contextualEnrichmentService;

    /// <summary>
    /// Creates a new wrapper around FluxImprover's ContextualEnrichmentService.
    /// </summary>
    /// <param name="contextualEnrichmentService">The FluxImprover contextual enrichment service to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when contextualEnrichmentService is null.</exception>
    public ContextualEnrichmentServiceWrapper(IContextualEnrichmentService contextualEnrichmentService)
    {
        _contextualEnrichmentService = contextualEnrichmentService
            ?? throw new ArgumentNullException(nameof(contextualEnrichmentService));
    }

    /// <summary>
    /// Enriches a FluxIndex chunk with document-level context.
    /// </summary>
    /// <param name="fluxIndexChunk">The FluxIndex chunk to enrich.</param>
    /// <param name="fullDocumentText">The full document text for context extraction.</param>
    /// <param name="options">Optional contextual enrichment options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A contextual chunk with LLM-generated context summary.</returns>
    public async Task<ContextualChunk> EnrichAsync(
        FluxIndexChunk fluxIndexChunk,
        string fullDocumentText,
        ContextualEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fluxIndexChunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullDocumentText);

        // Convert FluxIndex chunk to FluxImprover chunk format
        var adapter = new EnrichedChunkAdapter(fluxIndexChunk);
        var inputChunk = CreateChunkFromAdapter(adapter);

        // Enrich using FluxImprover service
        var enrichedResult = await _contextualEnrichmentService.EnrichAsync(
            inputChunk,
            fullDocumentText,
            options,
            cancellationToken);

        // Merge FluxIndex metadata with enriched result
        return MergeWithFluxIndexMetadata(enrichedResult, adapter);
    }

    /// <summary>
    /// Enriches multiple FluxIndex chunks from the same document with document-level context.
    /// </summary>
    /// <param name="fluxIndexChunks">The FluxIndex chunks to enrich (must be from the same document).</param>
    /// <param name="fullDocumentText">The full document text for context extraction.</param>
    /// <param name="options">Optional contextual enrichment options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Contextual chunks with LLM-generated context summaries.</returns>
    public async Task<IReadOnlyList<ContextualChunk>> EnrichBatchAsync(
        IEnumerable<FluxIndexChunk> fluxIndexChunks,
        string fullDocumentText,
        ContextualEnrichmentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fluxIndexChunks);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullDocumentText);

        var chunkList = fluxIndexChunks.ToList();
        if (chunkList.Count == 0)
        {
            return Array.Empty<ContextualChunk>();
        }

        // Convert FluxIndex chunks to FluxImprover chunks
        var adapters = chunkList.Select(c => new EnrichedChunkAdapter(c)).ToList();
        var inputChunks = adapters.Select(CreateChunkFromAdapter).ToList();

        // Enrich using FluxImprover service (handles parallelization internally)
        var enrichedResults = await _contextualEnrichmentService.EnrichBatchAsync(
            inputChunks,
            fullDocumentText,
            options,
            cancellationToken);

        // Merge FluxIndex metadata with enriched results
        var results = new List<ContextualChunk>(enrichedResults.Count);
        for (int i = 0; i < enrichedResults.Count; i++)
        {
            results.Add(MergeWithFluxIndexMetadata(enrichedResults[i], adapters[i]));
        }

        return results;
    }

    /// <summary>
    /// Gets the contextualized text for embedding.
    /// This prepends the context summary to the original chunk text.
    /// </summary>
    /// <param name="contextualChunk">The contextual chunk.</param>
    /// <returns>Text optimized for embedding with document context.</returns>
    public static string GetContextualizedTextForEmbedding(ContextualChunk contextualChunk)
    {
        return contextualChunk.GetContextualizedText();
    }

    /// <summary>
    /// Creates a FluxImprover Chunk from the EnrichedChunkAdapter.
    /// </summary>
    private static Chunk CreateChunkFromAdapter(EnrichedChunkAdapter adapter)
    {
        var metadata = adapter.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, object>();

        // Add position info from underlying chunk if available
        var underlying = adapter.UnderlyingChunk;
        metadata["position"] = underlying.ChunkIndex;
        metadata["sourceId"] = adapter.SourceId;

        if (!string.IsNullOrEmpty(adapter.HeadingPath))
        {
            metadata["headingPath"] = adapter.HeadingPath;
        }

        return new Chunk
        {
            Id = adapter.Id,
            Content = adapter.Text,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Merges FluxImprover contextual result with FluxIndex metadata.
    /// </summary>
    private static ContextualChunk MergeWithFluxIndexMetadata(
        ContextualChunk enrichedResult,
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

        return new ContextualChunk
        {
            Id = adapter.Id,
            Text = adapter.Text,
            SourceId = adapter.SourceId,
            ContextSummary = enrichedResult.ContextSummary,
            HeadingPath = adapter.HeadingPath,
            Position = enrichedResult.Position,
            TotalChunks = enrichedResult.TotalChunks,
            Metadata = mergedMetadata
        };
    }
}
