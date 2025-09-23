using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL with pgvector storage implementation for FluxIndex
/// </summary>
public class PostgreSQLVectorStore : IVectorStore
{
    private readonly FluxIndexDbContext _context;
    private readonly ILogger<PostgreSQLVectorStore> _logger;
    private readonly PostgreSQLOptions _options;

    public PostgreSQLVectorStore(
        FluxIndexDbContext context,
        ILogger<PostgreSQLVectorStore> logger,
        IOptions<PostgreSQLOptions> options)
    {
        _context = context;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string> StoreAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
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

    public async Task<IEnumerable<string>> StoreBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        foreach (var chunk in chunks)
        {
            var id = await StoreAsync(chunk, cancellationToken);
            ids.Add(id);
        }
        return ids;
    }

    public async Task<DocumentChunk?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(id), cancellationToken);

        if (entity == null) return null;

        return new DocumentChunk
        {
            Id = entity.Id.ToString(),
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            Content = entity.Content,
            Embedding = entity.Embedding.ToArray(),
            TokenCount = entity.TokenCount,
            Metadata = entity.Metadata
        };
    }

    public async Task<IEnumerable<DocumentChunk>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.ChunkIndex)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new DocumentChunk
        {
            Id = e.Id.ToString(),
            DocumentId = e.DocumentId,
            ChunkIndex = e.ChunkIndex,
            Content = e.Content,
            Embedding = e.Embedding.ToArray(),
            TokenCount = e.TokenCount,
            Metadata = e.Metadata
        });
    }

    public async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var guids = ids.Select(Guid.Parse).ToList();
        var entities = await _context.Vectors
            .Where(v => guids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(e => new DocumentChunk
        {
            Id = e.Id.ToString(),
            DocumentId = e.DocumentId,
            ChunkIndex = e.ChunkIndex,
            Content = e.Content,
            Embedding = e.Embedding.ToArray(),
            TokenCount = e.TokenCount,
            Metadata = e.Metadata
        });
    }

    public async Task<IEnumerable<DocumentChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding);

        // Get more results than needed to apply similarity filtering
        var candidates = await _context.Vectors
            .OrderBy(v => v.Embedding.CosineDistance(queryVector))
            .Take(topK * 3) // Get 3x results to filter by similarity
            .Select(v => new
            {
                Distance = v.Embedding.CosineDistance(queryVector),
                Chunk = new DocumentChunk
                {
                    Id = v.Id.ToString(),
                    DocumentId = v.DocumentId,
                    ChunkIndex = v.ChunkIndex,
                    Content = v.Content,
                    Embedding = v.Embedding.ToArray(),
                    TokenCount = v.TokenCount,
                    Metadata = v.Metadata
                }
            })
            .ToListAsync(cancellationToken);

        // Convert cosine distance (0-2) to cosine similarity (1-0) and filter
        var results = candidates
            .Select(c => new {
                Chunk = c.Chunk,
                Similarity = 1.0 - c.Distance // Convert distance to similarity
            })
            .Where(r => r.Similarity >= minScore)
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .Select(r => r.Chunk);

        return results;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(id), cancellationToken);

        if (entity == null) return false;

        _context.Vectors.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Vectors
            .Where(v => v.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (!entities.Any()) return false;

        _context.Vectors.RemoveRange(entities);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Vectors
            .AnyAsync(v => v.Id == Guid.Parse(id), cancellationToken);
    }

    public async Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == Guid.Parse(chunk.Id), cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = new Vector(chunk.Embedding.ToArray());
        entity.TokenCount = chunk.TokenCount;
        entity.Metadata = chunk.Metadata;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Vectors.CountAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await CountAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE vectors", cancellationToken);
    }
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