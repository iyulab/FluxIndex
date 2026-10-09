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
public partial class PostgreSQLQuantizedVectorStore : IQuantizedVectorStore
{
    private readonly IDbContextFactory<FluxIndexQuantizedDbContext> _contextFactory;
    private readonly IVectorQuantizer _quantizer;
    private readonly ILogger<PostgreSQLQuantizedVectorStore> _logger;
    private readonly PostgreSQLQuantizedOptions _options;

    public PostgreSQLQuantizedVectorStore(
        IDbContextFactory<FluxIndexQuantizedDbContext> contextFactory,
        IVectorQuantizer quantizer,
        ILogger<PostgreSQLQuantizedVectorStore> logger,
        IOptions<PostgreSQLQuantizedOptions> options)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _quantizer = quantizer ?? throw new ArgumentNullException(nameof(quantizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
    }

    public IVectorQuantizer? Quantizer => _quantizer;
    public bool SupportsQuantization => true;

    #region IVectorStore Implementation

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var chunkId = ResolveChunkId(chunk);
        var id = ChunkStorageId.ToStorageGuid(chunkId);
        await UpsertRowAsync(context, chunk, chunkId, id, cancellationToken);

        // Auto-quantize if enabled
        if (_options.AutoQuantizeOnStore && chunk.Embedding != null)
        {
            try
            {
                var quantized = await _quantizer.QuantizeAsync(chunk.Embedding, cancellationToken);
                var quantizedEntity = CreateQuantizedEntity(chunkId, quantized);
                context.QuantizedVectors.Add(quantizedEntity);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogAutoQuantizeFailed(_logger, ex, id);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return chunkId;
    }

    public async Task<IEnumerable<string>> StoreBatchAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var chunkList = chunks.ToList();
        var ids = new List<string>();

        foreach (var chunk in chunkList)
        {
            var chunkId = ResolveChunkId(chunk);
            var id = ChunkStorageId.ToStorageGuid(chunkId);
            ids.Add(chunkId);
            await UpsertRowAsync(context, chunk, chunkId, id, cancellationToken);
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
        var entity = await context.Vectors
            .FirstOrDefaultAsync(v => v.Id == ChunkStorageId.ToStorageGuid(id), cancellationToken);

        return entity != null ? MapToChunk(entity) : null;
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
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
        var guids = ids.Select(ChunkStorageId.ToStorageGuid).ToList();
        var entities = await context.Vectors
            .Where(v => guids.Contains(v.Id))
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
        FluxIndex.Core.Application.Services.Base.VectorStoreBase.ValidateFilters(filters);

        var queryVector = new Vector(queryEmbedding);

        // Push metadata filters down to SQL (jsonb @> containment) BEFORE the candidate trim —
        // otherwise higher-scoring non-matching rows crowd matching rows out of the window and the
        // caller silently receives fewer results than exist. Same predicate builder the
        // non-quantized store uses, so the two cannot drift on where a filter runs.
        var query = context.Vectors.AsQueryable();
        if (filters is { Count: > 0 })
        {
            query = query.Where(MetadataPredicateBuilder.Build<QuantizedVectorEntity>(filters, v => v.Metadata, v => v.DocumentId));
        }

        // Over-fetch so the minScore cut below still has candidates to work with.
        var candidates = await query
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
            .Select(r => r.Chunk)
            .ToList();
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var guid = ChunkStorageId.ToStorageGuid(id);
        // AsTracking: NoTracking context — Remove() on a detached instance throws when the same
        // row is already tracked (a Store in the same scope leaves it so).
        var entity = await context.Vectors.AsTracking().FirstOrDefaultAsync(v => v.Id == guid, cancellationToken);
        if (entity == null) return false;

        context.Vectors.Remove(entity);

        var quantizedKeys = QuantizedChunkKeys(entity).Append(id).Distinct(StringComparer.Ordinal).ToList();
        var quantizedEntities = await context.QuantizedVectors
            .AsTracking()
            .Where(q => quantizedKeys.Contains(q.ChunkId))
            .ToListAsync(cancellationToken);
        context.QuantizedVectors.RemoveRange(quantizedEntities);

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.Vectors
            .AsTracking()
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return false;

        var quantizedKeys = entities.SelectMany(QuantizedChunkKeys).Distinct(StringComparer.Ordinal).ToList();
        var quantizedEntities = await context.QuantizedVectors
            .AsTracking()
            .Where(q => quantizedKeys.Contains(q.ChunkId))
            .ToListAsync(cancellationToken);

        context.Vectors.RemoveRange(entities);
        context.QuantizedVectors.RemoveRange(quantizedEntities);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Rows are keyed on <see cref="ChunkStorageId.ToStorageGuid"/> of the chunk id, so each is re-inserted under its
    /// new key and the old row deleted; the quantized rows keep their own keys and have their chunk id rewritten. One
    /// <c>SaveChanges</c>, one transaction. Neither embedding is recomputed.
    /// </remarks>
    public async Task<int> ReassignDocumentAsync(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates = null,
        CancellationToken cancellationToken = default)
    {
        DocumentReassignment.ValidateArguments(oldDocumentId, newDocumentId, chunkIdMap);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await context.Vectors
            .AsTracking()
            .Where(v => v.DocumentId == oldDocumentId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return 0;

        var chunkIds = rows.Select(r => OriginalChunkId(r.Metadata) ?? r.Id.ToString()).ToList();
        DocumentReassignment.EnsureCovered(chunkIds, chunkIdMap);

        if (await context.Vectors.AnyAsync(v => v.DocumentId == newDocumentId, cancellationToken))
            throw DocumentReassignment.TargetDocumentNotEmpty(newDocumentId);

        var newKeys = chunkIds.ToDictionary(id => ChunkStorageId.ToStorageGuid(chunkIdMap[id]), id => chunkIdMap[id]);
        var newGuids = newKeys.Keys.ToList();
        var taken = await context.Vectors
            .Where(v => newGuids.Contains(v.Id))
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        if (taken.Count > 0)
            throw DocumentReassignment.TargetChunkIdsTaken(taken.Select(g => newKeys[g]).ToList());

        // A quantized row names its chunk by the caller's id; rows written before that was preserved used the row key.
        var quantizedKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var newId = chunkIdMap[chunkIds[i]];
            quantizedKeys[chunkIds[i]] = newId;
            quantizedKeys.TryAdd(rows[i].Id.ToString(), newId);
        }
        var quantizedLookup = quantizedKeys.Keys.ToList();
        var quantized = await context.QuantizedVectors
            .AsTracking()
            .Where(q => quantizedLookup.Contains(q.ChunkId))
            .ToListAsync(cancellationToken);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var newId = chunkIdMap[chunkIds[i]];
            var metadata = DocumentReassignment.RewriteMetadata(
                row.Metadata, oldDocumentId, newDocumentId, newId, metadataUpdates);
            metadata[ChunkStorageId.OriginalIdKey] = newId;
            context.Vectors.Add(new QuantizedVectorEntity
            {
                Id = ChunkStorageId.ToStorageGuid(newId),
                DocumentId = newDocumentId,
                ChunkIndex = row.ChunkIndex,
                TotalChunks = row.TotalChunks,
                Content = row.Content,
                Embedding = row.Embedding,
                TokenCount = row.TokenCount,
                Metadata = metadata
            });
        }

        foreach (var entity in quantized)
            entity.ChunkId = quantizedKeys[entity.ChunkId];

        context.Vectors.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Vectors.AnyAsync(v => v.Id == ChunkStorageId.ToStorageGuid(id), cancellationToken);
    }

    public Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetAsync(id, cancellationToken);

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // AsTracking: the quantized context is registered NoTracking, so without it the
        // mutations below are never written and this method reports success anyway.
        var entity = await context.Vectors.AsTracking()
            .FirstOrDefaultAsync(v => v.Id == ChunkStorageId.ToStorageGuid(chunk.Id ?? ""), cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : new Vector(Array.Empty<float>());
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = MetadataHelper.ForStorage(chunk);

        var hadChanges = context.ChangeTracker.HasChanges();
        var written = await context.SaveChangesAsync(cancellationToken);
        return !hadChanges || written > 0;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Vectors.CountAsync(cancellationToken);
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        => CountAsync(cancellationToken);

    public async Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Vectors
            .Select(v => v.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE quantized_vectors", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE vectors", cancellationToken);
    }

    #endregion

    #region IQuantizedVectorStore Implementation

    public async Task<string> StoreWithQuantizedAsync(
        DocumentChunk chunk,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var chunkId = ResolveChunkId(chunk);
        var id = ChunkStorageId.ToStorageGuid(chunkId);
        var entity = new QuantizedVectorEntity
        {
            Id = id,
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            TotalChunks = chunk.TotalChunks,
            Content = chunk.Content,
            Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : new Vector(Array.Empty<float>()),
            TokenCount = chunk.TokenCount,
            Metadata = WithOriginalId(MetadataHelper.ForStorage(chunk), chunkId)
        };

        context.Vectors.Add(entity);
        context.QuantizedVectors.Add(CreateQuantizedEntity(chunkId, quantizedEmbedding));
        await context.SaveChangesAsync(cancellationToken);

        return chunkId;
    }

    public async Task<IEnumerable<string>> StoreBatchWithQuantizedAsync(
        IEnumerable<(DocumentChunk Chunk, QuantizedVector QuantizedEmbedding)> chunksWithQuantized,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var items = chunksWithQuantized.ToList();
        var ids = new List<string>();

        foreach (var (chunk, quantized) in items)
        {
            var chunkId = ResolveChunkId(chunk);
            var id = ChunkStorageId.ToStorageGuid(chunkId);
            ids.Add(chunkId);

            var entity = new QuantizedVectorEntity
            {
                Id = id,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                TotalChunks = chunk.TotalChunks,
                Content = chunk.Content,
                Embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : new Vector(Array.Empty<float>()),
                TokenCount = chunk.TokenCount,
                Metadata = WithOriginalId(MetadataHelper.ForStorage(chunk), chunkId)
            };

            context.Vectors.Add(entity);
            context.QuantizedVectors.Add(CreateQuantizedEntity(chunkId, quantized));
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
        var quantizedEntities = await context.QuantizedVectors.ToListAsync(cancellationToken);
        if (quantizedEntities.Count == 0) return Enumerable.Empty<(DocumentChunk, float)>();

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

        // Fetch chunks. A quantized row names its chunk by the caller's id, the vector row is keyed on the storage GUID
        // derived from it — join on the GUID, which both spellings resolve to.
        var chunkIds = candidates.Select(c => ChunkStorageId.ToStorageGuid(c.ChunkId)).Distinct().ToList();
        var chunks = await context.Vectors
            .Where(v => chunkIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v, cancellationToken);

        // A chunk re-stored across the change in how quantized rows are named can hold one row under each spelling;
        // it is still one chunk, so it is returned once (at its closest distance — candidates are in distance order).
        var results = new List<(DocumentChunk, float)>();
        var seen = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            var key = ChunkStorageId.ToStorageGuid(candidate.ChunkId);
            if (!seen.Add(key) || !chunks.TryGetValue(key, out var entity)) continue;

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
            var fallbackResults = await SearchAsync(queryEmbedding, topK, minScore, null, cancellationToken);
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
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var keys = QuantizedChunkKeys(chunkId);
        var entity = await context.QuantizedVectors
            .Where(q => keys.Contains(q.ChunkId))
            .OrderBy(q => q.ChunkId == chunkId ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);

        return entity != null ? DeserializeQuantizedVector(entity) : null;
    }

    public async Task<bool> HasQuantizedEmbeddingAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var keys = QuantizedChunkKeys(chunkId);
        return await context.QuantizedVectors.AnyAsync(q => keys.Contains(q.ChunkId), cancellationToken);
    }

    public async Task<bool> UpdateQuantizedEmbeddingAsync(
        string chunkId,
        QuantizedVector quantizedEmbedding,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        // AsTracking: the context is registered NoTracking, so without it the edits below are never
        // written and this method reports success anyway.
        var keys = QuantizedChunkKeys(chunkId);
        var entity = await context.QuantizedVectors
            .AsTracking()
            .Where(q => keys.Contains(q.ChunkId))
            .OrderBy(q => q.ChunkId == chunkId ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity == null)
        {
            entity = CreateQuantizedEntity(chunkId, quantizedEmbedding);
            context.QuantizedVectors.Add(entity);
        }
        else
        {
            entity.ChunkId = chunkId;
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

    #region Private Helpers

    /// <summary>Chunk id the caller supplied, or a fresh one when the chunk carries none.</summary>
    private static string ResolveChunkId(DocumentChunk chunk) => chunk.EnsureId();

    /// <summary>
    /// Adds the row for <paramref name="chunk"/>, or updates it when <paramref name="storageId"/>
    /// is already stored — re-storing an id is an update, not a second row (docs/REFERENCE.md,
    /// "Chunk identity"). On an update the previous quantized embedding is removed, since it
    /// described the embedding that has just been replaced; the caller re-quantizes afterwards.
    /// </summary>
    private static async Task UpsertRowAsync(FluxIndexQuantizedDbContext context, DocumentChunk chunk, string chunkId, Guid storageId, CancellationToken cancellationToken)
    {
        var embedding = chunk.Embedding != null ? new Vector(chunk.Embedding) : new Vector(Array.Empty<float>());
        var metadata = WithOriginalId(MetadataHelper.ForStorage(chunk), chunkId);

        // AsTracking: NoTracking context — the edit below must be what SaveChanges writes, and a
        // fresh Add for an already-tracked key throws before the database is even reached.
        var existing = await context.Vectors
            .AsTracking()
            .FirstOrDefaultAsync(v => v.Id == storageId, cancellationToken);

        if (existing == null)
        {
            context.Vectors.Add(new QuantizedVectorEntity
            {
                Id = storageId,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                TotalChunks = chunk.TotalChunks,
                Content = chunk.Content,
                Embedding = embedding,
                TokenCount = chunk.TokenCount,
                Metadata = metadata
            });
            return;
        }

        existing.DocumentId = chunk.DocumentId;
        existing.ChunkIndex = chunk.ChunkIndex;
        existing.TotalChunks = chunk.TotalChunks;
        existing.Content = chunk.Content;
        existing.Embedding = embedding;
        existing.TokenCount = chunk.TokenCount;
        existing.Metadata = metadata;

        var staleKeys = QuantizedChunkKeys(existing).Append(chunkId).Distinct(StringComparer.Ordinal).ToList();
        var staleQuantized = await context.QuantizedVectors
            .AsTracking()
            .Where(q => staleKeys.Contains(q.ChunkId))
            .ToListAsync(cancellationToken);
        context.QuantizedVectors.RemoveRange(staleQuantized);
    }

    /// <summary>Metadata with the caller's chunk id preserved, so reads can return it.</summary>
    private static Dictionary<string, object> WithOriginalId(Dictionary<string, object>? metadata, string chunkId)
    {
        var result = metadata ?? new Dictionary<string, object>();
        result[ChunkStorageId.OriginalIdKey] = chunkId;
        return result;
    }

    /// <summary>
    /// Returns the chunk id the caller supplied, or <c>null</c> for rows written before this store
    /// began preserving it (those were keyed on the id itself, so the row key is still correct).
    /// </summary>
    private static string? OriginalChunkId(Dictionary<string, object>? metadata)
        => metadata is not null
           && metadata.TryGetValue(ChunkStorageId.OriginalIdKey, out var value)
           && value?.ToString() is { Length: > 0 } original
            ? original
            : null;

    /// <summary>
    /// The values a quantized row's chunk id may hold for the chunk <paramref name="chunkId"/>: the id itself, which is what
    /// this store writes, and the storage GUID it is keyed on, which is what earlier versions wrote.
    /// </summary>
    private static List<string> QuantizedChunkKeys(string chunkId)
        => new[] { chunkId, ChunkStorageId.ToStorageGuid(chunkId).ToString() }.Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The values a quantized row's chunk id may hold for the chunk stored in <paramref name="row"/> — its original chunk
    /// id and its row key.
    /// </summary>
    private static List<string> QuantizedChunkKeys(QuantizedVectorEntity row)
        => new[] { OriginalChunkId(row.Metadata) ?? row.Id.ToString(), row.Id.ToString() }.Distinct(StringComparer.Ordinal).ToList();

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
            Id = OriginalChunkId(entity.Metadata) ?? entity.Id.ToString(),
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            TotalChunks = entity.TotalChunks ?? 0,
            Content = entity.Content,
            Embedding = entity.Embedding?.ToArray(),
            TokenCount = entity.TokenCount,
            Metadata = entity.Metadata
        };

        // Include standard fields in metadata for consumer apps (RAG source citation)
        // jsonb comes back as JsonElement values; a consumer reads the same plain values every store returns.
        chunk.Metadata = MetadataValues.ToPlain(chunk.Metadata);
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

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to auto-quantize embedding for chunk {ChunkId}")]
    private static partial void LogAutoQuantizeFailed(ILogger logger, Exception exception, Guid chunkId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to batch quantize embeddings")]
    private static partial void LogBatchQuantizeFailed(ILogger logger, Exception exception);

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
