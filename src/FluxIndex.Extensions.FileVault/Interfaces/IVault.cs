using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Domain.Enums;

namespace FluxIndex.Extensions.FileVault.Interfaces;

/// <summary>
/// Main vault service interface.
/// Provides Git-like commands for file tracking and processing.
/// </summary>
public interface IVault
{
    /// <summary>
    /// Base path for the vault (.fluxindex directory).
    /// </summary>
    string VaultBasePath { get; }

    // === Entry Management ===

    /// <summary>
    /// Registers a source file and creates a vault entry.
    /// Creates .fluxindex/{hash}/ directory with Git repo.
    /// </summary>
    Task<VaultEntry> AddAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Gets a vault entry by source file path.
    /// </summary>
    Task<VaultEntry?> GetAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Gets a vault entry by content hash.
    /// </summary>
    Task<VaultEntry?> GetByHashAsync(string hash, CancellationToken ct = default);

    /// <summary>
    /// Lists all vault entries.
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> ListAsync(ProcessingStage? stageFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Removes a vault entry and its data.
    /// </summary>
    Task RemoveAsync(string filePath, CancellationToken ct = default);

    // === Pipeline Commands ===

    /// <summary>
    /// Extracts content from source file.
    /// Stage: Source → Extracted
    /// </summary>
    Task ExtractAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Refines extracted content.
    /// Stage: Extracted → Refined
    /// </summary>
    Task RefineAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Chunks refined content.
    /// Stage: Refined → Chunked
    /// </summary>
    Task ChunkAsync(string filePath, ChunkingOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Memorizes chunks to FluxIndex (like git commit + push).
    /// Stage: Chunked → Memorized
    /// </summary>
    Task MemorizeAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Processes file through all stages up to Memorized.
    /// Shorthand for: Add → Extract → Refine → Chunk → Memorize
    /// </summary>
    Task<VaultEntry> ProcessAsync(string filePath, ChunkingOptions? options = null, CancellationToken ct = default);

    // === Status & Diff ===

    /// <summary>
    /// Gets the status of all vault entries (like git status).
    /// </summary>
    Task<VaultStatus> StatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the diff for a vault entry.
    /// </summary>
    Task<string> DiffAsync(string filePath, string? stage = null, CancellationToken ct = default);

    /// <summary>
    /// Gets the commit history for a vault entry.
    /// </summary>
    Task<IReadOnlyList<GitCommit>> LogAsync(string filePath, int maxCount = 10, CancellationToken ct = default);

    // === Change Detection ===

    /// <summary>
    /// Checks if source file has changed since last processing.
    /// </summary>
    Task<bool> HasSourceChangedAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Checks if refined.md was manually edited.
    /// </summary>
    Task<bool> HasRefinedChangedAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Syncs entries with changed sources (reprocesses from stage 1).
    /// </summary>
    Task<SyncResult> SyncAsync(CancellationToken ct = default);

    // === Folder Watching ===

    /// <summary>
    /// Adds a watched folder.
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
    /// Watches a folder for file changes (legacy method).
    /// </summary>
    Task WatchFolderAsync(string folderPath, WatchOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Stops watching a folder (legacy method).
    /// </summary>
    Task UnwatchFolderAsync(string folderPath, CancellationToken ct = default);

    /// <summary>
    /// Scans a folder and adds new files.
    /// </summary>
    Task<ScanResult> ScanFolderAsync(string folderPath, CancellationToken ct = default);

    /// <summary>
    /// Scans a watched folder by ID.
    /// </summary>
    Task<ScanResult> ScanFolderAsync(Guid folderId, CancellationToken ct = default);

    // === Orphan Management ===

    /// <summary>
    /// Cleans up orphaned entries (source files that no longer exist).
    /// </summary>
    Task<int> CleanupOrphanedEntriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets orphaned entries.
    /// </summary>
    Task<IReadOnlyList<VaultEntry>> GetOrphanedEntriesAsync(CancellationToken ct = default);
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
    public int RefinedCount { get; init; }
    public int ChunkedCount { get; init; }
    public int MemorizedCount { get; init; }

    // Change tracking
    public int ChangedSourceCount { get; init; }
    public int ChangedRefinedCount { get; init; }
    public IReadOnlyList<VaultEntry> ChangedEntries { get; init; } = [];

    // Watcher status
    public int ActiveWatcherCount { get; init; }
    public int PausedWatcherCount { get; init; }
    public int ErrorWatcherCount { get; init; }

    // Queue status
    public int QueuedCount { get; init; }
    public int ProcessingCount { get; init; }
    public int ErrorCount { get; init; }
    public int OrphanedCount { get; init; }

    // Timing
    public DateTimeOffset? LastSyncTime { get; init; }
    public DateTimeOffset StatusAsOf { get; init; } = DateTimeOffset.UtcNow;

    // Storage
    public long TotalStorageSizeBytes { get; init; }
}

/// <summary>
/// Sync operation result.
/// </summary>
public sealed class SyncResult
{
    // Processing counts
    public int ProcessedCount { get; init; }
    public int SkippedCount { get; init; }
    public int QueuedCount { get; init; }

    // Error tracking
    public int ErrorCount { get; init; }
    public IReadOnlyList<SyncError> Errors { get; init; } = [];

    // Folder scanning
    public int FoldersScanned { get; init; }
    public int NewFilesDiscovered { get; init; }
    public int ChangedFilesDetected { get; init; }

    // Orphan management
    public int OrphansDetected { get; init; }
    public int OrphansCleaned { get; init; }

    // Timing
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool IsSuccess => ErrorCount == 0;
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
    public IReadOnlyList<VaultEntry> NewEntries { get; init; } = [];
    public IReadOnlyList<VaultEntry> ChangedEntries { get; init; } = [];
    public IReadOnlyList<string> OrphanedPaths { get; init; } = [];

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
    public bool AutoProcess { get; set; } = false;
}
