using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Pluggable search provider for RAG evaluation.
/// Implement this interface to connect your retrieval system to evaluation jobs.
/// </summary>
public interface IEvaluationSearchProvider
{
    /// <summary>
    /// Retrieves relevant document chunks for the given query.
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="topK">Maximum number of chunks to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Retrieved document chunks ordered by relevance</returns>
    Task<IEnumerable<DocumentChunk>> RetrieveChunksAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an answer for the given query using the retrieved chunks.
    /// </summary>
    /// <param name="query">The original query</param>
    /// <param name="retrievedChunks">Chunks retrieved for the query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated answer text</returns>
    Task<string> GenerateAnswerAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Mock implementation of IEvaluationSearchProvider for testing
/// </summary>
public class MockEvaluationSearchProvider : IEvaluationSearchProvider
{
    /// <inheritdoc />
    public Task<IEnumerable<DocumentChunk>> RetrieveChunksAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < topK; i++)
        {
            chunks.Add(new DocumentChunk
            {
                Id = $"mock_chunk_{i}",
                Content = $"Mock content for query: {query.Substring(0, System.Math.Min(50, query.Length))}...",
                DocumentId = $"mock_doc_{i}",
                ChunkIndex = i,
                Embedding = new float[384]
            });
        }
        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    /// <inheritdoc />
    public Task<string> GenerateAnswerAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"Mock answer for: {query}");
    }
}
