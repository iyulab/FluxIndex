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
    private readonly IDbContextFactory<SQLiteQuantizedDbContext> _contextFactory;
    private readonly IVectorQuantizer _quantizer;
    private readonly ILogger<SQLiteQuantizedVectorStore> _logger;
    private readonly SQLiteQuantizedOptions _options;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SQLiteQuantizedVectorStore(
        IDbContextFactory<SQLiteQuantizedDbContext> contextFactory,
        IVectorQuantizer quantizer,
        ILogger<SQLiteQuantizedVectorStore> logger,
        IOptions<SQLiteQuantizedOptions> options)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
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
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            SQLiteSchemaProvisioner.Provision(context);
            await TotalChunksBackfill.RunAsync(context, "vectors", cancellationToken);
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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var id = ResolveChunkId(chunk);
        await UpsertRowAsync(context, chunk, id, cancellationToken);

        // Auto-quantize if enabled and embedding exists
        if (_options.AutoQuantizeOnStore && chunk.Embedding != null)
        {
            try
            {
                var quantized = await _quantizer.QuantizeAsync(chunk.Embedding, cancellationToken);
                var quantizedEntity = CreateQuantizedEntity(id, quantized);
                context.QuantizedVectors.Add(quantizedEntity);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogAutoQuantizeFailed(_logger, ex, id);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var chunkList = chunks.ToList();
        var ids = new List<string>();

        foreach (var chunk in chunkList)
        {
            var id = ResolveChunkId(chunk);
            ids.Add(id);
            await UpsertRowAsync(context, chunk, id, cancellationToken);
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
                        context.QuantizedVectors.Add(quantizedEntity);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    LogBatchQuantizeFailed(_logger, ex);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var entity = await context.Vectors.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        return entity != null ? MapToChunk(entity) : null;
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var entities = await context.Vectors
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.ChunkIndex)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    public async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var idList = ids.ToList();
        var entities = await context.Vectors
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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // Fail-loud at call time (IVectorStore filter contract).
        Core.Application.Services.Base.VectorStoreBase.ValidateFilters(filters);

        await EnsureInitializedAsync(cancellationToken);

        var entities = await context.Vectors
            .Where(v => v.Embedding != null)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return Enumerable.Empty<DocumentChunk>();

        var queryMagnitude = ComputeMagnitude(queryEmbedding);
        if (queryMagnitude == 0) return Enumerable.Empty<DocumentChunk>();

        // Apply metadata filters BEFORE the topK trim (IVectorStore filter contract, incl.
        // collection values = MatchAny) — previously filters were silently ignored, leaking
        // chunks across filter scope (e.g. other tenants).
        var matcher = Core.Application.Services.Base.MetadataFilterMatcher.Compile(filters);
        var results = entities
            .Select(e => new { Chunk = MapToChunk(e), Score = FastCosineSimilarity(queryEmbedding, e.Embedding!, queryMagnitude) })
            .Where(x => matcher.Matches(x.Chunk.DocumentId, x.Chunk.Metadata))
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

        return results;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        // AsTracking: NoTracking context, and Remove() on a detached instance throws when the
        // same row is already tracked (a Store in the same scope leaves it so).
        var entity = await context.Vectors.AsTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity == null) return false;

        context.Vectors.Remove(entity);

        var quantizedEntity = await context.QuantizedVectors.AsTracking().FirstOrDefaultAsync(q => q.ChunkId == id, cancellationToken);
        if (quantizedEntity != null)
        {
            context.QuantizedVectors.Remove(quantizedEntity);
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var entities = await context.Vectors
            .AsTracking()
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return false;

        var chunkIds = entities.Select(e => e.Id).ToList();
        var quantizedEntities = await context.QuantizedVectors
            .AsTracking()
            .Where(q => chunkIds.Contains(q.ChunkId))
            .ToListAsync(cancellationToken);

        context.Vectors.RemoveRange(entities);
        context.QuantizedVectors.RemoveRange(quantizedEntities);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each chunk row is re-inserted under its new id and the old one deleted (EF Core cannot change a primary key in
    /// place); the quantized rows keep their own keys and have their chunk id rewritten. One <c>SaveChanges</c>, one
    /// transaction. Neither the full-precision nor the quantized embedding is recomputed.
    /// </remarks>
    public async Task<int> ReassignDocumentAsync(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates = null,
        CancellationToken cancellationToken = default)
    {
        DocumentReassignment.ValidateArguments(oldDocumentId, newDocumentId, chunkIdMap);
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Vectors
            .AsTracking()
            .Where(v => v.DocumentId == oldDocumentId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return 0;

        DocumentReassignment.EnsureCovered(rows.Select(r => r.Id), chunkIdMap);

        if (await context.Vectors.AnyAsync(v => v.DocumentId == newDocumentId, cancellationToken))
            throw DocumentReassignment.TargetDocumentNotEmpty(newDocumentId);

        var newIds = rows.Select(r => chunkIdMap[r.Id]).ToList();
        var taken = await context.Vectors
            .Where(v => newIds.Contains(v.Id))
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        if (taken.Count > 0)
            throw DocumentReassignment.TargetChunkIdsTaken(taken);

        var oldIds = rows.Select(r => r.Id).ToList();
        var quantized = await context.QuantizedVectors
            .AsTracking()
            .Where(q => oldIds.Contains(q.ChunkId))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var newId = chunkIdMap[row.Id];
            context.Vectors.Add(new QuantizedVectorEntity
            {
                Id = newId,
                DocumentId = newDocumentId,
                ChunkIndex = row.ChunkIndex,
                TotalChunks = row.TotalChunks,
                Content = row.Content,
                Embedding = row.Embedding,
                TokenCount = row.TokenCount,
                Metadata = DocumentReassignment.RewriteMetadata(
                    row.Metadata, oldDocumentId, newDocumentId, newId, chunkIdMap, metadataUpdates)
            });
        }

        foreach (var entity in quantized)
            entity.ChunkId = chunkIdMap[entity.ChunkId];

        context.Vectors.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);
        return await context.Vectors.AnyAsync(v => v.Id == id, cancellationToken);
    }

    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetAsync(id, cancellationToken);

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        // AsTracking: the context is registered NoTracking, so without it the mutations below
        // are never written and this method reports success anyway.
        var entity = await context.Vectors.AsTracking()
            .FirstOrDefaultAsync(v => v.Id == chunk.Id, cancellationToken);
        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding?.ToArray();
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = MetadataHelper.ForStorage(chunk);

        var hadChanges = context.ChangeTracker.HasChanges();
        var written = await context.SaveChangesAsync(cancellationToken);
        return !hadChanges || written > 0;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);
        return await context.Vectors.CountAsync(cancellationToken);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        => CountAsync(cancellationToken);

    public async Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);
        return await context.Vectors
            .Select(v => v.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        context.QuantizedVectors.RemoveRange(context.QuantizedVectors.AsTracking());
        context.Vectors.RemoveRange(context.Vectors.AsTracking());
        await context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region IQuantizedVectorStore Implementation

    public async Task<string> StoreWithQuantizedAsync(
        DocumentChunk chunk,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var id = chunk.EnsureId();
        var entity = CreateVectorEntity(chunk, id);
        var quantizedEntity = CreateQuantizedEntity(id, quantizedEmbedding);

        context.Vectors.Add(entity);
        context.QuantizedVectors.Add(quantizedEntity);
        await context.SaveChangesAsync(cancellationToken);

        return id;
    }

    public async Task<IEnumerable<string>> StoreBatchWithQuantizedAsync(
        IEnumerable<(DocumentChunk Chunk, QuantizedVector QuantizedEmbedding)> chunksWithQuantized,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var items = chunksWithQuantized.ToList();
        var ids = new List<string>();

        foreach (var (chunk, quantized) in items)
        {
            var id = chunk.EnsureId();
            ids.Add(id);

            context.Vectors.Add(CreateVectorEntity(chunk, id));
            context.QuantizedVectors.Add(CreateQuantizedEntity(id, quantized));
        }

        await context.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public async Task<IEnumerable<(DocumentChunk Chunk, float Score)>> SearchQuantizedAsync(
        QuantizedVector queryQuantized,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var quantizedEntities = await context.QuantizedVectors.ToListAsync(cancellationToken);
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
        var chunks = await context.Vectors
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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var entity = await context.QuantizedVectors.AsTracking()
            .FirstOrDefaultAsync(q => q.ChunkId == chunkId, cancellationToken);

        return entity != null ? DeserializeQuantizedVector(entity) : null;
    }

    public async Task<bool> HasQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);
        return await context.QuantizedVectors.AnyAsync(q => q.ChunkId == chunkId, cancellationToken);
    }

    public async Task<bool> UpdateQuantizedEmbeddingAsync(
        string chunkId,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        // AsTracking: the context is registered NoTracking, so without it the edits below are never
        // written and this method reports success anyway.
        var entity = await context.QuantizedVectors
            .AsTracking()
            .FirstOrDefaultAsync(q => q.ChunkId == chunkId, cancellationToken);

        if (entity == null)
        {
            entity = CreateQuantizedEntity(chunkId, quantizedEmbedding);
            context.QuantizedVectors.Add(entity);
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

        var hadQuantizedChanges = context.ChangeTracker.HasChanges();
        var quantizedWritten = await context.SaveChangesAsync(cancellationToken);
        return !hadQuantizedChanges || quantizedWritten > 0;
    }

    public async Task<QuantizedStorageStats> GetQuantizedStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);

        var totalCount = await context.Vectors.CountAsync(cancellationToken);
        var quantizedCount = await context.QuantizedVectors.CountAsync(cancellationToken);

        var quantizedEntities = await context.QuantizedVectors.ToListAsync(cancellationToken);

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

    /// <summary>Chunk id the caller supplied, or a fresh one when the chunk carries none.</summary>
    private static string ResolveChunkId(DocumentChunk chunk) => chunk.EnsureId();

    /// <summary>
    /// Adds the row for <paramref name="chunk"/>, or updates it when <paramref name="id"/> is
    /// already stored — re-storing an id is an update, not a second row (docs/REFERENCE.md,
    /// "Chunk identity"). On an update the previous quantized embedding is removed, since it
    /// described the embedding that has just been replaced; the caller re-quantizes afterwards.
    /// </summary>
    private static async Task UpsertRowAsync(SQLiteQuantizedDbContext context, DocumentChunk chunk, string id, CancellationToken cancellationToken)
    {
        // AsTracking: NoTracking context — the edit below must be what SaveChanges writes, and a
        // fresh Add for an already-tracked key throws before the database is even reached.
        var existing = await context.Vectors
            .AsTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (existing == null)
        {
            context.Vectors.Add(CreateVectorEntity(chunk, id));
            return;
        }

        existing.DocumentId = chunk.DocumentId;
        existing.ChunkIndex = chunk.ChunkIndex;
        existing.TotalChunks = chunk.TotalChunks;
        existing.Content = chunk.Content;
        existing.Embedding = chunk.Embedding?.ToArray();
        existing.TokenCount = chunk.TokenCount;
        existing.Metadata = MetadataHelper.ForStorage(chunk);

        var staleQuantized = await context.QuantizedVectors
            .AsTracking()
            .FirstOrDefaultAsync(q => q.ChunkId == id, cancellationToken);
        if (staleQuantized != null)
        {
            context.QuantizedVectors.Remove(staleQuantized);
        }
    }

    private static QuantizedVectorEntity CreateVectorEntity(DocumentChunk chunk, string id)
    {
        return new QuantizedVectorEntity
        {
            Id = id,
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            TotalChunks = chunk.TotalChunks,
            Content = chunk.Content,
            Embedding = chunk.Embedding?.ToArray(),
            TokenCount = chunk.TokenCount,
            Metadata = MetadataHelper.ForStorage(chunk)
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
            TotalChunks = entity.TotalChunks ?? 0,
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

        MetadataHelper.RestoreRichMetadata(chunk);
        return chunk;
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
