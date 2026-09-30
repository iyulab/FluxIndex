using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Unified keyword search service interface combining BM25 and sparse retrieval capabilities.
/// Supports both in-memory and RDB-backed inverted index implementations.
/// </summary>
public interface IKeywordSearchService
{
    #region Search Operations

    /// <summary>
    /// Performs keyword search using BM25 ranking algorithm.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="options">Search options including max results, min score, and BM25 parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of search results ranked by BM25 score.</returns>
    Task<IReadOnlyList<KeywordSearchResult>> SearchAsync(
        string query,
        KeywordSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Index Management

    /// <summary>
    /// Indexes a single chunk for keyword search.
    /// </summary>
    /// <param name="chunk">The document chunk to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IndexChunkAsync(
        DocumentChunk chunk,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes multiple chunks for keyword search.
    /// </summary>
    /// <param name="chunks">The document chunks to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IndexChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a chunk from the keyword index.
    /// </summary>
    /// <param name="chunkId">The chunk ID to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every given chunk from the keyword index as one operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart of <see cref="IndexChunksAsync"/> for removal. A generation swap that drops a
    /// document's superseded chunks should call this once rather than <see cref="DeleteChunkAsync"/> per
    /// chunk: a relational index rewrites the document frequency of every term the chunks held, and doing
    /// that per chunk rewrites the same shared term rows once for every chunk that holds them — tens of
    /// thousands of writes where one pass over each distinct term does.
    /// </para>
    /// <para>
    /// Blank and unknown ids are ignored; a repeated id is removed once.
    /// </para>
    /// </remarks>
    /// <param name="chunkIds">The chunk ids to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteChunksAsync(
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of every chunk this index holds for <paramref name="documentId"/> — the keyword-index
    /// counterpart of <see cref="IVectorStore.GetChunkIdsByDocumentIdAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caller that replaces a document's rows captures the previous generation here, writes the new
    /// one, and deletes only what the new generation did not write again — on this leg by this leg's
    /// own ids. Reading the ids from the vector store instead assumes both legs key their rows
    /// identically, which nothing enforces: rows written before a store honoured caller ids never
    /// match, and each re-index then leaves the previous keyword generation in place.
    /// </para>
    /// <para>
    /// Answers from the index's own table; an unknown document yields an empty list.
    /// </para>
    /// </remarks>
    /// <param name="documentId">The document whose chunk ids to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<string>> GetChunkIdsByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all chunks for a document from the keyword index.
    /// </summary>
    /// <param name="documentId">The document ID whose chunks should be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every chunk this index holds for <paramref name="documentIds"/> with <paramref name="chunks"/> — the
    /// keyword-index counterpart of <see cref="IVectorStore.ReplaceDocumentsAsync"/>. A listed document with no chunk in
    /// <paramref name="chunks"/> is removed.
    /// </summary>
    /// <remarks>
    /// The relational backends do it in one transaction. The default implementation indexes the new chunks first and
    /// then removes the previous chunks that were not written again, so an interrupted call leaves a document duplicated
    /// rather than missing.
    /// </remarks>
    /// <param name="documentIds">The documents to replace. Blank ids are ignored.</param>
    /// <param name="chunks">The chunks the documents consist of from now on (may be empty).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    async Task ReplaceDocumentsAsync(
        IReadOnlyCollection<string> documentIds,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(documentIds);
        System.ArgumentNullException.ThrowIfNull(chunks);

        var previous = new List<string>();
        foreach (var documentId in documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(System.StringComparer.Ordinal))
            previous.AddRange(await GetChunkIdsByDocumentIdAsync(documentId, cancellationToken));

        if (chunks.Count > 0)
            await IndexChunksAsync(chunks, cancellationToken);

        var written = new HashSet<string>(chunks.Select(c => c.Id), System.StringComparer.Ordinal);
        var stale = previous.Where(id => !written.Contains(id)).ToList();
        if (stale.Count > 0)
            await DeleteChunksAsync(stale, cancellationToken);
    }

