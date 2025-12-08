using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ChunkEmbedding entity.
/// Provides model-aware embedding storage and retrieval.
/// </summary>
public class ChunkEmbeddingRepository : IChunkEmbeddingRepository
{
    private readonly ServiceDbContext _context;

    public ChunkEmbeddingRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<ChunkEmbedding?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ChunkEmbeddings
            .Include(e => e.Chunk)
            .Include(e => e.Model)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<ChunkEmbedding?> GetByChunkAndModelAsync(
        Guid chunkId,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ChunkEmbeddings
            .Include(e => e.Chunk)
            .FirstOrDefaultAsync(
                e => e.ChunkId == chunkId && e.EmbeddingModelId == embeddingModelId,
                cancellationToken);
    }

    public async Task<List<ChunkEmbedding>> GetByChunkIdAsync(
        Guid chunkId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ChunkEmbeddings
            .Include(e => e.Model)
            .Where(e => e.ChunkId == chunkId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ChunkEmbedding>> GetByModelIdAsync(
        Guid embeddingModelId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ChunkEmbeddings
            .Include(e => e.Chunk)
            .Where(e => e.EmbeddingModelId == embeddingModelId);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<ChunkEmbedding>> GetByChunkIdsAndModelAsync(
        IEnumerable<Guid> chunkIds,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default)
    {
        var chunkIdList = chunkIds.ToList();
        return await _context.ChunkEmbeddings
            .Include(e => e.Chunk)
            .Where(e => chunkIdList.Contains(e.ChunkId) && e.EmbeddingModelId == embeddingModelId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ChunkEmbedding embedding, CancellationToken cancellationToken = default)
    {
        await _context.ChunkEmbeddings.AddAsync(embedding, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<ChunkEmbedding> embeddings, CancellationToken cancellationToken = default)
    {
        await _context.ChunkEmbeddings.AddRangeAsync(embeddings, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ChunkEmbedding embedding, CancellationToken cancellationToken = default)
    {
        _context.ChunkEmbeddings.Update(embedding);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var embedding = await _context.ChunkEmbeddings.FindAsync([id], cancellationToken);
        if (embedding != null)
        {
            _context.ChunkEmbeddings.Remove(embedding);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByChunkIdAsync(Guid chunkId, CancellationToken cancellationToken = default)
    {
        var embeddings = await _context.ChunkEmbeddings
            .Where(e => e.ChunkId == chunkId)
            .ToListAsync(cancellationToken);

        if (embeddings.Any())
        {
            _context.ChunkEmbeddings.RemoveRange(embeddings);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByModelIdAsync(Guid embeddingModelId, CancellationToken cancellationToken = default)
    {
        var embeddings = await _context.ChunkEmbeddings
            .Where(e => e.EmbeddingModelId == embeddingModelId)
            .ToListAsync(cancellationToken);

        if (embeddings.Any())
        {
            _context.ChunkEmbeddings.RemoveRange(embeddings);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByChunkAndModelAsync(
        Guid chunkId,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default)
    {
        var embedding = await _context.ChunkEmbeddings
            .FirstOrDefaultAsync(
                e => e.ChunkId == chunkId && e.EmbeddingModelId == embeddingModelId,
                cancellationToken);

        if (embedding != null)
        {
            _context.ChunkEmbeddings.Remove(embedding);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<(ChunkEmbedding Embedding, double Score)>> SearchByVectorAsync(
        float[] queryEmbedding,
        Guid embeddingModelId,
        int limit = 10,
        IEnumerable<Guid>? documentIds = null,
        double minScore = 0.0,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding);

        // Start with base query filtering by model
        var query = _context.ChunkEmbeddings
            .Include(e => e.Chunk)
                .ThenInclude(c => c.Document)
            .Where(e => e.EmbeddingModelId == embeddingModelId && e.Embedding != null);

        // Apply document filter if specified
        if (documentIds != null && documentIds.Any())
        {
            var docIdsList = documentIds.ToList();
            query = query.Where(e => docIdsList.Contains(e.Chunk.DocumentId));
        }

        // Execute query with cosine distance ordering and projection
        var results = await query
            .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
            .Take(limit * 2) // Get more to allow for score filtering
            .Select(e => new
            {
                Embedding = e,
                Distance = e.Embedding!.CosineDistance(queryVector)
            })
            .ToListAsync(cancellationToken);

        // Convert distance to similarity score (1 - distance) and filter by minimum score
        return results
            .Select(r => (r.Embedding, Score: 1.0 - r.Distance))
            .Where(r => r.Score >= minScore)
            .Take(limit)
            .ToList();
    }

    public async Task<int> GetCountByModelAsync(Guid embeddingModelId, CancellationToken cancellationToken = default)
    {
        return await _context.ChunkEmbeddings
            .CountAsync(e => e.EmbeddingModelId == embeddingModelId, cancellationToken);
    }

    public async Task<List<Guid>> GetChunkIdsWithoutEmbeddingAsync(
        Guid embeddingModelId,
        IEnumerable<Guid>? documentIds = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        // Get all chunk IDs that have embeddings for this model
        var chunkIdsWithEmbedding = _context.ChunkEmbeddings
            .Where(e => e.EmbeddingModelId == embeddingModelId)
            .Select(e => e.ChunkId);

        // Start with all chunks
        var query = _context.DocumentChunks.AsQueryable();

        // Apply document filter if specified
        if (documentIds != null && documentIds.Any())
        {
            var docIdsList = documentIds.ToList();
            query = query.Where(c => docIdsList.Contains(c.DocumentId));
        }

        // Get chunks that don't have embeddings for this model
        var chunkIdsQuery = query
            .Where(c => !chunkIdsWithEmbedding.Contains(c.Id))
            .Select(c => c.Id);

        if (limit.HasValue)
        {
            chunkIdsQuery = chunkIdsQuery.Take(limit.Value);
        }

        return await chunkIdsQuery.ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Guid chunkId,
        Guid embeddingModelId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ChunkEmbeddings
            .AnyAsync(
                e => e.ChunkId == chunkId && e.EmbeddingModelId == embeddingModelId,
                cancellationToken);
    }
}
