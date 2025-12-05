using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Service.Infrastructure.Repositories;

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
}
