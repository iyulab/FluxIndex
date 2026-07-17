using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL with pgvector storage implementation for FluxIndex.
/// Uses pgvector's native cosine distance for efficient vector search.
/// </summary>
public class PostgreSQLVectorStore : VectorStoreBase
{
    private readonly FluxIndexDbContext _context;
    private readonly PostgreSQLOptions _options;

    public PostgreSQLVectorStore(
        FluxIndexDbContext context,
        ILogger<PostgreSQLVectorStore> logger,
        IOptions<PostgreSQLOptions> options) : base(logger)
    {
        _context = context;
        _options = options.Value;
    }

    #region VectorStoreBase Core Implementations

    protected override async Task<string> StoreCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        var entity = new VectorEntity
        {
            Id = Guid.Parse(id),
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding is not null ? new Vector(chunk.Embedding.ToArray()) : new Vector(Array.Empty<float>()),
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata ?? new()
        };

        _context.Vectors.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return id;
    }

    protected override async Task<DocumentChunk?> GetCoreAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(id), cancellationToken);

        return entity == null ? null : MapToChunk(entity);
    }

    protected override async Task<IEnumerable<VectorSearchResult>> SearchCoreAsync(
        float[] queryEmbedding,
        int topK,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        var queryVector = new Vector(queryEmbedding);

        // Push metadata filters down to SQL (jsonb @> containment, GIN-indexable) BEFORE the
        // candidate trim — otherwise higher-scoring non-matching rows crowd matching rows out of
        // the topK*3 window (multi-tenant recall loss).
        var query = _context.Vectors.AsQueryable();
        if (filters is { Count: > 0 })
        {
            var filterJson = System.Text.Json.JsonSerializer.Serialize(filters);
            query = query.Where(v => EF.Functions.JsonContains(v.Metadata, filterJson));
        }

        // Use pgvector's native cosine distance for efficient search
        var candidates = await query
            .OrderBy(v => v.Embedding.CosineDistance(queryVector))
            .Take(topK * 3) // Get 3x results to filter by similarity
            .Select(v => new
            {
                Distance = v.Embedding.CosineDistance(queryVector),
                Entity = v
            })
            .ToListAsync(cancellationToken);

        // Convert cosine distance (0-2) to cosine similarity (1 to -1)
        return candidates.Select(c => new VectorSearchResult(
            MapToChunk(c.Entity),
            VectorMathUtilities.DistanceToSimilarity((float)c.Distance, DistanceType.Cosine)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Executes a single SQL DELETE using jsonb containment (<c>Metadata @&gt; filters</c>),
    /// matching the same semantics as the search-path filter pushdown.
    /// </remarks>
    public override async Task<int> DeleteByFilterAsync(
        Dictionary<string, object> filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (filters.Count == 0)
            throw new ArgumentException(
                "Filter must contain at least one key/value; use ClearAsync to remove all vectors.",
                nameof(filters));

        var filterJson = System.Text.Json.JsonSerializer.Serialize(filters);
        return await _context.Vectors
            .Where(v => EF.Functions.JsonContains(v.Metadata, filterJson))
            .ExecuteDeleteAsync(cancellationToken);
    }

    protected override async Task<bool> DeleteCoreAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(id), cancellationToken);

        if (entity == null) return false;

        _context.Vectors.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<bool> UpdateCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(chunk.Id), cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding is not null ? new Vector(chunk.Embedding.ToArray()) : new Vector(Array.Empty<float>());
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = chunk.Metadata ?? new();

        // Explicitly mark Metadata as modified for EF Core change tracking
        _context.Entry(entity).Property(e => e.Metadata).IsModified = true;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<IEnumerable<DocumentChunk>> GetByDocumentIdCoreAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
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
        return await _context.Vectors.CountAsync(cancellationToken);
    }

    public override async Task<int> GetDistinctDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Vectors
            .Select(v => v.DocumentId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    protected override async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE vectors", cancellationToken);
    }

    #endregion

    #region Overrides for Batch Optimization

    public override async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var guids = ids.Select(Guid.Parse).ToList();
        var entities = await _context.Vectors
            .Where(v => guids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(MapToChunk);
    }

    public override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return await _context.Vectors.AnyAsync(v => v.Id == Guid.Parse(id), cancellationToken);
    }

    #endregion

    #region Private Helper Methods

    private DocumentChunk MapToChunk(VectorEntity entity)
    {
        var chunk = new DocumentChunk
        {
            Id = entity.Id.ToString(),
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            Content = entity.Content,
            Embedding = entity.Embedding.ToArray(),
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
/// Vector entity for PostgreSQL storage
/// </summary>
public class VectorEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public Vector Embedding { get; set; } = new Vector(Array.Empty<float>());
    public int TokenCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
