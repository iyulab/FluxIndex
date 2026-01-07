namespace FluxIndex.Stack.Shared.DTOs.Vault;

/// <summary>
/// DTO for a watched folder.
/// </summary>
public class WatchedFolderDto
{
    public Guid Id { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsRecursive { get; init; }
    public string[] IncludePatterns { get; init; } = Array.Empty<string>();
    public string[] ExcludePatterns { get; init; } = Array.Empty<string>();
    public bool AutoMemorize { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastScannedAt { get; init; }
    public Guid? CollectionId { get; init; }
    public int TrackedFileCount { get; init; }

    /// <summary>
    /// Indicates whether the folder path currently exists on the filesystem.
    /// Updated in real-time when fetching folder list.
    /// </summary>
    public bool PathExists { get; init; }
}

/// <summary>
/// DTO for a tracked file.
/// </summary>
public class TrackedFileDto
{
    public Guid Id { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string? ContentHash { get; init; }
    public DateTime? FileModifiedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? MemorizedAt { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? WatchedFolderId { get; init; }
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// The actual indexing status from the associated Document.
    /// This reflects whether the file has been fully indexed with embeddings.
    /// </summary>
    public string? DocumentStatus { get; init; }

    /// <summary>
    /// Computed effective status that combines TrackedFile and Document status.
    /// This is what should be displayed to users.
    /// </summary>
    public string EffectiveStatus { get; init; } = string.Empty;
}

/// <summary>
/// Request to add a watched folder.
/// </summary>
public class AddWatchedFolderRequest
{
    public string Path { get; init; } = string.Empty;
    public string? Name { get; init; }
    public bool IsRecursive { get; init; } = true;
    public bool AutoMemorize { get; init; } = true;
    public string[]? IncludePatterns { get; init; }
    public string[]? ExcludePatterns { get; init; }
    public Guid? CollectionId { get; init; }
}

/// <summary>
/// Request to update a watched folder's path.
/// Used when folder is moved or renamed on the filesystem.
/// </summary>
public class UpdateFolderPathRequest
{
    public string NewPath { get; init; } = string.Empty;
}

/// <summary>
/// Request to memorize a file.
/// </summary>
public class MemorizeFileRequest
{
    public string SourcePath { get; init; } = string.Empty;
    public Guid? WatchedFolderId { get; init; }
}

/// <summary>
/// Result of a scan operation.
/// </summary>
public class ScanResultDto
{
    public Guid FolderId { get; init; }
    public int TotalFilesFound { get; init; }
    public int NewFilesQueued { get; init; }
    public int ChangedFilesQueued { get; init; }
    public int OrphanedFilesDetected { get; init; }
    public int SkippedFiles { get; init; }
    public List<string> Errors { get; init; } = new();
    public double DurationSeconds { get; init; }
}

/// <summary>
/// Result of a sync operation.
/// </summary>
public class SyncResultDto
{
    public int FoldersScanned { get; init; }
    public int FilesProcessed { get; init; }
    public int FilesQueued { get; init; }
    public int OrphanedFilesCleaned { get; init; }
    public List<string> Errors { get; init; } = new();
    public double DurationSeconds { get; init; }
}

/// <summary>
/// Current vault status.
/// </summary>
public class VaultStatusDto
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

/// <summary>
/// Tracked file version DTO.
/// </summary>
public class TrackedFileVersionDto
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool HasExtract { get; init; }
    public bool HasChunks { get; init; }
    public bool HasImages { get; init; }
    public bool HasQA { get; init; }
    public bool HasEnrichment { get; init; }
}
