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
            Embedding = new Vector(chunk.Embedding.ToArray()),
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata
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
        CancellationToken cancellationToken)
    {
        var queryVector = new Vector(queryEmbedding);

        // Use pgvector's native cosine distance for efficient search
        var candidates = await _context.Vectors
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
        entity.Embedding = new Vector(chunk.Embedding.ToArray());
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = chunk.Metadata;

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

        if (!entities.Any()) return false;

        _context.Vectors.RemoveRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected override async Task<int> CountCoreAsync(CancellationToken cancellationToken)
    {
        return await _context.Vectors.CountAsync(cancellationToken);
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
