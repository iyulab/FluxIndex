using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for DocumentChunk entity.
/// </summary>
public class DocumentChunkRepository : IDocumentChunkRepository
{
    private readonly ServiceDbContext _context;

    public DocumentChunkRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<DocumentChunk?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DocumentChunks
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<List<DocumentChunk>> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _context.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DocumentChunk>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        return await _context.DocumentChunks
            .Where(c => ids.Contains(c.Id))
            .Include(c => c.Document)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        await _context.DocumentChunks.AddAsync(chunk, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        await _context.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var chunks = await _context.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        _context.DocumentChunks.RemoveRange(chunks);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(Guid? documentId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.DocumentChunks.AsQueryable();

        if (documentId.HasValue)
        {
            query = query.Where(c => c.DocumentId == documentId.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid chunkId, CancellationToken cancellationToken = default)
    {
        var chunk = await _context.DocumentChunks.FindAsync([chunkId], cancellationToken);
        if (chunk != null)
        {
            _context.DocumentChunks.Remove(chunk);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        _context.DocumentChunks.Update(chunk);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(List<DocumentChunk> Items, int TotalCount)> GetPagedAsync(
        int page = 1,
        int pageSize = 20,
        Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DocumentChunks.AsQueryable();

        if (documentId.HasValue)
        {
            query = query.Where(c => c.DocumentId == documentId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(c => c.Document)
            .OrderBy(c => c.DocumentId)
            .ThenBy(c => c.ChunkIndex)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<List<(DocumentChunk Chunk, double Score)>> SearchByVectorAsync(
        float[] queryEmbedding,
        int limit = 10,
        IEnumerable<Guid>? documentIds = null,
        double minScore = 0.0,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding);

        // Query through ChunkEmbeddings (model-aware embedding storage)
        var query = _context.ChunkEmbeddings
            .Include(e => e.Chunk)
                .ThenInclude(c => c!.Document)
            .Where(e => e.Embedding != null);

        // Apply document filter if specified
        if (documentIds != null && documentIds.Any())
        {
            var docIdsList = documentIds.ToList();
            query = query.Where(e => docIdsList.Contains(e.Chunk!.DocumentId));
        }

        // Execute query with cosine distance ordering and projection
        var results = await query
            .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
            .Take(limit * 2) // Get more to allow for score filtering
            .Select(e => new
            {
                Chunk = e.Chunk!,
                Distance = e.Embedding!.CosineDistance(queryVector)
            })
            .ToListAsync(cancellationToken);

        // Convert distance to similarity score (1 - distance), deduplicate by chunk, and filter by minimum score
        return results
            .GroupBy(r => r.Chunk.Id)
            .Select(g => g.OrderByDescending(r => 1.0 - r.Distance).First())
            .Select(r => (r.Chunk, Score: 1.0 - r.Distance))
            .Where(r => r.Score >= minScore)
            .Take(limit)
            .ToList();
    }
}
