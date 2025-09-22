using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Service for BM25 (Best Matching 25) keyword-based ranking algorithm
/// </summary>
public interface IBM25Service
{
    /// <summary>
    /// Calculates BM25 score for a query-document pair
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="document">The document content</param>
    /// <param name="averageDocumentLength">Average length of documents in the corpus</param>
    /// <returns>BM25 relevance score</returns>
    float CalculateScore(string query, string document, float averageDocumentLength);

    /// <summary>
    /// Performs BM25 search across a collection of documents
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="topK">Number of top results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Top K documents ranked by BM25 score</returns>
    Task<IEnumerable<BM25Result>> SearchAsync(
        string query,
        int topK = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the IDF (Inverse Document Frequency) cache
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateIDFCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the IDF value for a term
    /// </summary>
    /// <param name="term">The term to lookup</param>
    /// <returns>IDF value for the term</returns>
    float GetIDF(string term);

    /// <summary>
    /// Tokenizes text into terms for BM25 processing
    /// </summary>
    /// <param name="text">Text to tokenize</param>
    /// <returns>List of tokens</returns>
    IEnumerable<string> Tokenize(string text);
}

/// <summary>
/// Represents a BM25 search result
/// </summary>
public class BM25Result
{
    /// <summary>
    /// Document identifier
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Chunk identifier
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// Content text
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// BM25 score
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// Document length
    /// </summary>
    public int DocumentLength { get; set; }

    /// <summary>
    /// Term frequencies
    /// </summary>
    public Dictionary<string, int> TermFrequencies { get; set; } = new();
    public Dictionary<string, object>? Metadata { get; set; }
}