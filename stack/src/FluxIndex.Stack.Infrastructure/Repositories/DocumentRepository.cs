using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Document entity.
/// </summary>
public class DocumentRepository : IDocumentRepository
{
    private readonly ServiceDbContext _context;

    public DocumentRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .Include(d => d.Collection)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<List<Document>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return new List<Document>();

        return await _context.Documents
            .Where(d => idList.Contains(d.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<Document?> GetByIdWithChunksAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .Include(d => d.Collection)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<List<Document>> GetByCollectionIdAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .Where(d => d.CollectionId == collectionId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Document> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        Guid? collectionId = null,
        DocumentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Documents.AsQueryable();

        if (collectionId.HasValue)
        {
            query = query.Where(d => d.CollectionId == collectionId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        query = query.OrderByDescending(d => d.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(d => d.Collection)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        await _context.Documents.AddAsync(document, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    public async Task UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        _context.Documents.Update(document);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await GetByIdAsync(id, cancellationToken);
        if (document != null)
        {
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Documents.AnyAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<bool> ContentHashExistsAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        return await _context.Documents.AnyAsync(d => d.ContentHash == contentHash, cancellationToken);
    }

    public async Task<int> GetCountAsync(Guid? collectionId = null, DocumentStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Documents.AsQueryable();

        if (collectionId.HasValue)
        {
            query = query.Where(d => d.CollectionId == collectionId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<long> GetTotalFileSizeAsync(Guid? collectionId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Documents.AsQueryable();

        if (collectionId.HasValue)
        {
            query = query.Where(d => d.CollectionId == collectionId.Value);
        }

        return await query
            .Where(d => d.FileSize.HasValue)
            .SumAsync(d => d.FileSize!.Value, cancellationToken);
    }

    public async Task<List<(string SourceType, int Count)>> GetGroupedBySourceTypeAsync(
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Documents.AsQueryable();

        if (collectionId.HasValue)
        {
            query = query.Where(d => d.CollectionId == collectionId.Value);
        }

        var grouped = await query
            .GroupBy(d => d.SourceType ?? "unknown")
            .Select(g => new { SourceType = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return grouped.Select(g => (g.SourceType, g.Count)).ToList();
    }

    public async Task<List<(DateTime Date, int Count)>> GetDailyUploadTrendsAsync(
        int days,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        var fromDate = DateTime.UtcNow.Date.AddDays(-days + 1);
        var query = _context.Documents.AsQueryable();

        if (collectionId.HasValue)
        {
            query = query.Where(d => d.CollectionId == collectionId.Value);
        }

        var grouped = await query
            .Where(d => d.CreatedAt >= fromDate)
            .GroupBy(d => d.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        // Fill in missing days with 0 count
        var result = new List<(DateTime Date, int Count)>();
        for (var date = fromDate; date <= DateTime.UtcNow.Date; date = date.AddDays(1))
        {
            var existing = grouped.FirstOrDefault(g => g.Date == date);
            result.Add((date, existing?.Count ?? 0));
        }

        return result;
    }
}
