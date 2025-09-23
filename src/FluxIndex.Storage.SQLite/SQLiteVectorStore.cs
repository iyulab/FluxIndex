using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// SQLite storage implementation for FluxIndex (development and testing)
/// Vector search performed in memory
/// </summary>
public class SQLiteVectorStore : IVectorStore
{
    private readonly SQLiteDbContext _context;
    private readonly ILogger<SQLiteVectorStore> _logger;
    private readonly SQLiteOptions _options;

    public SQLiteVectorStore(
        SQLiteDbContext context,
        ILogger<SQLiteVectorStore> logger,
        IOptions<SQLiteOptions> options)
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
            Id = id,
            DocumentId = chunk.DocumentId,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Embedding = chunk.Embedding?.ToArray(),
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
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (entity == null) return null;

        return new DocumentChunk
        {
            Id = entity.Id,
            DocumentId = entity.DocumentId,
            ChunkIndex = entity.ChunkIndex,
            Content = entity.Content,
            Embedding = entity.Embedding,
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
            Id = e.Id,
            DocumentId = e.DocumentId,
            ChunkIndex = e.ChunkIndex,
            Content = e.Content,
            Embedding = e.Embedding,
            TokenCount = e.TokenCount,
            Metadata = e.Metadata
        });
    }

    public async Task<IEnumerable<DocumentChunk>> GetChunksByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Vectors
            .Where(v => ids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return entities.Select(e => new DocumentChunk
        {
            Id = e.Id,
            DocumentId = e.DocumentId,
            ChunkIndex = e.ChunkIndex,
            Content = e.Content,
            Embedding = e.Embedding,
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
        // Load all vectors for optimized in-memory search
        var entities = await _context.Vectors
            .Where(v => v.Embedding != null)
            .Select(v => new { v.Id, v.DocumentId, v.ChunkIndex, v.Content, v.Embedding, v.TokenCount, v.Metadata })
            .ToListAsync(cancellationToken);

        if (!entities.Any()) return Enumerable.Empty<DocumentChunk>();

        // Pre-compute query magnitude for optimization
        var queryMagnitude = ComputeMagnitude(queryEmbedding);
        if (queryMagnitude == 0) return Enumerable.Empty<DocumentChunk>();

        // Optimized similarity computation with early termination
        var candidates = new List<(float score, object entity)>();
        var threshold = minScore;

        foreach (var entity in entities)
        {
            if (entity.Embedding == null) continue;

            var score = FastCosineSimilarity(queryEmbedding, entity.Embedding, queryMagnitude);

            if (score >= threshold)
            {
                candidates.Add((score, entity));

                // Dynamic threshold adjustment for better performance
                if (candidates.Count > topK * 2)
                {
                    candidates.Sort((a, b) => b.score.CompareTo(a.score));
                    candidates = candidates.Take(topK).ToList();
                    threshold = candidates.Last().score;
                }
            }
        }

        // Final sorting and selection
        var results = candidates
            .OrderByDescending(c => c.score)
            .Take(topK)
            .Select(c =>
            {
                var e = c.entity;
                var entityType = e.GetType();
                return new DocumentChunk
                {
                    Id = (string)entityType.GetProperty("Id")!.GetValue(e)!,
                    DocumentId = (string)entityType.GetProperty("DocumentId")!.GetValue(e)!,
                    ChunkIndex = (int)entityType.GetProperty("ChunkIndex")!.GetValue(e)!,
                    Content = (string)entityType.GetProperty("Content")!.GetValue(e)!,
                    Embedding = (float[])entityType.GetProperty("Embedding")!.GetValue(e)!,
                    TokenCount = (int)entityType.GetProperty("TokenCount")!.GetValue(e)!,
                    Metadata = (Dictionary<string, object>?)entityType.GetProperty("Metadata")!.GetValue(e)
                };
            });

        return results;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

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
            .AnyAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<DocumentChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Vectors
            .FirstOrDefaultAsync(v => v.Id == chunk.Id, cancellationToken);

        if (entity == null) return false;

        entity.Content = chunk.Content;
        entity.Embedding = chunk.Embedding?.ToArray();
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
        _context.Vectors.RemoveRange(_context.Vectors);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;

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

    /// <summary>
    /// Optimized cosine similarity computation when query magnitude is pre-computed
    /// </summary>
    private static float FastCosineSimilarity(float[] query, float[] candidate, float queryMagnitude)
    {
        if (query.Length != candidate.Length || queryMagnitude == 0) return 0f;

        float dotProduct = 0f;
        float candidateMagnitude = 0f;

        // Single loop for both dot product and candidate magnitude
        for (int i = 0; i < query.Length; i++)
        {
            dotProduct += query[i] * candidate[i];
            candidateMagnitude += candidate[i] * candidate[i];
        }

        candidateMagnitude = (float)Math.Sqrt(candidateMagnitude);
        return candidateMagnitude == 0 ? 0 : dotProduct / (queryMagnitude * candidateMagnitude);
    }

    /// <summary>
    /// Pre-compute vector magnitude for optimization
    /// </summary>
    private static float ComputeMagnitude(float[] vector)
    {
        float sum = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }
        return (float)Math.Sqrt(sum);
    }
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