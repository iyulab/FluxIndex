using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Decorator that adds quantization support to any IVectorStore implementation.
/// Stores quantized embeddings alongside original embeddings for flexible search strategies.
/// </summary>
public partial class QuantizedVectorStoreDecorator : IQuantizedVectorStore
{
    private readonly IVectorStore _innerStore;
    private readonly IVectorQuantizer _quantizer;
    private readonly ILogger<QuantizedVectorStoreDecorator> _logger;
    private readonly ConcurrentDictionary<string, QuantizedVector> _quantizedEmbeddings;
    private readonly QuantizedVectorStoreOptions _options;

    public QuantizedVectorStoreDecorator(
        IVectorStore innerStore,
        IVectorQuantizer quantizer,
        ILogger<QuantizedVectorStoreDecorator> logger,
        QuantizedVectorStoreOptions? options = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _quantizer = quantizer ?? throw new ArgumentNullException(nameof(quantizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _quantizedEmbeddings = new ConcurrentDictionary<string, QuantizedVector>();
        _options = options ?? new QuantizedVectorStoreOptions();
    }

    public IVectorQuantizer? Quantizer => _quantizer;
    public bool SupportsQuantization => true;

    #region IVectorStore Implementation (Delegated)

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        var id = await _innerStore.StoreAsync(chunk, cancellationToken);

        // Auto-quantize if embedding exists and option enabled
        if (_options.AutoQuantizeOnStore && chunk.Embedding != null)
        {
            try
            {
                var quantized = await _quantizer.QuantizeAsync(chunk.Embedding, cancellationToken);
                _quantizedEmbeddings[id] = quantized;
            }
            catch (Exception ex)
            {
                LogAutoQuantizeFailed(_logger, ex, id);
            }
        }

        return id;
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var ids = await _innerStore.StoreBatchAsync(chunkList, cancellationToken);
        var idList = ids.ToList();

        // Auto-quantize if enabled
        if (_options.AutoQuantizeOnStore)
        {
            var embeddings = chunkList
                .Select(c => c.Embedding)
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();

            if (embeddings.Count > 0)
            {
                try
                {
                    var quantizedList = await _quantizer.QuantizeBatchAsync(embeddings, cancellationToken);
                    var quantizedArray = quantizedList.ToArray();

                    int quantizedIndex = 0;
                    for (int i = 0; i < chunkList.Count && quantizedIndex < quantizedArray.Length; i++)
                    {
                        if (chunkList[i].Embedding != null)
                        {
                            _quantizedEmbeddings[idList[i]] = quantizedArray[quantizedIndex];
                            quantizedIndex++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogAutoQuantizeBatchFailed(_logger, ex);
                }
            }
        }

        return idList;
    }

    public Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _innerStore.GetAsync(id, cancellationToken);

    public Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
        => _innerStore.GetByDocumentIdAsync(documentId, cancellationToken);

    public Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        => _innerStore.GetChunksByIdsAsync(ids, cancellationToken);

    public Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
        => _innerStore.SearchAsync(queryEmbedding, topK, minScore, cancellationToken);

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _quantizedEmbeddings.TryRemove(id, out _);
        return await _innerStore.DeleteAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        // Get all chunk IDs for this document to remove quantized embeddings
        var chunks = await _innerStore.GetByDocumentIdAsync(documentId, cancellationToken);
        foreach (var chunk in chunks)
        {
            if (chunk.Id != null)
            {
                _quantizedEmbeddings.TryRemove(chunk.Id, out _);
            }
        }
        return await _innerStore.DeleteByDocumentIdAsync(documentId, cancellationToken);
    }

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
        => _innerStore.ExistsAsync(id, cancellationToken);

    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => _innerStore.GetByIdAsync(id, cancellationToken);

    public Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
        => _innerStore.UpdateAsync(chunk, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => _innerStore.CountAsync(cancellationToken);

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        => _innerStore.GetCountAsync(cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _quantizedEmbeddings.Clear();
        await _innerStore.ClearAsync(cancellationToken);
    }

    #endregion

    #region IQuantizedVectorStore Implementation

    public async Task<string> StoreWithQuantizedAsync(
        DocumentChunk chunk,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        var id = await _innerStore.StoreAsync(chunk, cancellationToken);
        _quantizedEmbeddings[id] = quantizedEmbedding;
        return id;
    }

    public async Task<IEnumerable<string>> StoreBatchWithQuantizedAsync(
        IEnumerable<(DocumentChunk Chunk, QuantizedVector QuantizedEmbedding)> chunksWithQuantized,
        CancellationToken cancellationToken = default)
    {
        var items = chunksWithQuantized.ToList();
        var chunks = items.Select(x => x.Chunk);
        var ids = await _innerStore.StoreBatchAsync(chunks, cancellationToken);
        var idList = ids.ToList();

        for (int i = 0; i < idList.Count; i++)
        {
            _quantizedEmbeddings[idList[i]] = items[i].QuantizedEmbedding;
        }

        return idList;
    }

    public async Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchQuantizedAsync(
        QuantizedVector queryQuantized,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (_quantizedEmbeddings.IsEmpty)
        {
            LogNoQuantizedEmbeddings(_logger);
            return Enumerable.Empty<(DocumentChunk, float)>();
        }

        // Compute distances to all quantized vectors
        var candidates = new List<(string Id, float Distance)>();

        foreach (var kvp in _quantizedEmbeddings)
        {
            var distance = _quantizer.ComputeDistance(queryQuantized, kvp.Value);
            candidates.Add((kvp.Key, distance));
        }

        // Sort by distance (lower is better) and convert to similarity score
        var topCandidates = candidates
            .OrderBy(c => c.Distance)
            .Take(topK * 2) // Get extra candidates for filtering
            .ToList();

        // Fetch chunks and compute similarity scores
        var results = new List<(DocumentChunk Chunk, float Score)>();
        var chunkIds = topCandidates.Select(c => c.Id).ToList();
        var chunks = await _innerStore.GetChunksByIdsAsync(chunkIds, cancellationToken);
        var chunkDict = chunks.ToDictionary(c => c.Id ?? "", c => c);

        foreach (var candidate in topCandidates)
        {
            if (!chunkDict.TryGetValue(candidate.Id, out var chunk)) continue;

            // Convert distance to similarity score (assuming normalized vectors)
            // For cosine distance: similarity = 1 - distance
            // For euclidean: similarity = 1 / (1 + distance)
            var score = ConvertDistanceToScore(candidate.Distance);

            if (score >= minScore)
            {
                results.Add((chunk, score));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK);
    }

    public async Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchWithRerankAsync(
        float[] queryEmbedding,
        QuantizedVector queryQuantized,
        int topK = 10,
        int candidateMultiplier = 3,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        // Phase 1: Fast quantized search to get candidates
        var candidateCount = topK * candidateMultiplier;
        var candidates = await SearchQuantizedAsync(queryQuantized, candidateCount, 0.0f, cancellationToken);
        var candidateList = candidates.ToList();

        if (candidateList.Count == 0)
        {
            // Fallback to original search
            var fallbackResults = await _innerStore.SearchAsync(queryEmbedding, topK, minScore, cancellationToken);
            return fallbackResults.Select(c => (c, ComputeCosineSimilarity(queryEmbedding, c.Embedding ?? Array.Empty<float>())));
        }

        // Phase 2: Rerank with original embeddings for accuracy
        var rerankedResults = candidateList
            .Where(c => c.Chunk.Embedding != null)
            .Select(c => (
                Chunk: c.Chunk,
                Score: ComputeCosineSimilarity(queryEmbedding, c.Chunk.Embedding!)
            ))
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK);

        return rerankedResults;
    }

    public Task<QuantizedVector?> GetQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        _quantizedEmbeddings.TryGetValue(chunkId, out var quantized);
        return Task.FromResult(quantized);
    }

    public Task<bool> HasQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_quantizedEmbeddings.ContainsKey(chunkId));
    }

