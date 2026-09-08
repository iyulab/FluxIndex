using FluxIndex.Core.Domain.Models;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Optional capability for an <see cref="IVectorStore"/> that can fuse dense vector search with its
/// own already-populated keyword index (e.g. sqlite-vec + <c>chunk_fts</c>) in a single native call.
/// </summary>
/// <remarks>
/// When the registered vector store implements this, a hybrid search prefers it over
/// a separately-registered <see cref="IHybridSearchService"/>. The reason is a population gap:
/// <see cref="IHybridSearchService"/> requires an <see cref="IKeywordSearchService"/> whose index is
/// filled via <c>IndexChunkAsync</c> — a pipeline that writes only to the vector store never calls
/// it — whereas this native hybrid fuses over the keyword rows the store itself wrote at ingestion.
/// Routing to the native capability yields real hybrid results over indexed data with no second
/// index and no reindex.
/// </remarks>
public interface INativeHybridSearch
{
    /// <summary>
    /// Fuses dense vector search with the store's native keyword index and returns ranked results.
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector.</param>
    /// <param name="textQuery">Raw text query for the keyword side.</param>
    /// <param name="topK">Maximum results to return.</param>
    /// <param name="minScore">Minimum score threshold.</param>
    /// <param name="vectorWeight">Vector score weight (0.0–1.0); null uses the store's configured default.</param>
    /// <param name="filters">
    /// Metadata filter applied to <em>both</em> legs before fusion, with the same vocabulary as
    /// <see cref="IVectorStore.SearchAsync"/> (a collection value matches any of its members).
    /// A scoped hybrid request has to be answered by the fused ranking of the in-scope chunks —
    /// filtering the fused list afterwards would rank out-of-scope chunks first and starve the scope.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Fused results ordered by score.</returns>
    Task<IEnumerable<HybridSearchResult>> HybridSearchAsync(
        float[] queryEmbedding,
        string textQuery,
        int topK = 10,
        float minScore = 0.0f,
        float? vectorWeight = null,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default);
}
