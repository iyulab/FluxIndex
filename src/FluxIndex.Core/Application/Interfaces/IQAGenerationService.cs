namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Service for generating QA (Question-Answer) pairs from document chunks.
/// Used for RAG evaluation dataset generation.
/// </summary>
public interface IQAGenerationService
{
    /// <summary>
    /// Generates QA pairs from a single chunk.
    /// </summary>
    /// <param name="chunkContent">The chunk text content.</param>
    /// <param name="maxPairs">Maximum number of QA pairs to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated QA pairs.</returns>
    Task<IReadOnlyList<GeneratedQAPair>> GenerateFromChunkAsync(
        string chunkContent,
        int maxPairs = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates QA pairs from multiple chunks.
    /// </summary>
    /// <param name="chunks">List of chunk contents with their IDs.</param>
    /// <param name="maxPairsPerChunk">Maximum QA pairs per chunk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated QA pairs grouped by chunk.</returns>
    Task<IReadOnlyList<ChunkQAPairs>> GenerateFromChunksBatchAsync(
        IReadOnlyList<ChunkInput> chunks,
        int maxPairsPerChunk = 3,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Input for chunk QA generation
/// </summary>
public class ChunkInput
{
    /// <summary>
    /// Chunk identifier
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Chunk text content
    /// </summary>
    public required string Content { get; init; }
}

/// <summary>
/// Generated QA pair
/// </summary>
public class GeneratedQAPair
{
    /// <summary>
    /// Generated question
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// Generated answer
    /// </summary>
    public required string Answer { get; init; }

    /// <summary>
    /// Context used for generation
    /// </summary>
    public required string Context { get; init; }

    /// <summary>
    /// Quality score (0-1) if evaluation was performed
    /// </summary>
    public double? QualityScore { get; init; }
}

/// <summary>
/// QA pairs for a specific chunk
/// </summary>
public class ChunkQAPairs
{
    /// <summary>
    /// Chunk identifier
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Generated QA pairs for this chunk
    /// </summary>
    public required IReadOnlyList<GeneratedQAPair> QAPairs { get; init; }
}

/// <summary>
/// Mock implementation for QA generation service.
/// Returns empty results when no LLM service is available.
/// </summary>
public class MockQAGenerationService : IQAGenerationService
{
    public Task<IReadOnlyList<GeneratedQAPair>> GenerateFromChunkAsync(
        string chunkContent,
        int maxPairs = 3,
        CancellationToken cancellationToken = default)
    {
        // Return empty list - no QA pairs generated without LLM
        return Task.FromResult<IReadOnlyList<GeneratedQAPair>>(Array.Empty<GeneratedQAPair>());
    }

    public Task<IReadOnlyList<ChunkQAPairs>> GenerateFromChunksBatchAsync(
        IReadOnlyList<ChunkInput> chunks,
        int maxPairsPerChunk = 3,
        CancellationToken cancellationToken = default)
    {
        // Return empty QA pairs for all chunks
        var results = chunks.Select(c => new ChunkQAPairs
        {
            ChunkId = c.ChunkId,
            QAPairs = Array.Empty<GeneratedQAPair>()
        }).ToList();

        return Task.FromResult<IReadOnlyList<ChunkQAPairs>>(results);
    }
}
