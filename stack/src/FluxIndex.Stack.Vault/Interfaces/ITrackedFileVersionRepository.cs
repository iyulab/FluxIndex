using FluxIndex.Stack.Vault.Entities;

namespace FluxIndex.Stack.Vault.Interfaces;

/// <summary>
/// Repository interface for TrackedFileVersion entity.
/// </summary>
public interface ITrackedFileVersionRepository
{
    Task<TrackedFileVersion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TrackedFileVersion?> GetByTrackedFileIdAndVersionAsync(Guid trackedFileId, int version, CancellationToken cancellationToken = default);
    Task<TrackedFileVersion?> GetLatestByTrackedFileIdAsync(Guid trackedFileId, CancellationToken cancellationToken = default);

    Task<List<TrackedFileVersion>> GetByTrackedFileIdAsync(Guid trackedFileId, CancellationToken cancellationToken = default);

    Task<TrackedFileVersion> AddAsync(TrackedFileVersion version, CancellationToken cancellationToken = default);
    Task UpdateAsync(TrackedFileVersion version, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes old versions exceeding the retention count.
    /// </summary>
    Task<int> CleanupOldVersionsAsync(Guid trackedFileId, int retentionCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total storage size for all versions of a file.
    /// </summary>
    Task<long> GetTotalStorageSizeAsync(Guid trackedFileId, CancellationToken cancellationToken = default);
}
