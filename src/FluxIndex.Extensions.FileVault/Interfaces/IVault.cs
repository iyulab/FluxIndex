using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;

using SyncStatus = FluxIndex.Extensions.FileVault.Domain.Enums.SyncStatus;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Main vault service interface.
/// Provides simplified commands for file tracking and processing.
/// </summary>
public interface IVault
{
    /// <summary>
    /// Base path for the vault (.vault directory).
    /// </summary>
    string VaultBasePath { get; }

    // === Core Commands ===

    /// <summary>
    /// Memorizes a file through the full pipeline.
    /// Flow: extract → chunk → embed → commit
    /// For new files or when source has changed.
    /// </summary>
    Task<VaultEntry> MemorizeAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Refreshes a file's vault content without re-extraction.
    /// Flow: chunk → embed → commit (skip extraction)
    /// Use when vault/ files (append-text.md, qa.md) were manually edited.
    /// </summary>
    Task<VaultEntry> RefreshAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Syncs all watched folders and queues necessary memorize/refresh operations.
    /// Detects changes and queues appropriate jobs.
    /// </summary>
    Task<SyncResult> SyncAsync(CancellationToken ct = default);

    /// <summary>
    /// Detects what kind of changes exist for a file.
    /// Combines content-hash check (source changes) and git status (vault changes).
    /// </summary>
    Task<ChangeDetectionResult> DetectChangesAsync(string filePath, CancellationToken ct = default);

    // === Entry Management ===

    /// <summary>
    /// Gets a vault entry by source file path.
    /// Returns null if entry doesn't exist.
    /// </summary>
    Task<VaultEntry?> GetAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Gets a vault entry by filepath hash.
    /// </summary>
    Task<VaultEntry?> GetByHashAsync(string filepathHash, CancellationToken ct = default);

