using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite vector store with native quantized embedding storage support.
/// Stores quantized embeddings in a separate table for efficient retrieval.
/// </summary>
public partial class SQLiteQuantizedVectorStore : IQuantizedVectorStore, IDisposable
{
    private readonly SQLiteQuantizedDbContext _context;
    private readonly IVectorQuantizer _quantizer;
    private readonly ILogger<SQLiteQuantizedVectorStore> _logger;
    private readonly SQLiteQuantizedOptions _options;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SQLiteQuantizedVectorStore(
        SQLiteQuantizedDbContext context,
        IVectorQuantizer quantizer,
        ILogger<SQLiteQuantizedVectorStore> logger,
        IOptions<SQLiteQuantizedOptions> options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _quantizer = quantizer ?? throw new ArgumentNullException(nameof(quantizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
    }

    public IVectorQuantizer? Quantizer => _quantizer;
    public bool SupportsQuantization => true;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await _context.Database.EnsureCreatedAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    #region IVectorStore Implementation

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var id = chunk.Id ?? Guid.NewGuid().ToString();
        var entity = CreateVectorEntity(chunk, id);

        _context.Vectors.Add(entity);

        // Auto-quantize if enabled and embedding exists
        if (_options.AutoQuantizeOnStore && chunk.Embedding != null)
        {
            try
            {
                var quantized = await _quantizer.QuantizeAsync(chunk.Embedding, cancellationToken);
                var quantizedEntity = CreateQuantizedEntity(id, quantized);
                _context.QuantizedVectors.Add(quantizedEntity);
            }
            catch (Exception ex)
            {
                LogAutoQuantizeFailed(_logger, ex, id);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var chunkList = chunks.ToList();
        var ids = new List<string>();

        foreach (var chunk in chunkList)
        {
            var id = chunk.Id ?? Guid.NewGuid().ToString();
            ids.Add(id);

            var entity = CreateVectorEntity(chunk, id);
            _context.Vectors.Add(entity);
        }

        // Batch quantize if enabled
        if (_options.AutoQuantizeOnStore)
        {
            var embeddingsToQuantize = chunkList
                .Select((c, i) => (Index: i, Embedding: c.Embedding))
                .Where(x => x.Embedding != null)
                .ToList();

            if (embeddingsToQuantize.Count > 0)
            {
                try
                {
                    var embeddings = embeddingsToQuantize.Select(x => x.Embedding!);
                    var quantizedList = await _quantizer.QuantizeBatchAsync(embeddings, cancellationToken);
                    var quantizedArray = quantizedList.ToArray();

                    for (int i = 0; i < quantizedArray.Length; i++)
                    {
                        var originalIndex = embeddingsToQuantize[i].Index;
                        var quantizedEntity = CreateQuantizedEntity(ids[originalIndex], quantizedArray[i]);
                        _context.QuantizedVectors.Add(quantizedEntity);
                    }
                }
                catch (Exception ex)
                {
                    LogBatchQuantizeFailed(_logger, ex);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.Vectors.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        return entity != null ? MapToChunk(entity) : null;
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.ChunkIndex)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    public async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var idList = ids.ToList();
        var entities = await _context.Vectors
            .Where(v => idList.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filters = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entities = await _context.Vectors
            .Where(v => v.Embedding != null)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return Enumerable.Empty<DocumentChunk>();

        var queryMagnitude = ComputeMagnitude(queryEmbedding);
        if (queryMagnitude == 0) return Enumerable.Empty<DocumentChunk>();

        var results = entities
            .Select(e => new { Entity = e, Score = FastCosineSimilarity(queryEmbedding, e.Embedding!, queryMagnitude) })
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => MapToChunk(x.Entity));

        return results;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.Vectors.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity == null) return false;

        _context.Vectors.Remove(entity);

        var quantizedEntity = await _context.QuantizedVectors.FirstOrDefaultAsync(q => q.ChunkId == id, cancellationToken);
        if (quantizedEntity != null)
        {
            _context.QuantizedVectors.Remove(quantizedEntity);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return false;

        var chunkIds = entities.Select(e => e.Id).ToList();
        var quantizedEntities = await _context.QuantizedVectors
            .Where(q => chunkIds.Contains(q.ChunkId))
            .ToListAsync(cancellationToken);

        _context.Vectors.RemoveRange(entities);
        _context.QuantizedVectors.RemoveRange(quantizedEntities);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _context.Vectors.AnyAsync(v => v.Id == id, cancellationToken);
    }

    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetAsync(id, cancellationToken);

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.Vectors.FirstOrDefaultAsync(v => v.Id == chunk.Id, cancellationToken);
        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding?.ToArray();
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = chunk.Metadata ?? new Dictionary<string, object>();

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _context.Vectors.CountAsync(cancellationToken);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        => CountAsync(cancellationToken);

    public async Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _context.Vectors
            .Select(v => v.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        _context.QuantizedVectors.RemoveRange(_context.QuantizedVectors);
        _context.Vectors.RemoveRange(_context.Vectors);
        await _context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region IQuantizedVectorStore Implementation

    public async Task<string> StoreWithQuantizedAsync(
        DocumentChunk chunk,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var id = chunk.Id ?? Guid.NewGuid().ToString();
        var entity = CreateVectorEntity(chunk, id);
        var quantizedEntity = CreateQuantizedEntity(id, quantizedEmbedding);

        _context.Vectors.Add(entity);
        _context.QuantizedVectors.Add(quantizedEntity);
        await _context.SaveChangesAsync(cancellationToken);

        return id;
    }

    public async Task<IEnumerable<string>> StoreBatchWithQuantizedAsync(
        IEnumerable<(DocumentChunk Chunk, QuantizedVector QuantizedEmbedding)> chunksWithQuantized,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var items = chunksWithQuantized.ToList();
        var ids = new List<string>();

        foreach (var (chunk, quantized) in items)
        {
            var id = chunk.Id ?? Guid.NewGuid().ToString();
            ids.Add(id);

            _context.Vectors.Add(CreateVectorEntity(chunk, id));
            _context.QuantizedVectors.Add(CreateQuantizedEntity(id, quantized));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public async Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchQuantizedAsync(
        QuantizedVector queryQuantized,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var quantizedEntities = await _context.QuantizedVectors.ToListAsync(cancellationToken);
        if (quantizedEntities.Count == 0) return Enumerable.Empty<(DocumentChunk, float)>();

        // Compute distances and rank
        var candidates = quantizedEntities
            .Select(e => new
            {
                ChunkId = e.ChunkId,
                Distance = _quantizer.ComputeDistance(queryQuantized, DeserializeQuantizedVector(e))
            })
            .OrderBy(x => x.Distance)
            .Take(topK * 2)
            .ToList();

        // Fetch chunks
        var chunkIds = candidates.Select(c => c.ChunkId).ToList();
        var chunks = await _context.Vectors
            .Where(v => chunkIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v, cancellationToken);

        var results = new List<(DocumentChunk, float)>();
        foreach (var candidate in candidates)
        {
            if (!chunks.TryGetValue(candidate.ChunkId, out var entity)) continue;

            var score = ConvertDistanceToScore(candidate.Distance);
            if (score >= minScore)
            {
                results.Add((MapToChunk(entity), score));
            }
        }

        return results.OrderByDescending(r => r.Item2).Take(topK);
    }

    public async Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchWithRerankAsync(
        float[] queryEmbedding,
        QuantizedVector queryQuantized,
        int topK = 10,
        int candidateMultiplier = 3,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        // Phase 1: Fast quantized search
        var candidateCount = topK * candidateMultiplier;
        var candidates = await SearchQuantizedAsync(queryQuantized, candidateCount, 0.0f, cancellationToken);
        var candidateList = candidates.ToList();

        if (candidateList.Count == 0)
        {
            // Fallback to original search
            var fallbackResults = await SearchAsync(queryEmbedding, topK, minScore, null, cancellationToken);
            return fallbackResults.Select(c => (c, ComputeCosineSimilarity(queryEmbedding, c.Embedding ?? Array.Empty<float>())));
        }

        // Phase 2: Rerank with original embeddings
        var queryMagnitude = ComputeMagnitude(queryEmbedding);
        var reranked = candidateList
            .Where(c => c.Chunk.Embedding != null)
            .Select(c => (
                Chunk: c.Chunk,
                Score: FastCosineSimilarity(queryEmbedding, c.Chunk.Embedding!, queryMagnitude)
            ))
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK);

        return reranked;
    }

    public async Task<QuantizedVector?> GetQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.QuantizedVectors
            .FirstOrDefaultAsync(q => q.ChunkId == chunkId, cancellationToken);

        return entity != null ? DeserializeQuantizedVector(entity) : null;
    }

    public async Task<bool> HasQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _context.QuantizedVectors.AnyAsync(q => q.ChunkId == chunkId, cancellationToken);
    }

    public async Task<bool> UpdateQuantizedEmbeddingAsync(
        string chunkId,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.QuantizedVectors
            .FirstOrDefaultAsync(q => q.ChunkId == chunkId, cancellationToken);

        if (entity == null)
        {
            entity = CreateQuantizedEntity(chunkId, quantizedEmbedding);
            _context.QuantizedVectors.Add(entity);
        }
        else
        {
            entity.QuantizedData = quantizedEmbedding.Data;
            entity.QuantizationType = (int)quantizedEmbedding.Type;
            entity.OriginalDimension = quantizedEmbedding.OriginalDimension;
            entity.MetadataJson = quantizedEmbedding.Metadata != null
                ? JsonSerializer.Serialize(quantizedEmbedding.Metadata)
                : null;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<QuantizedStorageStats> GetQuantizedStatsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var totalCount = await _context.Vectors.CountAsync(cancellationToken);
        var quantizedCount = await _context.QuantizedVectors.CountAsync(cancellationToken);

        var quantizedEntities = await _context.QuantizedVectors.ToListAsync(cancellationToken);

        long quantizedSize = 0;
        long estimatedOriginalSize = 0;
        QuantizationType? quantizationType = null;

        foreach (var entity in quantizedEntities)
        {
            quantizedSize += entity.QuantizedData.Length;
            estimatedOriginalSize += entity.OriginalDimension * sizeof(float);
            quantizationType ??= (QuantizationType)entity.QuantizationType;
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

    /// <summary>
    /// Disposes the initialization lock semaphore.
    /// </summary>
    public void Dispose()
    {
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to auto-quantize embedding for chunk {ChunkId}")]
    private static partial void LogAutoQuantizeFailed(ILogger logger, Exception exception, string chunkId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to batch quantize embeddings")]
    private static partial void LogBatchQuantizeFailed(ILogger logger, Exception exception);

    #endregion

    #region Private Helpers

    private static QuantizedVectorEntity CreateVectorEntity(DocumentChunk chunk, string id)
    {
        return new QuantizedVectorEntity
        {
            Id = id,
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding?.ToArray(),
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata ?? new Dictionary<string, object>()
        };
    }

    private static QuantizedEmbeddingEntity CreateQuantizedEntity(string chunkId, QuantizedVector quantized)
    {
        return new QuantizedEmbeddingEntity
        {
            Id = Guid.NewGuid().ToString(),
            ChunkId = chunkId,
            QuantizedData = quantized.Data,
            QuantizationType = (int)quantized.Type,
            OriginalDimension = quantized.OriginalDimension,
            MetadataJson = quantized.Metadata != null
                ? JsonSerializer.Serialize(quantized.Metadata)
                : null,
            CreatedAt = quantized.CreatedAt
        };
    }

    private static QuantizedVector DeserializeQuantizedVector(QuantizedEmbeddingEntity entity)
    {
        return new QuantizedVector
        {
            Data = entity.QuantizedData,
            Type = (QuantizationType)entity.QuantizationType,
            OriginalDimension = entity.OriginalDimension,
            Metadata = !string.IsNullOrEmpty(entity.MetadataJson)
                ? JsonSerializer.Deserialize<QuantizationMetadata>(entity.MetadataJson)
                : null,
            CreatedAt = entity.CreatedAt
        };
    }

    private static DocumentChunk MapToChunk(QuantizedVectorEntity entity)
    {
        var chunk = new DocumentChunk
        {
            Id = entity.Id,
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            Content = entity.Content,
            Embedding = entity.Embedding,
            TokenCount = entity.TokenCount,
            Metadata = entity.Metadata
        };

        // Include standard fields in metadata for consumer apps (RAG source citation)
        chunk.Metadata = MetadataHelper.EnsureInitialized(chunk.Metadata);
        chunk.Metadata["chunkIndex"] = chunk.ChunkIndex;
        chunk.Metadata["totalChunks"] = chunk.TotalChunks;
        chunk.Metadata["tokenCount"] = chunk.TokenCount;

        RestoreRichMetadataStatic(chunk);
        return chunk;
    }

    private static void RestoreRichMetadataStatic(DocumentChunk chunk)
    {
        if (chunk.Metadata == null)
            return;

        var chunkMetadata = MetadataHelper.DeserializeChunkMetadata(chunk.Metadata);
        if (chunkMetadata != null)
            chunk.SetMetadata(chunkMetadata);

        var quality = MetadataHelper.DeserializeChunkQuality(chunk.Metadata);
        if (quality != null)
            chunk.SetQuality(quality);

        var relationships = MetadataHelper.DeserializeRelationships(chunk.Metadata);
        if (relationships != null)
        {
            foreach (var rel in relationships)
                chunk.AddRelationship(rel);
        }
    }

    private static float ConvertDistanceToScore(float distance)
    {
        if (distance <= 0) return 1.0f;
        if (distance >= 2) return 0.0f;
        return 1.0f - (distance / 2.0f);
    }

    private static float ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

        float dotProduct = 0f, magA = 0f, magB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var mag = (float)Math.Sqrt(magA) * (float)Math.Sqrt(magB);
        return mag == 0 ? 0 : dotProduct / mag;
    }

    private static float FastCosineSimilarity(float[] query, float[] candidate, float queryMagnitude)
    {
        if (query.Length != candidate.Length || queryMagnitude == 0) return 0f;

        float dotProduct = 0f, candidateMag = 0f;
        for (int i = 0; i < query.Length; i++)
        {
            dotProduct += query[i] * candidate[i];
            candidateMag += candidate[i] * candidate[i];
        }

        candidateMag = (float)Math.Sqrt(candidateMag);
        return candidateMag == 0 ? 0 : dotProduct / (queryMagnitude * candidateMag);
    }

    private static float ComputeMagnitude(float[] vector)
    {
        float sum = 0f;
        for (int i = 0; i < vector.Length; i++)
            sum += vector[i] * vector[i];
        return (float)Math.Sqrt(sum);
    }

    #endregion
}

/// <summary>
/// Configuration options for SQLiteQuantizedVectorStore.
/// </summary>
public class SQLiteQuantizedOptions : SQLiteOptions
{
    /// <summary>
    /// Automatically quantize embeddings when storing chunks.
    /// </summary>
    public bool AutoQuantizeOnStore { get; set; } = true;
}
