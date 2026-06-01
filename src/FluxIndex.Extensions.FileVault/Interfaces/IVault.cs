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

    /// <summary>
    /// Removes multiple vault entries and their associated data.
    /// Processes each path sequentially; skips paths with no matching entry.
    /// </summary>
    Task RemoveAsync(IEnumerable<string> filePaths, CancellationToken ct = default);

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

    // === Search ===

    /// <summary>
    /// Searches indexed content with path-based scope filtering.
    /// </summary>
    /// <param name="query">Search query text.</param>
    /// <param name="options">Search options including path scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Search results.</returns>
    Task<VaultSearchResult> SearchAsync(string query, VaultSearchOptions? options = null, CancellationToken ct = default);
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
    public int RefinedCount { get; init; }
    public int MemorizedCount { get; init; }
    public int StaleCount { get; init; }
    public int ErrorStageCount { get; init; }

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
    public bool AutoMemorize { get; set; }
}

/// <summary>
/// Search strategy for vault content search. The facade owns this enum (rather than reusing one of
/// FluxIndex.Core's overlapping <c>SearchStrategy</c> types) so the public surface stays self-contained.
/// </summary>
public enum VaultSearchStrategy
{
    /// <summary>
    /// Dense vector (semantic) search only. Default — preserves pre-strategy behavior.
    /// </summary>
    Vector = 0,

    /// <summary>
    /// Hybrid vector + keyword (BM25) fused search via <c>IHybridSearchService</c>. Requires the
    /// consumer to register <c>IHybridSearchService</c> in the same container; when it is absent the
    /// request executes as <see cref="Vector"/> and the result reports that via
    /// <see cref="VaultSearchResult.ExecutedStrategy"/> (no silent mismatch).
    /// </summary>
    Hybrid = 1,

    // Keyword-only (pure BM25) is intentionally not exposed yet: HybridSearchOptions has no dedicated
    // keyword path (only VectorWeight/SparseWeight), so a "Keyword" value would secretly run degenerate
    // weighted hybrid — the same silent-mismatch class this carrier fixes. Tracked in ISSUE-161.
}

/// <summary>
/// Search options for vault content search.
/// </summary>
public sealed class VaultSearchOptions
{
    /// <summary>
    /// Path scope for search. Can be:
    /// - Empty/null: Search all indexed files
    /// - "folder/": Search all files in folder (recursive)
    /// - "folder/file.pdf": Search only in specific file
    /// - Multiple paths: Search in all specified paths
    /// </summary>
    public IReadOnlyList<string> PathScope { get; init; } = [];

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Minimum score threshold for results.
    /// </summary>
    public float MinScore { get; init; }

    /// <summary>
    /// Whether to include chunk content in results.
    /// </summary>
    public bool IncludeContent { get; init; } = true;

    /// <summary>
    /// Whether to include metadata in results.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;

    /// <summary>
    /// Search strategy to use. Defaults to <see cref="VaultSearchStrategy.Vector"/>. A
    /// <see cref="VaultSearchStrategy.Hybrid"/> request is honored only when the consumer registered
    /// <c>IHybridSearchService</c>; otherwise it degrades to vector and the degradation is reported on
    /// <see cref="VaultSearchResult.ExecutedStrategy"/>.
    /// </summary>
    public VaultSearchStrategy SearchStrategy { get; init; } = VaultSearchStrategy.Vector;

    /// <summary>
    /// Creates options for searching all files.
    /// </summary>
    public static VaultSearchOptions All(int topK = 10) => new() { TopK = topK };

    /// <summary>
    /// Creates options for searching within specific paths.
    /// </summary>
    public static VaultSearchOptions ForPaths(params string[] paths) => new() { PathScope = paths };

    /// <summary>
    /// Creates options for searching within a single folder.
    /// </summary>
    public static VaultSearchOptions ForFolder(string folderPath) => new() { PathScope = [folderPath.TrimEnd('/', '\\')] };

    /// <summary>
    /// Creates options for searching a single file.
    /// </summary>
    public static VaultSearchOptions ForFile(string filePath) => new() { PathScope = [filePath] };
}

/// <summary>
/// Single search result item.
/// </summary>
public sealed class VaultSearchResultItem
{
    /// <summary>
    /// The vault entry this result belongs to.
    /// </summary>
    public VaultEntry Entry { get; init; } = null!;

    /// <summary>
    /// Source file path.
    /// </summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// File name.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Chunk index within the document.
    /// </summary>
    public int ChunkIndex { get; init; }

    /// <summary>
    /// Chunk content (if included).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Similarity score (0.0 to 1.0).
    /// </summary>
    public float Score { get; init; }

    /// <summary>
    /// Chunk metadata (if included).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Search result from vault search.
/// </summary>
public sealed class VaultSearchResult
{
    /// <summary>
    /// The search query.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Search results ordered by relevance.
    /// </summary>
    public IReadOnlyList<VaultSearchResultItem> Items { get; init; } = [];

    /// <summary>
    /// Total number of results found (before TopK limit).
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Paths that were searched.
    /// </summary>
    public IReadOnlyList<string> SearchedPaths { get; init; } = [];

    /// <summary>
    /// Number of documents that were searched.
    /// </summary>
    public int DocumentsSearched { get; init; }

    /// <summary>
    /// Search duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether the search was successful.
    /// </summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>
    /// Error message if search failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The strategy the caller requested (echo of <see cref="VaultSearchOptions.SearchStrategy"/>).
    /// </summary>
    public VaultSearchStrategy RequestedStrategy { get; init; } = VaultSearchStrategy.Vector;

    /// <summary>
    /// The strategy actually executed. May differ from <see cref="RequestedStrategy"/> when a
    /// <see cref="VaultSearchStrategy.Hybrid"/> request degrades to vector because no
    /// <c>IHybridSearchService</c> is registered. Consumers should report this value (not the request)
    /// as the effective strategy.
    /// </summary>
    public VaultSearchStrategy ExecutedStrategy { get; init; } = VaultSearchStrategy.Vector;

    /// <summary>
    /// Creates an empty result.
    /// </summary>
    public static VaultSearchResult Empty(string query) => new()
    {
        Query = query,
        Items = [],
        TotalCount = 0
    };

    /// <summary>
    /// Creates an error result.
    /// </summary>
    public static VaultSearchResult Error(string query, string error) => new()
    {
        Query = query,
        Items = [],
        TotalCount = 0,
        IsSuccess = false,
        ErrorMessage = error
    };
}