    /// <summary>
    /// Lists all vault entries, optionally filtered by stage.
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> ListAsync(ProcessingStage? stageFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Removes a vault entry and its associated data.
    /// Also removes chunks from vector store.
    /// </summary>
    Task RemoveAsync(string filePath, CancellationToken ct = default);

    // === Status & History ===

    /// <summary>
    /// Gets the overall vault status.
    /// </summary>
    Task<VaultStatus> StatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the diff for a vault entry's vault/ directory.
    /// </summary>
    Task<string> DiffAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Gets the commit history for a vault entry.
    /// </summary>
    Task<IReadOnlyList<GitCommit>> LogAsync(string filePath, int maxCount = 10, CancellationToken ct = default);

    // === Folder Watching ===

    /// <summary>
    /// Adds a folder to watch for changes.
    /// </summary>
    Task<WatchedFolder> AddWatchedFolderAsync(
        string folderPath,
        string? name = null,
        bool isRecursive = true,
        bool autoMemorize = false,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a watched folder by ID.
    /// </summary>
    Task<WatchedFolder?> GetWatchedFolderAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>
    /// Gets all watched folders.
    /// </summary>
    Task<IReadOnlyList<WatchedFolder>> GetAllWatchedFoldersAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes a watched folder.
    /// </summary>
    Task RemoveWatchedFolderAsync(Guid folderId, bool removeTrackedFiles = false, CancellationToken ct = default);

    /// <summary>
    /// Pauses watching a folder.
    /// </summary>
    Task PauseWatchingAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>
    /// Resumes watching a folder.
    /// </summary>
    Task ResumeWatchingAsync(Guid folderId, CancellationToken ct = default);

    /// <summary>
    /// Scans a folder and detects changes.
    /// </summary>
    Task<ScanResult> ScanFolderAsync(string folderPath, CancellationToken ct = default);

    /// <summary>
    /// Scans a watched folder by ID.
    /// </summary>
    Task<ScanResult> ScanFolderAsync(Guid folderId, CancellationToken ct = default);

    // === Queue Management ===

    /// <summary>
    /// Pauses the background queue processing.
    /// </summary>
    Task PauseQueueAsync(CancellationToken ct = default);

    /// <summary>
    /// Resumes the background queue processing.
    /// </summary>
    Task ResumeQueueAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the current queue status.
    /// </summary>
    Task<QueueStatus> GetQueueStatusAsync(CancellationToken ct = default);

    // === Maintenance ===

    /// <summary>
    /// Cleans up orphaned entries (source files that no longer exist).
    /// </summary>
    Task<int> CleanupOrphanedEntriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets orphaned entries.
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> GetOrphanedEntriesAsync(CancellationToken ct = default);

    // === Status-based Queries ===

    /// <summary>
    /// Lists entries filtered by sync status.
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> ListByStatusAsync(SyncStatus status, CancellationToken ct = default);

    /// <summary>
    /// Gets entries that are pending removal (SourceDeleted, RemovalPending, or RemovalPartial).
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> GetPendingRemovalsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets entries that are in an error state.
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> GetErrorEntriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets entries that need synchronization (SourceModified or VaultModified).
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> GetEntriesNeedingSyncAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of change detection for a file.
/// </summary>
public sealed class ChangeDetectionResult
{
    /// <summary>
    /// The file path that was checked.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Whether an entry exists for this file.
    /// </summary>
    public bool EntryExists { get; init; }

    /// <summary>
    /// Whether the source file content has changed (content-hash mismatch).
    /// </summary>
    public bool SourceChanged { get; init; }

    /// <summary>
    /// Whether vault files have been modified (git status shows changes).
    /// </summary>
    public bool VaultChanged { get; init; }

    /// <summary>
    /// Whether the source file exists on disk.
    /// </summary>
    public bool SourceExists { get; init; }

    /// <summary>
    /// The recommended action based on detected changes.
    /// </summary>
    public ChangeAction RecommendedAction { get; init; }

    /// <summary>
    /// List of modified vault files (if any).
    /// </summary>
    public IReadOnlyList<string> ModifiedVaultFiles { get; init; } = [];

    /// <summary>
    /// Whether any changes were detected.
    /// </summary>
    public bool HasChanges => SourceChanged || VaultChanged;

    // === File Metadata ===

    /// <summary>
    /// File name without path.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// File extension including dot (e.g., ".pdf").
    /// </summary>
    public string FileExtension { get; init; } = string.Empty;

    /// <summary>
    /// File size in bytes. Null if file doesn't exist.
    /// </summary>
    public long? FileSize { get; init; }

    /// <summary>
    /// File last modified time. Null if file doesn't exist.
    /// </summary>
    public DateTimeOffset? FileModifiedAt { get; init; }

    // === Vault Status ===

    /// <summary>
    /// Processing stage if entry exists.
    /// </summary>
    public ProcessingStage? Stage { get; init; }

    /// <summary>
    /// Sync status if entry exists.
    /// </summary>
    public SyncStatus? SyncStatus { get; init; }

    /// <summary>
    /// Number of chunks if memorized.
    /// </summary>
    public int? ChunkCount { get; init; }

    /// <summary>
    /// Last error message if any.
    /// </summary>
    public string? LastError { get; init; }
}

/// <summary>
/// Recommended action based on change detection.
/// </summary>
public enum ChangeAction
{
    /// <summary>
    /// No action needed - file is up to date.
    /// </summary>
    None = 0,

    /// <summary>
    /// Memorize - new file or source changed.
    /// </summary>
    Memorize = 1,

    /// <summary>
    /// Refresh - only vault files changed.
    /// </summary>
    Refresh = 2,

    /// <summary>
    /// Remove - source file no longer exists.
    /// </summary>
    Remove = 3
}

/// <summary>
/// Vault status summary.
/// </summary>
public sealed class VaultStatus
{
    // Entry counts by stage
    public int TotalEntries { get; init; }
    public int SourceCount { get; init; }
    public int ExtractedCount { get; init; }
    public int MemorizedCount { get; init; }

    // Change tracking
    public int ChangedSourceCount { get; init; }
    public int ChangedVaultCount { get; init; }
    public IReadOnlyList<VaultEntry> ChangedEntries { get; init; } = [];

    // SyncStatus counts
    public int InSyncCount { get; init; }
    public int SourceModifiedCount { get; init; }
    public int VaultModifiedCount { get; init; }
    public int SourceDeletedCount { get; init; }
    public int RemovalPendingCount { get; init; }
    public int RemovalPartialCount { get; init; }
    public int ErrorCount { get; init; }

    // Watcher status
    public int ActiveWatcherCount { get; init; }
    public int PausedWatcherCount { get; init; }
    public int ErrorWatcherCount { get; init; }

    // Queue status
    public int QueuedCount { get; init; }
    public int ProcessingCount { get; init; }
    public int FailedCount { get; init; }
    public int OrphanedCount { get; init; }

    // Timing
    public DateTimeOffset? LastSyncTime { get; init; }
    public DateTimeOffset StatusAsOf { get; init; } = DateTimeOffset.UtcNow;

    // Storage
    public long TotalStorageSizeBytes { get; init; }
}

/// <summary>
/// Queue status summary.
/// </summary>
public sealed class QueueStatus
{
    public int QueuedCount { get; init; }
    public int ProcessingCount { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public bool IsPaused { get; init; }
    public DateTimeOffset? LastProcessedAt { get; init; }
}

/// <summary>
/// Sync operation result.
/// </summary>
public sealed class SyncResult
{
    // Queued job counts
    public int MemorizeQueuedCount { get; init; }
    public int RefreshQueuedCount { get; init; }
    public int RemoveQueuedCount { get; init; }

    // Skip counts
    public int SkippedCount { get; init; }

    // Error tracking
    public int ErrorCount { get; init; }
    public IReadOnlyList<SyncError> Errors { get; init; } = [];

    // Folder scanning
    public int FoldersScanned { get; init; }
    public int NewFilesDiscovered { get; init; }
    public int ChangedFilesDetected { get; init; }

    // Orphan management
    public int OrphansDetected { get; init; }
    public int OrphansQueued { get; init; }

    // Timing
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool IsSuccess => ErrorCount == 0;
    public int TotalQueuedCount => MemorizeQueuedCount + RefreshQueuedCount + RemoveQueuedCount;
}

/// <summary>
/// Sync error details.
/// </summary>
public sealed class SyncError
{
    public string FilePath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>
/// Folder scan result.
/// </summary>
public sealed class ScanResult
{
    // File counts
    public int ScannedCount { get; init; }
    public int NewFilesCount { get; init; }
    public int ExistingFilesCount { get; init; }
    public int ChangedFilesCount { get; init; }
    public int SkippedFilesCount { get; init; }
    public int OrphanedFilesCount { get; init; }

    // Results
    public IReadOnlyList<ChangeDetectionResult> DetectedChanges { get; init; } = [];

    // Errors
    public int ErrorCount { get; init; }
    public IReadOnlyList<ScanError> Errors { get; init; } = [];

    // Timing
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Scan error details.
/// </summary>
public sealed class ScanError
{
    public string FilePath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>
/// Watch options for folder monitoring.
/// </summary>
public sealed class WatchOptions
{
    public bool IsRecursive { get; set; } = true;
    public List<string> IncludePatterns { get; set; } = ["*.pdf", "*.docx", "*.md", "*.txt", "*.html"];
    public List<string> ExcludePatterns { get; set; } = ["~$*", "*.tmp", ".*"];
    public bool AutoMemorize { get; set; } = false;
}
