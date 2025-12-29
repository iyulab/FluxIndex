using FluxIndex.Stack.Infrastructure.Data;
using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;
using FluxIndex.Stack.Vault.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for WatchedFolder entity.
/// </summary>
public class WatchedFolderRepository : IWatchedFolderRepository
{
    private readonly ServiceDbContext _context;

    public WatchedFolderRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<WatchedFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.WatchedFolders
            .Include(f => f.TrackedFiles)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<WatchedFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _context.WatchedFolders
            .Include(f => f.TrackedFiles)
            .FirstOrDefaultAsync(f => f.Path == path, cancellationToken);
    }

    public async Task<List<WatchedFolder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.WatchedFolders
            .Include(f => f.TrackedFiles)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<WatchedFolder>> GetByStatusAsync(WatcherStatus status, CancellationToken cancellationToken = default)
    {
        return await _context.WatchedFolders
            .Where(f => f.Status == status)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<WatchedFolder>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.WatchedFolders
            .Where(f => f.Status == WatcherStatus.Active)
            .Include(f => f.TrackedFiles)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<WatchedFolder> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        WatcherStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.WatchedFolders.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(f => f.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(f => f.TrackedFiles)
            .OrderBy(f => f.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<WatchedFolder> AddAsync(WatchedFolder watchedFolder, CancellationToken cancellationToken = default)
    {
        await _context.WatchedFolders.AddAsync(watchedFolder, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return watchedFolder;
    }

    public async Task UpdateAsync(WatchedFolder watchedFolder, CancellationToken cancellationToken = default)
    {
        _context.WatchedFolders.Update(watchedFolder);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await _context.WatchedFolders
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (folder != null)
        {
            _context.WatchedFolders.Remove(folder);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _context.WatchedFolders
            .AnyAsync(f => f.Path == path, cancellationToken);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.WatchedFolders.CountAsync(cancellationToken);
    }
}
