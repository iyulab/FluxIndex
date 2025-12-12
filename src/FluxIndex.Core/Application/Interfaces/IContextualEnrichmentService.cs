namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Service for contextual enrichment of document chunks (Anthropic Contextual Retrieval pattern).
/// Adds document-level context to each chunk before embedding for 49-67% better retrieval accuracy.
/// </summary>
/// <remarks>
/// Reference: https://www.anthropic.com/news/contextual-retrieval
/// </remarks>
public interface IContextualEnrichmentService
{
    /// <summary>
    /// Enriches a single chunk with document-level context.
    /// </summary>
    /// <param name="chunkContent">The chunk text content.</param>
    /// <param name="fullDocumentText">The full document text for context extraction.</param>
    /// <param name="chunkIndex">The chunk index in the document.</param>
    /// <param name="totalChunks">Total number of chunks in the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Context summary to prepend to the chunk for embedding.</returns>
    Task<string> GenerateContextAsync(
        string chunkContent,
        string fullDocumentText,
        int chunkIndex,
        int totalChunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enriches multiple chunks from the same document with document-level context.
    /// </summary>
    /// <param name="chunks">List of chunk contents.</param>
    /// <param name="fullDocumentText">The full document text for context extraction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Context summaries for each chunk (in same order as input).</returns>
    Task<IReadOnlyList<string>> GenerateContextBatchAsync(
        IReadOnlyList<string> chunks,
        string fullDocumentText,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Mock implementation for contextual enrichment service.
/// Returns empty context when no LLM service is available.
/// </summary>
public class MockContextualEnrichmentService : IContextualEnrichmentService
{
    public Task<string> GenerateContextAsync(
        string chunkContent,
        string fullDocumentText,
        int chunkIndex,
        int totalChunks,
        CancellationToken cancellationToken = default)
    {
        // Return empty context - chunk will be embedded without additional context
        return Task.FromResult(string.Empty);
    }

    public Task<IReadOnlyList<string>> GenerateContextBatchAsync(
        IReadOnlyList<string> chunks,
        string fullDocumentText,
        CancellationToken cancellationToken = default)
    {
        // Return empty contexts for all chunks
        var emptyContexts = new string[chunks.Count];
        Array.Fill(emptyContexts, string.Empty);
        return Task.FromResult<IReadOnlyList<string>>(emptyContexts);
    }
}
