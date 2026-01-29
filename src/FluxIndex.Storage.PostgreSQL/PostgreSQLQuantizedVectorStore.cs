using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Text.Json;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL vector store with native quantized embedding storage support.
/// Uses pgvector for original embeddings and a separate table for quantized data.
/// </summary>
public class PostgreSQLQuantizedVectorStore : IQuantizedVectorStore
{
    private readonly FluxIndexQuantizedDbContext _context;
    private readonly IVectorQuantizer _quantizer;
    private readonly ILogger<PostgreSQLQuantizedVectorStore> _logger;
    private readonly PostgreSQLQuantizedOptions _options;

    public PostgreSQLQuantizedVectorStore(
        FluxIndexQuantizedDbContext context,
        IVectorQuantizer quantizer,
        ILogger<PostgreSQLQuantizedVectorStore> logger,
        IOptions<PostgreSQLQuantizedOptions> options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _quantizer = quantizer ?? throw new ArgumentNullException(nameof(quantizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
    }

    public IVectorQuantizer? Quantizer => _quantizer;
    public bool SupportsQuantization => true;

    #region IVectorStore Implementation

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var entity = new QuantizedVectorEntity
        {
            Id = id,
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : null,
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata ?? new Dictionary<string, object>()
        };

        _context.Vectors.Add(entity);

        // Auto-quantize if enabled
        if (_options.AutoQuantizeOnStore && chunk.Embedding != null)
        {
            try
            {
                var quantized = await _quantizer.QuantizeAsync(chunk.Embedding, cancellationToken);
                var quantizedEntity = CreateQuantizedEntity(id.ToString(), quantized);
                _context.QuantizedVectors.Add(quantizedEntity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-quantize embedding for chunk {ChunkId}", id);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return id.ToString();
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var ids = new List<string>();

        foreach (var chunk in chunkList)
        {
            var id = Guid.NewGuid();
            ids.Add(id.ToString());

            var entity = new QuantizedVectorEntity
            {
                Id = id,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
                Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : null,
                TokenCount = chunk.TokenCount,
                Metadata = chunk.Metadata ?? new Dictionary<string, object>()
            };
            _context.Vectors.Add(entity);
        }

        // Batch quantize
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
                    _logger.LogWarning(ex, "Failed to batch quantize embeddings");
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(id), cancellationToken);

        return entity != null ? MapToChunk(entity) : null;
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
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
        var guids = ids.Select(Guid.Parse).ToList();
        var entities = await _context.Vectors
            .Where(v => guids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding);

        // Use pgvector's cosine distance for efficient search
        var candidates = await _context.Vectors
            .OrderBy(v => v.Embedding.CosineDistance(queryVector))
            .Take(topK * 3)
            .Select(v => new
            {
                Distance = v.Embedding.CosineDistance(queryVector),
                Entity = v
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(c => new { Chunk = MapToChunk(c.Entity), Similarity = 1.0 - c.Distance })
            .Where(r => r.Similarity >= minScore)
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .Select(r => r.Chunk);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var guid = Guid.Parse(id);
        var entity = await _context.Vectors.FirstOrDefaultAsync(v => v.Id == guid, cancellationToken);
        if (entity == null) return false;

        _context.Vectors.Remove(entity);

        var quantizedEntity = await _context.QuantizedVectors
            .FirstOrDefaultAsync(q => q.ChunkId == id, cancellationToken);
        if (quantizedEntity != null)
        {
            _context.QuantizedVectors.Remove(quantizedEntity);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (!entities.Any()) return false;

        var chunkIds = entities.Select(e => e.Id.ToString()).ToList();
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
        return await _context.Vectors.AnyAsync(v => v.Id == Guid.Parse(id), cancellationToken);
    }

    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetAsync(id, cancellationToken);

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(chunk.Id ?? ""), cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : null;
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = chunk.Metadata ?? new Dictionary<string, object>();

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Vectors.CountAsync(cancellationToken);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        => CountAsync(cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE quantized_vectors", cancellationToken);
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE vectors", cancellationToken);
    }

    #endregion

    #region IQuantizedVectorStore Implementation

    public async Task<string> StoreWithQuantizedAsync(
        DocumentChunk chunk,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var entity = new QuantizedVectorEntity
        {
            Id = id,
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : null,
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata ?? new Dictionary<string, object>()
        };

        _context.Vectors.Add(entity);
        _context.QuantizedVectors.Add(CreateQuantizedEntity(id.ToString(), quantizedEmbedding));
        await _context.SaveChangesAsync(cancellationToken);

        return id.ToString();
    }

    public async Task<IEnumerable<string>> StoreBatchWithQuantizedAsync(
        IEnumerable<(DocumentChunk Chunk, QuantizedVector QuantizedEmbedding)> chunksWithQuantized,
        CancellationToken cancellationToken = default)
    {
        var items = chunksWithQuantized.ToList();
        var ids = new List<string>();

        foreach (var (chunk, quantized) in items)
        {
            var id = Guid.NewGuid();
            ids.Add(id.ToString());

            var entity = new QuantizedVectorEntity
            {
                Id = id,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
                Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : null,
                TokenCount = chunk.TokenCount,
                Metadata = chunk.Metadata ?? new Dictionary<string, object>()
            };

            _context.Vectors.Add(entity);
            _context.QuantizedVectors.Add(CreateQuantizedEntity(id.ToString(), quantized));
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
        var quantizedEntities = await _context.QuantizedVectors.ToListAsync(cancellationToken);
        if (!quantizedEntities.Any()) return Enumerable.Empty<(DocumentChunk, float)>();

        // Compute distances in memory (quantized search)
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
        var chunkIds = candidates.Select(c => Guid.Parse(c.ChunkId)).ToList();
        var chunks = await _context.Vectors
            .Where(v => chunkIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id.ToString(), v => v, cancellationToken);

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
        // Phase 1: Fast quantized search
        var candidateCount = topK * candidateMultiplier;
        var candidates = await SearchQuantizedAsync(queryQuantized, candidateCount, 0.0f, cancellationToken);
        var candidateList = candidates.ToList();

        if (candidateList.Count == 0)
        {
            // Fallback to pgvector search
            var fallbackResults = await SearchAsync(queryEmbedding, topK, minScore, cancellationToken);
            return fallbackResults.Select(c => (c, ComputeCosineSimilarity(queryEmbedding, c.Embedding ?? Array.Empty<float>())));
        }

        // Phase 2: Rerank with original embeddings
        var reranked = candidateList
            .Where(c => c.Chunk.Embedding != null)
            .Select(c => (
                Chunk: c.Chunk,
                Score: ComputeCosineSimilarity(queryEmbedding, c.Chunk.Embedding!)
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
        var entity = await _context.QuantizedVectors
            .FirstOrDefaultAsync(q => q.ChunkId == chunkId, cancellationToken);

        return entity != null ? DeserializeQuantizedVector(entity) : null;
    }

    public async Task<bool> HasQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        return await _context.QuantizedVectors.AnyAsync(q => q.ChunkId == chunkId, cancellationToken);
    }

    public async Task<bool> UpdateQuantizedEmbeddingAsync(
        string chunkId,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
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

    #region Private Helpers

    private static PostgresQuantizedEmbeddingEntity CreateQuantizedEntity(string chunkId, QuantizedVector quantized)
    {
        return new PostgresQuantizedEmbeddingEntity
        {
            Id = Guid.NewGuid(),
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

    private static QuantizedVector DeserializeQuantizedVector(PostgresQuantizedEmbeddingEntity entity)
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
            Id = entity.Id.ToString(),
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            Content = entity.Content,
            Embedding = entity.Embedding?.ToArray(),
            TokenCount = entity.TokenCount,
            Metadata = entity.Metadata
        };

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

    #endregion
}

/// <summary>
/// Configuration options for PostgreSQLQuantizedVectorStore.
/// </summary>
public class PostgreSQLQuantizedOptions : PostgreSQLOptions
{
    /// <summary>
    /// Automatically quantize embeddings when storing chunks.
    /// </summary>
    public bool AutoQuantizeOnStore { get; set; } = true;
}
