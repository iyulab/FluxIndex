using FluxIndex.Stack.Infrastructure.Data;
using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FluxIndex.Stack.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for TrackedFileVersion entity.
/// </summary>
public class TrackedFileVersionRepository : ITrackedFileVersionRepository
{
    private readonly ServiceDbContext _context;

    public TrackedFileVersionRepository(ServiceDbContext context)
    {
        _context = context;
    }

    public async Task<TrackedFileVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFileVersions
            .Include(v => v.TrackedFile)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<TrackedFileVersion?> GetByTrackedFileIdAndVersionAsync(
        Guid trackedFileId,
        int version,
        CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFileVersions
            .FirstOrDefaultAsync(v => v.TrackedFileId == trackedFileId && v.Version == version, cancellationToken);
    }

    public async Task<TrackedFileVersion?> GetLatestByTrackedFileIdAsync(
        Guid trackedFileId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFileVersions
            .Where(v => v.TrackedFileId == trackedFileId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<TrackedFileVersion>> GetByTrackedFileIdAsync(
        Guid trackedFileId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFileVersions
            .Where(v => v.TrackedFileId == trackedFileId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<TrackedFileVersion> AddAsync(
        TrackedFileVersion version,
        CancellationToken cancellationToken = default)
    {
        await _context.TrackedFileVersions.AddAsync(version, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return version;
    }

    public async Task UpdateAsync(TrackedFileVersion version, CancellationToken cancellationToken = default)
    {
        _context.TrackedFileVersions.Update(version);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var version = await _context.TrackedFileVersions
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (version != null)
        {
            _context.TrackedFileVersions.Remove(version);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> CleanupOldVersionsAsync(
        Guid trackedFileId,
        int retentionCount,
        CancellationToken cancellationToken = default)
    {
        // Get all versions ordered by version number descending
        var versions = await _context.TrackedFileVersions
            .Where(v => v.TrackedFileId == trackedFileId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        // Keep the latest N versions
        var versionsToDelete = versions.Skip(retentionCount).ToList();

        if (versionsToDelete.Count > 0)
        {
            _context.TrackedFileVersions.RemoveRange(versionsToDelete);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return versionsToDelete.Count;
    }

    public async Task<long> GetTotalStorageSizeAsync(
        Guid trackedFileId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TrackedFileVersions
            .Where(v => v.TrackedFileId == trackedFileId)
            .SumAsync(v => v.FileSize, cancellationToken);
    }
}