    /// <summary>
    /// Removes every chunk whose metadata matches <paramref name="filter"/>, and returns how many
    /// were removed. The filter uses the same vocabulary and match-any semantics as
    /// <c>IVectorStore.DeleteByFilterAsync</c>, so one filter object cleans both legs of a hybrid
    /// index symmetrically — without it, a caller purging a tenant can drop its vectors but has no
    /// primitive for its keyword rows except a delete-per-document loop.
    /// </summary>
    /// <param name="filter">
    /// Metadata key/value conditions, ANDed together. A collection value matches any of its elements.
    /// Only scalar metadata participates — see <see cref="KeywordSearchOptions.MetadataFilter"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of chunks removed.</returns>
    /// <exception cref="ArgumentException">The filter is empty — that would mean "delete everything",
    /// which is <see cref="ClearIndexAsync"/>'s job and too destructive to reach by accident.</exception>
    Task<int> DeleteByFilterAsync(
        IReadOnlyDictionary<string, object> filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves every chunk this index holds for <paramref name="oldDocumentId"/> to <paramref name="newDocumentId"/>,
    /// renaming each to the id <paramref name="chunkIdMap"/> gives it and applying <paramref name="metadataUpdates"/>
    /// to its metadata — the keyword-index counterpart of <see cref="IVectorStore.ReassignDocumentAsync"/>, with the
    /// same checks and the same meaning. The stored text is kept; an index may analyze it again so that fields scored
    /// from metadata follow <paramref name="metadataUpdates"/>.
    /// </summary>
    /// <remarks>
    /// The default implementation throws <see cref="NotSupportedException"/>; every index shipped with FluxIndex
    /// overrides it.
    /// </remarks>
    /// <param name="oldDocumentId">The document whose chunks move.</param>
    /// <param name="newDocumentId">The document id the chunks move to.</param>
    /// <param name="chunkIdMap">Old chunk id to new chunk id, covering every chunk this index holds for the old document.</param>
    /// <param name="metadataUpdates">Metadata keys to set on every moved chunk; a null value removes the key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of chunks moved; 0 when the index holds nothing for <paramref name="oldDocumentId"/>.</returns>
    /// <exception cref="ArgumentException">A stored chunk of the old document has no map entry, or the arguments are invalid.</exception>
    /// <exception cref="InvalidOperationException">The new document already has chunks, or a new chunk id is taken.</exception>
    Task<int> ReassignDocumentAsync(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support ReassignDocumentAsync.");

    /// <summary>
    /// Clears all data from the keyword index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearIndexAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Statistics and Maintenance

    /// <summary>
    /// Gets statistics about the keyword index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Index statistics including document count, term count, and average document length.</returns>
    Task<KeywordIndexStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the keyword index for better search performance.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OptimizeIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the IDF (Inverse Document Frequency) cache.
    /// Call this after significant index updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshIDFCacheAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Term Operations

    /// <summary>
    /// Gets the IDF value for a specific term.
    /// </summary>
    /// <param name="term">The term to look up.</param>
    /// <returns>IDF value (0 if term not found).</returns>
    double GetIDF(string term);

    /// <summary>
    /// Gets how many indexed chunks hold each term — the index's own document frequency, looked up in one call.
    /// </summary>
    /// <param name="terms">
    /// Index terms, as <see cref="Tokenize"/> produces them. Matched case-insensitively; a term the index does not
    /// hold maps to 0.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry per distinct term, keyed by the caller's spelling. Together with
    /// <see cref="KeywordIndexStatistics.TotalDocuments"/> from <see cref="GetStatisticsAsync"/> this gives a term's
    /// rarity without inverting <see cref="GetIDF"/> or running a search per term.
    /// </returns>
    Task<IReadOnlyDictionary<string, int>> GetDocumentFrequenciesAsync(
        IEnumerable<string> terms,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tokenizes text into terms for BM25 processing.
    /// </summary>
    /// <param name="text">Text to tokenize.</param>
    /// <returns>Collection of tokens.</returns>
    IEnumerable<string> Tokenize(string text);

    #endregion
}

/// <summary>
/// Result of a keyword search operation.
/// </summary>
public record KeywordSearchResult
{
    /// <summary>
    /// The matched document chunk.
    /// </summary>
    public required DocumentChunk Chunk { get; init; }

    /// <summary>
    /// BM25 relevance score.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Terms from the query that matched in this chunk.
    /// </summary>
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];

    /// <summary>
    /// Term frequency in the document for each matched term.
    /// </summary>
    public Dictionary<string, int> TermFrequencies { get; init; } = [];

    /// <summary>
    /// Document length in terms.
    /// </summary>
    public int DocumentLength { get; init; }
}

/// <summary>
/// Options for keyword search operations.
/// </summary>
public class KeywordSearchOptions
{
    /// <summary>
    /// Maximum number of results to return. Default: 10.
    /// </summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>
    /// Minimum BM25 score threshold. Default: 0.0.
    /// </summary>
    public double MinScore { get; set; }

    /// <summary>
    /// BM25 k1 parameter controlling term frequency saturation. Default: 1.2.
    /// Higher values increase the impact of term frequency.
    /// </summary>
    public double K1 { get; set; } = 1.2;

    /// <summary>
    /// BM25 b parameter controlling document length normalization. Default: 0.75.
    /// Higher values penalize longer documents more.
    /// </summary>
    public double B { get; set; } = 0.75;

    /// <summary>
    /// Enable term expansion using synonyms. Default: false.
    /// </summary>
    public bool EnableTermExpansion { get; set; }

    /// <summary>
    /// Enable phrase search for better precision. Default: false.
    /// </summary>
    public bool EnablePhraseSearch { get; set; }

    /// <summary>
    /// Filter results by document ID. Default: null (no filter).
    /// </summary>
    public string? DocumentIdFilter { get; set; }

    /// <summary>
    /// Restricts the search to chunks whose metadata matches every entry here. A collection value
    /// matches any of its elements, mirroring the vector store's payload filter, so a caller can hand
    /// the same filter object to both legs of a hybrid search. Default: null (no filter).
    /// An entry under <see cref="FilterKeys.DocumentId"/> matches the chunk's own document id — as it
    /// does in every vector store — so a chunk indexed without a metadata copy of its document id is
    /// still inside its document's scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The condition is pushed into the query, not applied to the results: without that, a scoped
    /// search over a shared index returns whatever survives filtering the global top N, so a tenant
    /// whose documents lose the global ranking race gets zero results for a query that matches its
    /// documents perfectly well. Filtering after truncation is a false-negative machine.
    /// </para>
    /// <para>
    /// Only scalar metadata is filterable — strings, numbers, booleans, and dates, plus collections
    /// of those. Values that are objects stay readable on the returned chunk but cannot be filtered
    /// on; there is no agreed way to compare them that holds across storage backends.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, object>? MetadataFilter { get; set; }
}

/// <summary>
/// Statistics about the keyword search index.
/// </summary>
public record KeywordIndexStatistics
{
    /// <summary>
    /// Total number of indexed documents.
    /// </summary>
    public long TotalDocuments { get; init; }

    /// <summary>
    /// Total number of unique terms in the index.
    /// </summary>
    public int TotalTerms { get; init; }

    /// <summary>
    /// Total number of term occurrences across all documents, counted over chunk bodies. Metadata
    /// fields scored beside the body (see <c>KeywordFieldOptions</c>) are not included.
    /// </summary>
    public long TotalTermOccurrences { get; init; }

    /// <summary>
    /// Average document length in terms.
    /// </summary>
    public double AverageDocumentLength { get; init; }

    /// <summary>
    /// Index size in bytes (for RDB-backed implementations).
    /// </summary>
    public long IndexSizeBytes { get; init; }

    /// <summary>
    /// Timestamp of last index optimization.
    /// </summary>
    public DateTime? LastOptimizedAt { get; init; }

    /// <summary>
    /// Most frequent terms in the index.
    /// </summary>
    public Dictionary<string, long> TopFrequentTerms { get; init; } = [];
}
