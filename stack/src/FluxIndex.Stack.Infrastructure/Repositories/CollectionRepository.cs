using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Collection entity.
/// </summary>
public class CollectionRepository : ICollectionRepository
{
    private readonly ServiceDbContext _context;

    public CollectionRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<Collection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Collections
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Collection?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Collections
            .FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
    }

    public async Task<List<Collection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Collections
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Collection> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Collections.OrderBy(c => c.Name);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Collection> AddAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        await _context.Collections.AddAsync(collection, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return collection;
    }

    public async Task UpdateAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        _context.Collections.Update(collection);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var collection = await GetByIdAsync(id, cancellationToken);
        if (collection != null)
        {
            _context.Collections.Remove(collection);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetDocumentCountAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .CountAsync(d => d.CollectionId == collectionId, cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Collections
            .AnyAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Collections.Where(c => c.Name == name);
        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }
        return await query.AnyAsync(cancellationToken);
    }
}
