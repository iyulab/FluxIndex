using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Late Chunking Embedding Service implementing Jina AI's approach.
/// Generates embeddings for the full document first, then derives chunk embeddings
/// from the full document representation, preserving more contextual information.
/// </summary>
public partial class LateChunkingEmbeddingService : ILateChunkingEmbeddingService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly LateChunkingOptions _options;
    private readonly ILogger<LateChunkingEmbeddingService> _logger;

    public LateChunkingEmbeddingService(
        IEmbeddingService embeddingService,
        IOptions<LateChunkingOptions> options,
        ILogger<LateChunkingEmbeddingService> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LateChunkingResult> GenerateLateChunkingEmbeddingsAsync(
        string documentContent,
        IReadOnlyList<ChunkBoundary> chunkBoundaries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentContent);
        ArgumentNullException.ThrowIfNull(chunkBoundaries);

        if (chunkBoundaries.Count == 0)
        {
            return new LateChunkingResult
            {
                DocumentEmbedding = null,
                ChunkEmbeddings = Array.Empty<ChunkEmbeddingInfo>()
            };
        }

        LogLateChunkingEmbedding5(_logger, chunkBoundaries.Count);

        // Strategy selection based on document length
        if (documentContent.Length <= _options.MaxDocumentLength)
        {
            return await GenerateWithFullDocumentAsync(documentContent, chunkBoundaries, cancellationToken);
        }
        else
        {
            return await GenerateWithSlidingWindowAsync(documentContent, chunkBoundaries, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkEmbeddingInfo>> GenerateChunkEmbeddingsWithContextAsync(
        IReadOnlyList<IEnrichedChunk> chunks,
        int contextWindowSize = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count == 0)
            return Array.Empty<ChunkEmbeddingInfo>();

        if (_logger.IsEnabled(LogLevel.Information))
            LogLateChunkingEmbedding4(_logger, contextWindowSize, chunks.Count);

        var results = new List<ChunkEmbeddingInfo>();

        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = chunks[i];
            var contextualContent = BuildContextualContent(chunks, i, contextWindowSize);

            // Generate embedding for the contextualized content
            var embeddingValues = await _embeddingService.GenerateEmbeddingAsync(contextualContent, cancellationToken);
            var embedding = new EmbeddingVector(embeddingValues, _embeddingService.GetModelName());

            results.Add(new ChunkEmbeddingInfo
            {
                ChunkId = chunk.ChunkId,
                ChunkIndex = i,
                OriginalContent = chunk.Content,
                ContextualContent = contextualContent,
                Embedding = embedding,
                ContextWindowUsed = contextWindowSize,
                PrecedingChunksIncluded = Math.Min(i, contextWindowSize),
                FollowingChunksIncluded = Math.Min(chunks.Count - i - 1, contextWindowSize)
            });
        }

        LogLateChunkingEmbedding3(_logger, results.Count);

        return results.AsReadOnly();
    }

    /// <summary>
    /// Generates embeddings using the full document approach
    /// For documents within the embedding model's context window
    /// </summary>
    private async Task<LateChunkingResult> GenerateWithFullDocumentAsync(
        string documentContent,
        IReadOnlyList<ChunkBoundary> chunkBoundaries,
        CancellationToken cancellationToken)
    {
        LogLateChunkingEmbedding2(_logger);

        var modelName = _embeddingService.GetModelName();

        // Step 1: Generate full document embedding
        var documentEmbeddingValues = await _embeddingService.GenerateEmbeddingAsync(documentContent, cancellationToken);
        var documentEmbedding = new EmbeddingVector(documentEmbeddingValues, modelName);

        // Step 2: Generate individual chunk embeddings with document context
        var chunkEmbeddings = new List<ChunkEmbeddingInfo>();

        foreach (var boundary in chunkBoundaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkContent = documentContent.Substring(boundary.StartPosition, boundary.Length);

            // Combine chunk with document context markers
            var contextualContent = _options.ContextIntegrationMode switch
            {
                ContextIntegrationMode.PrependSummary => $"[Document context follows]\n{chunkContent}",
                ContextIntegrationMode.WeightedCombination => chunkContent,
                ContextIntegrationMode.SurroundingContext => GetSurroundingContext(documentContent, boundary),
                _ => chunkContent
            };

            var chunkEmbeddingValues = await _embeddingService.GenerateEmbeddingAsync(contextualContent, cancellationToken);
            var chunkEmbedding = new EmbeddingVector(chunkEmbeddingValues, modelName);

            // Apply weighted combination with document embedding if configured
            var finalEmbedding = _options.ContextIntegrationMode == ContextIntegrationMode.WeightedCombination
                ? CombineEmbeddings(chunkEmbedding, documentEmbedding, _options.DocumentContextWeight)
                : chunkEmbedding;

            chunkEmbeddings.Add(new ChunkEmbeddingInfo
            {
                ChunkId = boundary.ChunkId,
                ChunkIndex = boundary.Index,
                OriginalContent = chunkContent,
                ContextualContent = contextualContent,
                Embedding = finalEmbedding,
                ContextWindowUsed = 0, // Full document context
                DocumentContextApplied = true
            });
        }

        return new LateChunkingResult
        {
            DocumentEmbedding = documentEmbedding,
            ChunkEmbeddings = chunkEmbeddings.AsReadOnly()
        };
    }

    /// <summary>
    /// Generates embeddings using sliding window for long documents
    /// </summary>
    private async Task<LateChunkingResult> GenerateWithSlidingWindowAsync(
        string documentContent,
        IReadOnlyList<ChunkBoundary> chunkBoundaries,
        CancellationToken cancellationToken)
    {
        LogLateChunkingEmbedding1(_logger);

        var modelName = _embeddingService.GetModelName();
        var chunkEmbeddings = new List<ChunkEmbeddingInfo>();

        foreach (var boundary in chunkBoundaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Calculate window boundaries
            var windowStart = Math.Max(0, boundary.StartPosition - _options.WindowOverlap);
            var windowEnd = Math.Min(documentContent.Length, boundary.EndPosition + _options.WindowOverlap);
            var windowContent = documentContent.Substring(windowStart, windowEnd - windowStart);

            var chunkContent = documentContent.Substring(boundary.StartPosition, boundary.Length);

            // Generate embedding with surrounding context
            var embeddingValues = await _embeddingService.GenerateEmbeddingAsync(windowContent, cancellationToken);
            var embedding = new EmbeddingVector(embeddingValues, modelName);

            chunkEmbeddings.Add(new ChunkEmbeddingInfo
            {
                ChunkId = boundary.ChunkId,
                ChunkIndex = boundary.Index,
                OriginalContent = chunkContent,
                ContextualContent = windowContent,
                Embedding = embedding,
                ContextWindowUsed = _options.WindowOverlap,
                DocumentContextApplied = false
            });
        }

        // Generate approximate document embedding from chunk embeddings
        var validEmbeddings = chunkEmbeddings.Where(c => c.Embedding != null).Select(c => c.Embedding!).ToList();
        var documentEmbedding = AverageEmbeddings(validEmbeddings);

        return new LateChunkingResult
        {
            DocumentEmbedding = documentEmbedding,
            ChunkEmbeddings = chunkEmbeddings.AsReadOnly()
        };
    }

    private string GetSurroundingContext(string documentContent, ChunkBoundary boundary)
    {
        var contextStart = Math.Max(0, boundary.StartPosition - _options.SurroundingContextSize);
        var contextEnd = Math.Min(documentContent.Length, boundary.EndPosition + _options.SurroundingContextSize);

        var beforeContext = documentContent.Substring(contextStart, boundary.StartPosition - contextStart);
        var chunkContent = documentContent.Substring(boundary.StartPosition, boundary.Length);
        var afterContext = documentContent.Substring(boundary.EndPosition, contextEnd - boundary.EndPosition);

        return $"{beforeContext}[[{chunkContent}]]{afterContext}";
    }

    private static string BuildContextualContent(IReadOnlyList<IEnrichedChunk> chunks, int currentIndex, int windowSize)
    {
        var parts = new List<string>();

        // Add preceding chunks
        var startIndex = Math.Max(0, currentIndex - windowSize);
        for (int i = startIndex; i < currentIndex; i++)
        {
            parts.Add($"[Preceding] {chunks[i].Content}");
        }

        // Add current chunk (emphasized)
        parts.Add($"[Current] {chunks[currentIndex].Content}");

        // Add following chunks
        var endIndex = Math.Min(chunks.Count - 1, currentIndex + windowSize);
        for (int i = currentIndex + 1; i <= endIndex; i++)
        {
            parts.Add($"[Following] {chunks[i].Content}");
        }

        return string.Join("\n\n", parts);
    }

    private static EmbeddingVector CombineEmbeddings(EmbeddingVector chunkEmbedding, EmbeddingVector documentEmbedding, double documentWeight)
    {
        var chunkWeight = 1.0 - documentWeight;
        var combinedValues = new float[chunkEmbedding.Dimension];

        for (int i = 0; i < chunkEmbedding.Dimension; i++)
        {
            combinedValues[i] = (float)(chunkEmbedding.Values[i] * chunkWeight + documentEmbedding.Values[i] * documentWeight);
        }

        // Normalize the combined vector
        var magnitude = Math.Sqrt(combinedValues.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (int i = 0; i < combinedValues.Length; i++)
            {
                combinedValues[i] /= (float)magnitude;
            }
        }

        return new EmbeddingVector(combinedValues, chunkEmbedding.ModelName);
    }

    private static EmbeddingVector? AverageEmbeddings(List<EmbeddingVector> embeddings)
    {
        if (embeddings.Count == 0)
            return null;

        var dimension = embeddings[0].Dimension;
        var modelName = embeddings[0].ModelName;
        var averaged = new float[dimension];

        foreach (var embedding in embeddings)
        {
            for (int i = 0; i < dimension; i++)
            {
                averaged[i] += embedding.Values[i];
            }
        }

        for (int i = 0; i < dimension; i++)
        {
            averaged[i] /= embeddings.Count;
        }

        // Normalize
        var magnitude = Math.Sqrt(averaged.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (int i = 0; i < averaged.Length; i++)
            {
                averaged[i] /= (float)magnitude;
            }
        }

        return new EmbeddingVector(averaged, modelName);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating late chunking embeddings for document with {ChunkCount} chunks")]
    private static partial void LogLateChunkingEmbedding5(ILogger logger, int chunkCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Generating chunk embeddings with context window {WindowSize} for {Count} chunks")]
    private static partial void LogLateChunkingEmbedding4(ILogger logger, int windowSize, int count);
    [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} chunk embeddings with context")]
    private static partial void LogLateChunkingEmbedding3(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Using full document approach for late chunking")]
    private static partial void LogLateChunkingEmbedding2(ILogger logger);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Using sliding window approach for late chunking (document too long)")]
    private static partial void LogLateChunkingEmbedding1(ILogger logger);

    #endregion
}

/// <summary>
/// Interface for late chunking embedding generation
/// </summary>
public interface ILateChunkingEmbeddingService
{
    /// <summary>
    /// Generates embeddings using late chunking approach
    /// </summary>
    Task<LateChunkingResult> GenerateLateChunkingEmbeddingsAsync(
        string documentContent,
        IReadOnlyList<ChunkBoundary> chunkBoundaries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates chunk embeddings with surrounding context
    /// </summary>
    Task<IReadOnlyList<ChunkEmbeddingInfo>> GenerateChunkEmbeddingsWithContextAsync(
        IReadOnlyList<IEnrichedChunk> chunks,
        int contextWindowSize = 2,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of late chunking embedding generation
/// </summary>
public partial class LateChunkingResult
{
    /// <summary>
    /// Full document embedding
    /// </summary>
    public EmbeddingVector? DocumentEmbedding { get; init; }

    /// <summary>
    /// Individual chunk embeddings with context
    /// </summary>
    public IReadOnlyList<ChunkEmbeddingInfo> ChunkEmbeddings { get; init; } = Array.Empty<ChunkEmbeddingInfo>();
}

/// <summary>
/// Information about a chunk's embedding
/// </summary>
public partial class ChunkEmbeddingInfo
{
    /// <summary>
    /// Chunk identifier
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Index of the chunk in the document
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Original chunk content
    /// </summary>
    public string OriginalContent { get; init; } = string.Empty;

    /// <summary>
    /// Content with context applied
    /// </summary>
    public string ContextualContent { get; init; } = string.Empty;

    /// <summary>
    /// Generated embedding
    /// </summary>
    public EmbeddingVector? Embedding { get; init; }

    /// <summary>
    /// Size of context window used
    /// </summary>
    public int ContextWindowUsed { get; init; }

    /// <summary>
    /// Number of preceding chunks included
    /// </summary>
    public int PrecedingChunksIncluded { get; init; }

    /// <summary>
    /// Number of following chunks included
    /// </summary>
    public int FollowingChunksIncluded { get; init; }

    /// <summary>
    /// Whether document-level context was applied
    /// </summary>
    public bool DocumentContextApplied { get; init; }
}

/// <summary>
/// Represents chunk boundaries in a document
/// </summary>
public partial class ChunkBoundary
{
    /// <summary>
    /// Chunk identifier
    /// </summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Index of the chunk
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Start position in document
    /// </summary>
    public int StartPosition { get; init; }

    /// <summary>
    /// End position in document
    /// </summary>
    public int EndPosition { get; init; }

    /// <summary>
    /// Length of the chunk
    /// </summary>
    public int Length => EndPosition - StartPosition;
}

/// <summary>
/// How to integrate document context with chunk embeddings
/// </summary>
public enum ContextIntegrationMode
{
    /// <summary>
    /// Prepend a document summary to each chunk
    /// </summary>
    PrependSummary,

    /// <summary>
    /// Weighted combination of chunk and document embeddings
    /// </summary>
    WeightedCombination,

    /// <summary>
    /// Include surrounding text from the document
    /// </summary>
    SurroundingContext
}

/// <summary>
/// Options for late chunking embedding
/// </summary>
public partial class LateChunkingOptions
{
    /// <summary>
    /// Maximum document length for full document processing
    /// </summary>
    public int MaxDocumentLength { get; set; } = 8000;

    /// <summary>
    /// Overlap for sliding window approach
    /// </summary>
    public int WindowOverlap { get; set; } = 200;

    /// <summary>
    /// Size of surrounding context to include
    /// </summary>
    public int SurroundingContextSize { get; set; } = 500;

    /// <summary>
    /// How to integrate document context
    /// </summary>
    public ContextIntegrationMode ContextIntegrationMode { get; set; } = ContextIntegrationMode.SurroundingContext;

    /// <summary>
    /// Weight for document context in weighted combination (0.0 - 1.0)
    /// </summary>
    public double DocumentContextWeight { get; set; } = 0.3;
}
