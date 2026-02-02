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

/// <summary>
/// Request for vault search with path-based scope filtering.
/// </summary>
public class VaultSearchRequestDto
{
    /// <summary>
    /// Search query text.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Path scope for search. Can be:
    /// - Empty/null: Search all indexed files
    /// - ["folder/"]: Search all files in folder (recursive)
    /// - ["folder/file.pdf"]: Search only in specific file
    /// - ["folder/a.pdf", "folder/sub/"]: Search in multiple paths
    /// </summary>
    public List<string>? PathScope { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int TopK { get; init; } = 10;

    /// <summary>
    /// Minimum score threshold for results.
    /// </summary>
    public float MinScore { get; init; } = 0.0f;

    /// <summary>
    /// Whether to include chunk content in results.
    /// </summary>
    public bool IncludeContent { get; init; } = true;

    /// <summary>
    /// Whether to include metadata in results.
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;
}

/// <summary>
/// Individual search result item.
/// </summary>
public class VaultSearchResultItemDto
{
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
/// Response from vault search.
/// </summary>
public class VaultSearchResponseDto
{
    /// <summary>
    /// The search query.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Search results ordered by relevance.
    /// </summary>
    public List<VaultSearchResultItemDto> Items { get; init; } = new();

    /// <summary>
    /// Total number of results found.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Paths that were searched.
    /// </summary>
    public List<string> SearchedPaths { get; init; } = new();

    /// <summary>
    /// Number of documents that were searched.
    /// </summary>
    public int DocumentsSearched { get; init; }

    /// <summary>
    /// Search duration in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// Whether the search was successful.
    /// </summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>
    /// Error message if search failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
