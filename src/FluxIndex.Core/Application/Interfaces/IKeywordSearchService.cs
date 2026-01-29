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
    /// Removes all chunks for a document from the keyword index.
    /// </summary>
    /// <param name="documentId">The document ID whose chunks should be removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default);

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
    public double MinScore { get; set; } = 0.0;

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
    public bool EnableTermExpansion { get; set; } = false;

    /// <summary>
    /// Enable phrase search for better precision. Default: false.
    /// </summary>
    public bool EnablePhraseSearch { get; set; } = false;

    /// <summary>
    /// Filter results by document ID. Default: null (no filter).
    /// </summary>
    public string? DocumentIdFilter { get; set; }
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
    /// Total number of term occurrences across all documents.
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
