using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;

namespace FluxIndex.Stack.Vault.Interfaces;

/// <summary>
/// Repository interface for WatchedFolder entity.
/// </summary>
public interface IWatchedFolderRepository
{
    Task<WatchedFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WatchedFolder?> GetByPathAsync(string path, CancellationToken cancellationToken = default);

    Task<List<WatchedFolder>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<WatchedFolder>> GetByStatusAsync(WatcherStatus status, CancellationToken cancellationToken = default);
    Task<List<WatchedFolder>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<(List<WatchedFolder> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        WatcherStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<WatchedFolder> AddAsync(WatchedFolder watchedFolder, CancellationToken cancellationToken = default);
    Task UpdateAsync(WatchedFolder watchedFolder, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByPathAsync(string path, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
}