    public Task<bool> UpdateQuantizedEmbeddingAsync(
        string chunkId,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        _quantizedEmbeddings[chunkId] = quantizedEmbedding;
        return Task.FromResult(true);
    }

    public async Task<QuantizedStorageStats> GetQuantizedStatsAsync(CancellationToken cancellationToken = default)
    {
        var totalCount = await _innerStore.CountAsync(cancellationToken);
        var quantizedCount = _quantizedEmbeddings.Count;

        long quantizedSize = 0;
        long estimatedOriginalSize = 0;
        QuantizationType? quantizationType = null;

        foreach (var kvp in _quantizedEmbeddings)
        {
            quantizedSize += kvp.Value.SizeBytes;
            estimatedOriginalSize += kvp.Value.OriginalDimension * sizeof(float);
            quantizationType ??= kvp.Value.Type;
        }

        return new QuantizedStorageStats
        {
            QuantizedChunkCount = quantizedCount,
            UnquantizedChunkCount = totalCount - quantizedCount,
            QuantizedStorageSizeBytes = quantizedSize,
            EstimatedOriginalSizeBytes = estimatedOriginalSize,
            QuantizationType = quantizationType
        };
    }

    #endregion

    #region Private Helpers

    private static float ConvertDistanceToScore(float distance)
    {
        // Convert distance to similarity score
        // Assumes distance is in range [0, 2] for cosine distance
        // or [0, inf) for euclidean distance
        if (distance <= 0) return 1.0f;
        if (distance >= 2) return 0.0f;
        return 1.0f - (distance / 2.0f);
    }

    private static float ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

        float dotProduct = 0f;
        float magnitudeA = 0f;
        float magnitudeB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var magnitude = (float)Math.Sqrt(magnitudeA) * (float)Math.Sqrt(magnitudeB);
        return magnitude == 0 ? 0 : dotProduct / magnitude;
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to auto-quantize embedding for chunk {ChunkId}")]
    private static partial void LogAutoQuantizeFailed(ILogger logger, Exception exception, string chunkId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to auto-quantize batch embeddings")]
    private static partial void LogAutoQuantizeBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No quantized embeddings available for search")]
    private static partial void LogNoQuantizedEmbeddings(ILogger logger);

    #endregion
}

/// <summary>
/// Configuration options for QuantizedVectorStoreDecorator.
/// </summary>
public class QuantizedVectorStoreOptions
{
    /// <summary>
    /// Automatically quantize embeddings when storing chunks.
    /// Default: true
    /// </summary>
    public bool AutoQuantizeOnStore { get; set; } = true;

    /// <summary>
    /// Store original embeddings alongside quantized ones.
    /// Required for reranking operations.
    /// Default: true
    /// </summary>
    public bool StoreOriginalEmbeddings { get; set; } = true;

    /// <summary>
    /// Default candidate multiplier for rerank search.
    /// Higher values increase accuracy but reduce performance.
    /// Default: 3
    /// </summary>
    public int DefaultCandidateMultiplier { get; set; } = 3;
}
