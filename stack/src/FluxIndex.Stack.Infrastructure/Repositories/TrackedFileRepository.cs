using FluxIndex.Stack.Infrastructure.Data;
using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;
using FluxIndex.Stack.Vault.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for TrackedFile entity.
/// </summary>
public class TrackedFileRepository : ITrackedFileRepository
{
    private readonly ServiceDbContext _context;

    public TrackedFileRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<TrackedFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Include(f => f.WatchedFolder)
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<TrackedFile?> GetBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Include(f => f.WatchedFolder)
            .FirstOrDefaultAsync(f => f.SourcePath == sourcePath, cancellationToken);
    }

    public async Task<TrackedFile?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Include(f => f.WatchedFolder)
            .FirstOrDefaultAsync(f => f.DocumentId == documentId, cancellationToken);
    }

    public async Task<List<TrackedFile>> GetByStatusAsync(TrackedFileStatus status, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Where(f => f.Status == status)
            .Include(f => f.WatchedFolder)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TrackedFile>> GetByWatchedFolderIdAsync(Guid watchedFolderId, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Where(f => f.WatchedFolderId == watchedFolderId)
            .OrderBy(f => f.FileName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TrackedFile>> GetStaleFilesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Where(f => f.Status == TrackedFileStatus.Stale)
            .Include(f => f.WatchedFolder)
            .OrderBy(f => f.LastSyncedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TrackedFile>> GetOrphanedFilesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Where(f => f.Status == TrackedFileStatus.Orphaned)
            .Include(f => f.WatchedFolder)
            .OrderBy(f => f.LastSyncedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<TrackedFile> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        TrackedFileStatus? status = null,
        Guid? watchedFolderId = null,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TrackedFiles.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(f => f.Status == status.Value);
        }

        if (watchedFolderId.HasValue)
        {
            query = query.Where(f => f.WatchedFolderId == watchedFolderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(f =>
                f.FileName.Contains(searchTerm) ||
                f.SourcePath.Contains(searchTerm));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(f => f.WatchedFolder)
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<TrackedFile> AddAsync(TrackedFile trackedFile, CancellationToken cancellationToken = default)
    {
        await _context.TrackedFiles.AddAsync(trackedFile, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return trackedFile;
    }

    public async Task UpdateAsync(TrackedFile trackedFile, CancellationToken cancellationToken = default)
    {
        _context.TrackedFiles.Update(trackedFile);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var file = await _context.TrackedFiles
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file != null)
        {
            _context.TrackedFiles.Remove(file);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetCountByStatusAsync(TrackedFileStatus status, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .CountAsync(f => f.Status == status, cancellationToken);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles.CountAsync(cancellationToken);
    }

    public async Task<bool> ExistsBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .AnyAsync(f => f.SourcePath == sourcePath, cancellationToken);
    }

    public async Task<TrackedFile?> GetNextQueuedAsync(CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFiles
            .Where(f => f.Status == TrackedFileStatus.Queued)
            .OrderBy(f => f.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> BulkUpdateStatusAsync(
        IEnumerable<Guid> ids,
        TrackedFileStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return 0;

        return await _context.TrackedFiles
            .Where(f => idList.Contains(f.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(f => f.Status, newStatus),
                cancellationToken);
    }

    public async Task<int> MarkOrphanedFilesAsync(
        Guid watchedFolderId,
        IEnumerable<string> existingPaths,
        CancellationToken cancellationToken = default)
    {
        var existingPathSet = existingPaths.ToHashSet();

        // Get all tracked files for this folder that aren't already orphaned or removed
        var trackedFiles = await _context.TrackedFiles
            .Where(f => f.WatchedFolderId == watchedFolderId &&
                        f.Status != TrackedFileStatus.Orphaned &&
                        f.Status != TrackedFileStatus.Removed)
            .ToListAsync(cancellationToken);

        var orphanedCount = 0;
        foreach (var file in trackedFiles)
        {
            if (!existingPathSet.Contains(file.SourcePath))
            {
                file.MarkAsOrphaned();
                orphanedCount++;
            }
        }

        if (orphanedCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return orphanedCount;
    }
}
