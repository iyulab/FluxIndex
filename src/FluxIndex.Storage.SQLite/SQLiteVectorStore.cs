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
    private readonly SQLiteDbContext _context;
    private readonly SQLiteOptions _options;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SQLiteVectorStore(
        SQLiteDbContext context,
        ILogger<SQLiteVectorStore> logger,
        IOptions<SQLiteOptions> options) : base(logger)
    {
        _context = context;
        _options = options.Value;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // EnsureCreatedAsync는 데이터베이스가 이미 존재하면 테이블을 생성하지 않음
            // fallback 모드에서 SQLiteVecDbContext가 먼저 데이터베이스를 생성했을 수 있음
            await _context.Database.EnsureCreatedAsync(cancellationToken);

            // CREATE TABLE IF NOT EXISTS를 사용하여 항상 테이블 존재 보장
            // fallback 모드에서 SQLiteVecDbContext가 먼저 DB를 생성한 경우를 처리
            await EnsureVectorsTableExistsAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// vectors 테이블이 존재하도록 보장 (CREATE TABLE IF NOT EXISTS 사용)
    /// </summary>
    private async Task EnsureVectorsTableExistsAsync(CancellationToken cancellationToken)
    {
        // CREATE TABLE IF NOT EXISTS를 사용하여 테이블이 이미 존재하면 무시
        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS vectors (
                Id TEXT PRIMARY KEY NOT NULL,
                DocumentId TEXT NOT NULL,
                ChunkIndex INTEGER NOT NULL,
                Content TEXT NOT NULL,
                Embedding TEXT,
                TokenCount INTEGER NOT NULL,
                Metadata TEXT
            )";

        var createIndexSql1 = "CREATE INDEX IF NOT EXISTS IX_vectors_DocumentId ON vectors(DocumentId)";
        var createIndexSql2 = "CREATE INDEX IF NOT EXISTS IX_vectors_ChunkIndex ON vectors(ChunkIndex)";

        await _context.Database.ExecuteSqlRawAsync(createTableSql, cancellationToken);
        await _context.Database.ExecuteSqlRawAsync(createIndexSql1, cancellationToken);
        await _context.Database.ExecuteSqlRawAsync(createIndexSql2, cancellationToken);
    }

    #region VectorStoreBase Core Implementations

    protected override async Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var id = Guid.NewGuid().ToString();
        var entity = new VectorEntity
        {
            Id = id,
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding?.ToArray(),
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata ?? new()
        };

        _context.Vectors.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return id;
    }

    protected override async Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        return entity == null ? null : MapToChunk(entity);
    }

    protected override async Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        // Load all vectors for optimized in-memory search
        var entities = await _context.Vectors
            .Where(v => v.Embedding != null)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return [];

        // Pre-compute query magnitude for optimization
        var queryMagnitude = ComputeMagnitude(queryEmbedding);
        if (queryMagnitude == 0) return [];

        // Compute similarities using centralized utilities
        var results = new List<VectorSearchResult>();

        foreach (var entity in entities)
        {
            if (entity.Embedding == null) continue;

            var score = ComputeFastCosineSimilarity(queryEmbedding, entity.Embedding, queryMagnitude);
            var chunk = MapToChunk(entity);
            results.Add(new VectorSearchResult(chunk, score));
        }

        // Return all results - filtering and sorting handled by base class
        return results.OrderByDescending(r => r.Score).Take(topK * 2);
    }

    protected override async Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (entity == null) return false;

        _context.Vectors.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == chunk.Id, cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding?.ToArray();
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = chunk.Metadata ?? new();

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entities = await _context.Vectors
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

        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0) return false;

        _context.Vectors.RemoveRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<int> CountCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await _context.Vectors.CountAsync(cancellationToken);
    }

    protected override async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        _context.Vectors.RemoveRange(_context.Vectors);
        await _context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Overrides for Batch Optimization

    public override async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
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

    public override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(id)) return false;
        return await _context.Vectors.AnyAsync(v => v.Id == id, cancellationToken);
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
    public string Content { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
