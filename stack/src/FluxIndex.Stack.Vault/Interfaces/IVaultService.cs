using FluxIndex.Stack.Vault.Entities;
using FluxIndex.Stack.Vault.Enums;

namespace FluxIndex.Stack.Vault.Interfaces;

/// <summary>
/// Core service interface for vault operations.
/// </summary>
public interface IVaultService
{
    // Watched Folder Operations
    Task<WatchedFolder> AddWatchedFolderAsync(
        string path,
        string? name = null,
        bool isRecursive = true,
        bool autoMemorize = true,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default);

    Task RemoveWatchedFolderAsync(Guid folderId, bool removeTrackedFiles = true, CancellationToken cancellationToken = default);
    Task PauseWatchingAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task ResumeWatchingAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<WatchedFolder> UpdateFolderPathAsync(Guid folderId, string newPath, CancellationToken cancellationToken = default);
    Task<WatchedFolder?> GetWatchedFolderAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<List<WatchedFolder>> GetAllWatchedFoldersAsync(CancellationToken cancellationToken = default);

    // File Operations
    Task<TrackedFile> MemorizeFileAsync(string sourcePath, Guid? watchedFolderId = null, CancellationToken cancellationToken = default);
    Task UnmemorizeFileAsync(Guid fileId, bool deleteArtifacts = true, CancellationToken cancellationToken = default);
    Task<TrackedFile> ReprocessFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<TrackedFile?> GetTrackedFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<TrackedFile?> GetTrackedFileByPathAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<List<TrackedFile>> GetTrackedFilesByFolderAsync(Guid folderId, CancellationToken cancellationToken = default);

    // Scan Operations
    Task<ScanResult> ScanFolderAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<SyncResult> SyncAllAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupOrphanedFilesAsync(CancellationToken cancellationToken = default);

    // Status
    Task<VaultStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a folder scan operation.
/// </summary>
public class ScanResult
{
    public Guid FolderId { get; init; }
    public int TotalFilesFound { get; init; }
    public int NewFilesQueued { get; init; }
    public int ChangedFilesQueued { get; init; }
    public int OrphanedFilesDetected { get; init; }
    public int SkippedFiles { get; init; }
    public List<string> Errors { get; init; } = new();
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Result of a full sync operation.
/// </summary>
public class SyncResult
{
    public int FoldersScanned { get; init; }
    public int FilesProcessed { get; init; }
    public int FilesQueued { get; init; }
    public int OrphanedFilesCleaned { get; init; }
    public List<string> Errors { get; init; } = new();
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Current vault status.
/// </summary>
public class VaultStatus
{
    public bool IsEnabled { get; init; }
    public int ActiveWatchers { get; init; }
    public int TotalTrackedFiles { get; init; }
    public int MemorizedFiles { get; init; }
    public int QueuedFiles { get; init; }
    public int ProcessingFiles { get; init; }
    public int StaleFiles { get; init; }
    public int OrphanedFiles { get; init; }
    public int ErrorFiles { get; init; }
    public DateTime? LastSyncAt { get; init; }
}
