using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite storage implementation for FluxIndex (development and testing)
/// Vector search performed in memory using VectorMathUtilities
/// </summary>
public class SQLiteVectorStore : VectorStoreBase, IDisposable
{
    private readonly IDbContextFactory<SQLiteDbContext> _contextFactory;
    private readonly SQLiteOptions _options;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Creates the store. Every operation opens its own <see cref="SQLiteDbContext"/> from
    /// <paramref name="contextFactory"/>, so one store instance is safe for concurrent callers.
    /// </summary>
    public SQLiteVectorStore(
        IDbContextFactory<SQLiteDbContext> contextFactory,
        ILogger<SQLiteVectorStore> logger,
        IOptions<SQLiteOptions> options) : base(logger)
    {
        _contextFactory = contextFactory;
        _options = options.Value;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // Per owned table (EnsureCreated is a no-op once any table exists — in fallback mode the
            // sqlite-vec context may have created the database first), plus any nullable column an older
            // database lacks, then the backfill that gives pre-column rows a real TotalChunks. This
            // replaced a hand-written CREATE TABLE that had already drifted from the EF model.
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

    #region VectorStoreBase Core Implementations

    protected override async Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Honour the caller's chunk id (see SQLiteVecVectorStore.StoreCoreAsync); re-storing an id
        // updates the row instead of adding a second one.
        var id = chunk.EnsureId();
        var existing = await context.Vectors
            .AsTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (existing == null)
        {
            context.Vectors.Add(new VectorEntity
            {
                Id = id,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                TotalChunks = chunk.TotalChunks,
                Content = chunk.Content,
                Embedding = chunk.Embedding?.ToArray(),
                TokenCount = chunk.TokenCount,
                Metadata = chunk.Metadata ?? new()
            });
        }
        else
        {
            existing.DocumentId = chunk.DocumentId;
            existing.ChunkIndex = chunk.ChunkIndex;
            existing.TotalChunks = chunk.TotalChunks;
            existing.Content = chunk.Content;
            existing.Embedding = chunk.Embedding?.ToArray();
            existing.TokenCount = chunk.TokenCount;
            existing.Metadata = chunk.Metadata ?? new();
        }

        await context.SaveChangesAsync(cancellationToken);
        return id;
    }

    protected override async Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.Vectors
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        return entity == null ? null : MapToChunk(entity);
    }

    protected override async Task<IEnumerable<ScoredChunk>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Load all vectors for optimized in-memory search
        var entities = await context.Vectors
            .Where(v => v.Embedding != null)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return [];

        // Pre-compute query magnitude for optimization
        var queryMagnitude = ComputeMagnitude(queryEmbedding);
        if (queryMagnitude == 0) return [];

        // Compute similarities using centralized utilities.
        // Metadata filters MUST be applied before the topK*2 trim below — otherwise
        // higher-scoring non-matching chunks crowd matching ones out of the window.
        var results = new List<ScoredChunk>();
        var matcher = MetadataFilterMatcher.Compile(filters);

        foreach (var entity in entities)
        {
            if (entity.Embedding == null) continue;

            var chunk = MapToChunk(entity);
            if (!matcher.Matches(chunk.DocumentId, chunk.Metadata))
                continue;

            var score = ComputeFastCosineSimilarity(queryEmbedding, entity.Embedding, queryMagnitude);
            results.Add(new ScoredChunk(chunk, score));
        }

        // Return all results - minScore filtering and sorting handled by base class
        return results.OrderByDescending(r => r.Score).Take(topK * 2);
    }

    protected override async Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // AsTracking: NoTracking context, and Remove() on a detached instance throws when the
        // same row is already tracked (a Store in the same scope leaves it so).
        var entity = await context.Vectors
            .AsTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (entity == null) return false;

        context.Vectors.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // AsTracking regardless of how this context happens to be registered — the sibling
        // stores are NoTracking for read performance, and under that registration an untracked
        // entity's mutations are dropped without a word.
        var entity = await context.Vectors.AsTracking()
            .FirstOrDefaultAsync(v => v.Id == chunk.Id, cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding?.ToArray();
        entity.TokenCount = chunk.TokenCount;
        entity.TotalChunks = chunk.TotalChunks;
        entity.Metadata = chunk.Metadata ?? new();

        var hadChanges = context.ChangeTracker.HasChanges();
        var written = await context.SaveChangesAsync(cancellationToken);
        return !hadChanges || written > 0;
    }

    protected override async Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.Vectors
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.ChunkIndex)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    protected override async Task<bool> DeleteByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.Vectors
            .AsTracking()
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return false;

        context.Vectors.RemoveRange(entities);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<int> CountCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Vectors.CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Vectors
            .Select(v => v.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    protected override async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Vectors.RemoveRange(context.Vectors.AsTracking());
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<int> DeleteByFilterAsync(
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count == 0)
            throw new ArgumentException(
                "Filter must contain at least one key/value; use ClearAsync to remove all vectors.",
                nameof(filters));

        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.Vectors.AsTracking().ToListAsync(cancellationToken);
        var deleteMatcher = MetadataFilterMatcher.Compile(filters);
        var matched = entities
            .Where(v => deleteMatcher.Matches(v.DocumentId, MapToChunk(v).Metadata))
            .ToList();

        if (matched.Count == 0)
            return 0;

        context.Vectors.RemoveRange(matched);
        await context.SaveChangesAsync(cancellationToken);
        return matched.Count;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The primary key cannot be changed in place through EF Core, so each row is re-inserted under its new id and
    /// the old row deleted — in one <c>SaveChanges</c>, which is one transaction. The embedding column is copied as
    /// stored.
    /// </remarks>
    public override async Task<int> ReassignDocumentAsync(
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

        foreach (var row in rows)
        {
            var newId = chunkIdMap[row.Id];
            context.Vectors.Add(new VectorEntity
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

        context.Vectors.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    #endregion

    #region Overrides for Batch Optimization

    public override async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var idList = ids.ToList();
        var entities = await context.Vectors
            .Where(v => idList.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    public override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(id)) return false;
        return await context.Vectors.AnyAsync(v => v.Id == id, cancellationToken);
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

    #region Private Helper Methods

    private DocumentChunk MapToChunk(VectorEntity entity)
    {
        var chunk = new DocumentChunk
        {
            Id = entity.Id,
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            // Null only on a schema managed outside FluxIndex that never ran the backfill.
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

        RestoreRichMetadata(chunk);
        return chunk;
    }

    #endregion
}

/// <summary>
/// Vector entity for SQLite storage
/// </summary>
public class VectorEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    /// <summary>
    /// Number of chunks in the document (<c>DocumentChunk.TotalChunks</c>). Nullable so the column can be
    /// added to an existing database in place; <see cref="TotalChunksBackfill"/> fills older rows from a
    /// per-document count right after provisioning.
    /// </summary>
    public int? TotalChunks { get; set; }
    public string Content { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
