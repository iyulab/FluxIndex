using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;

namespace FluxIndex.Stack.Vault.Interfaces;

/// <summary>
/// Repository interface for TrackedFile entity.
/// </summary>
public interface ITrackedFileRepository
{
    Task<TrackedFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TrackedFile?> GetBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<TrackedFile?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<List<TrackedFile>> GetByStatusAsync(TrackedFileStatus status, CancellationToken cancellationToken = default);
    Task<List<TrackedFile>> GetByWatchedFolderIdAsync(Guid watchedFolderId, CancellationToken cancellationToken = default);
    Task<List<TrackedFile>> GetStaleFilesAsync(CancellationToken cancellationToken = default);
    Task<List<TrackedFile>> GetOrphanedFilesAsync(CancellationToken cancellationToken = default);

    Task<(List<TrackedFile> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        TrackedFileStatus? status = null,
        Guid? watchedFolderId = null,
        string? searchTerm = null,
        CancellationToken cancellationToken = default);

    Task<TrackedFile> AddAsync(TrackedFile trackedFile, CancellationToken cancellationToken = default);
    Task UpdateAsync(TrackedFile trackedFile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> GetCountByStatusAsync(TrackedFileStatus status, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next file in Queued status for processing.
    /// </summary>
    Task<TrackedFile?> GetNextQueuedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk update status for multiple files.
    /// </summary>
    Task<int> BulkUpdateStatusAsync(
        IEnumerable<Guid> ids,
        TrackedFileStatus newStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks all files from a folder as orphaned if their source no longer exists.
    /// </summary>
    Task<int> MarkOrphanedFilesAsync(Guid watchedFolderId, IEnumerable<string> existingPaths, CancellationToken cancellationToken = default);
}
