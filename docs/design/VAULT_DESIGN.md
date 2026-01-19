# FileVault Design Document

## Overview

FluxIndex.Extensions.FileVault is a file-to-vector synchronization layer that maintains consistency between source files and the FluxIndex vector store. It provides automatic change detection, phased processing pipelines, and robust state management.

## Design Goals

1. **Transparent Sync**: Files are tracked and indexed without user intervention
2. **Change Detection**: Detect file modifications, deletions via content hashing
3. **Atomic Operations**: Phased removal with recovery from partial failures
4. **Artifact Organization**: Store extracted content, images, refined markdown
5. **Resilient Processing**: Background queue with retry and error recovery

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        FluxIndex.Extensions.FileVault                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐                   │
│  │ IVault       │───▶│ VaultPipeline│───▶│ VaultStorage │                   │
│  │ (VaultManager)    │ (Processing) │    │  (Artifacts) │                   │
│  └──────────────┘    └──────────────┘    └──────────────┘                   │
│         │                   │                   │                            │
│         ▼                   ▼                   ▼                            │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐                   │
│  │ QueueService │    │  IExtractor  │    │  IGitService │                   │
│  │ (Background) │    │  IChunker    │    │  (Versioning)│                   │
│  └──────────────┘    └──────────────┘    └──────────────┘                   │
│         │                   │                                                │
│         ▼                   ▼                                                │
│  ┌──────────────┐    ┌──────────────┐                                       │
│  │ FileWatcher  │    │ IVectorStore │                                       │
│  │ (Detection)  │    │ IEmbedding   │                                       │
│  └──────────────┘    └──────────────┘                                       │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Processing Stage State Machine

```
    ┌───────────────────────┐
    │       Source          │  (File registered, not yet processed)
    └───────────┬───────────┘
                │ extract()
                ▼
    ┌───────────────────────┐
    │      Extracted        │  (Content extracted to vault/)
    └───────────┬───────────┘
                │ chunk() + embed() + index()
                ▼
    ┌───────────────────────┐
    │      Memorized        │  (Indexed in vector store)
    └───────────────────────┘
```

## Sync Status State Machine

Tracks synchronization state between source file and vector store:

```
InSync ←────────────────────────────────────────┐
   │                                             │
   ├──[source changed]──→ SourceModified ──[memorize]─┤
   │                                             │
   ├──[vault changed]───→ VaultModified ──[refresh]───┤
   │                                             │
   └──[source deleted]──→ SourceDeleted
                              │
                              ├──[queue]──→ RemovalPending
                              │                   │
                              │       ┌───────────┴───────────┐
                              │       │                       │
                              │  [Phase1 done]            [failure]
                              │       │                       │
                              │       ▼                       ▼
                              │  RemovalPartial            Error
                              │  (Vector deleted)             │
                              │       │                       │
                              │  [Phase2 done]            [retry]
                              │       │                       │
                              │       ▼                       │
                              │  [Entry removed] ←────────────┘
                              │
                              └──[immediate]──→ [Entry removed]
```

### SyncStatus Enum

| Status | Description |
|--------|-------------|
| `InSync` | Source and vector store are synchronized |
| `SourceModified` | Source file changed, needs re-memorization |
| `VaultModified` | Vault files changed, needs refresh |
| `SourceDeleted` | Source file deleted, pending removal |
| `RemovalPending` | Removal queued, not started |
| `RemovalPartial` | Vector deleted, storage pending |
| `Error` | Processing error occurred |

## Data Model

### VaultEntry

Primary entity representing a tracked file.

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Unique identifier |
| FilepathHash | string | Hash of normalized path (directory name) |
| SourcePath | string | Full path to source file |
| SourceContentHash | ContentHash? | SHA256 hash for change detection |
| Stage | ProcessingStage | Source / Extracted / Memorized |
| SyncStatus | SyncStatus | Synchronization state |
| ChunkCount | int | Number of indexed chunks |
| CreatedAt | DateTimeOffset | When entry was created |
| LastProcessedAt | DateTimeOffset? | Last successful processing |
| LastSyncCheckAt | DateTimeOffset? | Last sync check time |
| LastError | string? | Last error message |
| RemovalPhase | string? | Current removal phase ("Vector") |

### WatchedFolder

Folder being monitored for changes.

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Unique identifier |
| Path | string | Full folder path |
| Name | string | Display name |
| IsRecursive | bool | Include subdirectories |
| AutoMemorize | bool | Auto-process new files |
| IncludePatterns | string[] | File patterns to include |
| ExcludePatterns | string[] | File patterns to exclude |
| Status | WatcherStatus | Active / Paused / Error |

## Storage Structure

```
.vault/                              # Vault root (configurable)
├── {filepath-hash}/                 # Entry directory
│   ├── meta.json                    # Entry metadata (git-ignored)
│   ├── images/                      # Extracted images (git-ignored)
│   │   ├── img_001.png
│   │   └── manifest.json
│   └── vault/                       # Git-tracked content
│       ├── .git/
│       ├── refined.md               # Extracted + refined content
│       ├── append-text.md           # User-appended notes
│       └── qa.md                    # Q&A pairs
│
└── {filepath-hash-2}/
    └── ...
```

### Directory Naming

- Entry directories use first 8 bytes of SHA256(normalized_path) as hex
- Example: `83d095c10b2f28b1` for `D:\Documents\manual.pdf`
- Same path always produces same hash (deterministic)

## Two-Phase Removal

Ensures atomic cleanup even if process crashes mid-removal:

```
Phase 1: Vector Store Deletion
    │
    ├── entry.MarkRemovalPending()
    ├── entry.SaveMetadata()
    │
    ├── pipeline.RemoveAsync(entry)  // Delete from vector store
    │
    ├── entry.MarkRemovalPartial("Vector")
    └── entry.SaveMetadata()

Phase 2: Storage Deletion
    │
    ├── storage.DeleteEntryStorageAsync(entry)
    └── [Entry completely removed]
```

### Recovery on Startup

```csharp
// VaultBackgroundService.StartAsync()
var partialRemovals = await ListByStatusAsync(SyncStatus.RemovalPartial);
foreach (var entry in partialRemovals)
{
    if (entry.RemovalPhase == "Vector")
    {
        // Vector already deleted, just clean storage
        await storage.DeleteEntryStorageAsync(entry);
    }
}
```

## Pipeline Operations

### Memorize (Full Pipeline)

```
Source File → Extract → Store refined.md → Chunk → Embed → Index
                ↓
          SourceContentHash updated
                ↓
          Stage = Memorized
          SyncStatus = InSync
```

### Refresh (Re-index Only)

```
vault/refined.md → Chunk → Embed → Index
        ↓
   Stage = Memorized
   SyncStatus = InSync
```

Used when vault files are manually edited.

### DetectChanges

```csharp
// Compare current file hash with stored SourceContentHash
if (currentHash != entry.SourceContentHash)
    return ChangeAction.Memorize;

// Check git status for vault/ changes
if (gitStatus.HasModifications)
    return ChangeAction.Refresh;

// Check if source file exists
if (!File.Exists(sourcePath))
    return ChangeAction.Remove;

return ChangeAction.None;
```

## Configuration

```csharp
services.AddFileVault(options =>
{
    // Storage
    options.VaultBasePath = @"D:\Data\.vault";
    options.VaultDirectoryName = ".vault";  // Default marker

    // File handling
    options.MaxFileSizeMB = 100;
    options.DefaultIncludePatterns = ["*.pdf", "*.docx", "*.md"];
    options.DefaultExcludePatterns = ["~$*", "*.tmp", ".*"];

    // Real-time watching
    options.EnableRealTimeWatch = true;
    options.DebounceDelayMs = 500;
    options.WatcherBufferSize = 65536;

    // Background processing
    options.EnableBackgroundProcessing = true;
    options.MaxConcurrentProcessing = 4;
    options.QueuePollingIntervalMs = 1000;

    // Retry behavior
    options.EnableAutoRetry = true;
    options.MaxRetryCount = 3;
    options.RetryDelayMs = 5000;

    // Chunking
    options.Chunking.MaxChunkSize = 1024;
    options.Chunking.OverlapSize = 128;
    options.Chunking.Strategy = "Intelligent";
});
```

## Service Registration

```csharp
// Basic registration
services.AddFileVault();

// With background queue processing
services.AddFileVaultWithBackgroundProcessing();

// With FileFlux integration (extraction + chunking)
services.AddFileVaultWithFileFlux();

// With full FluxIndex integration (extraction + chunking + indexing)
services.AddFileVaultWithFluxIndex();
```

## Key Interfaces

### IVault

Main facade for vault operations.

```csharp
public interface IVault
{
    // Core operations
    Task<VaultEntry> MemorizeAsync(string filePath, CancellationToken ct);
    Task<VaultEntry> RefreshAsync(string filePath, CancellationToken ct);
    Task<SyncResult> SyncAsync(CancellationToken ct);
    Task<ChangeDetectionResult> DetectChangesAsync(string filePath, CancellationToken ct);

    // Entry management
    Task<VaultEntry?> GetAsync(string filePath, CancellationToken ct);
    Task<IReadOnlyList<VaultEntry>> ListAsync(ProcessingStage? filter, CancellationToken ct);
    Task RemoveAsync(string filePath, CancellationToken ct);

    // Status queries
    Task<IReadOnlyList<VaultEntry>> ListByStatusAsync(SyncStatus status, CancellationToken ct);
    Task<IReadOnlyList<VaultEntry>> GetPendingRemovalsAsync(CancellationToken ct);
    Task<IReadOnlyList<VaultEntry>> GetErrorEntriesAsync(CancellationToken ct);

    // Folder watching
    Task<WatchedFolder> AddWatchedFolderAsync(string path, ...);
    Task<ScanResult> ScanFolderAsync(string path, CancellationToken ct);

    // Queue management
    Task PauseQueueAsync(CancellationToken ct);
    Task ResumeQueueAsync(CancellationToken ct);
}
```

### IVaultPipeline

Processing pipeline for memorization and refresh.

```csharp
public interface IVaultPipeline
{
    Task<MemorizeResult> MemorizeAsync(VaultEntry entry, MemorizeOptions? options, CancellationToken ct);
    Task<RefreshResult> RefreshAsync(VaultEntry entry, RefreshOptions? options, CancellationToken ct);
    Task RemoveAsync(VaultEntry entry, CancellationToken ct);
}
```

## Error Handling

| Scenario | Handling |
|----------|----------|
| File locked | Retry with exponential backoff |
| Extraction fails | Mark entry as Error, log details |
| Vector store error | Retry, then mark as Error |
| Partial removal | Recover on next startup |
| Disk full | Throw, prevent new memorizations |

## Performance Considerations

1. **Content Hashing**: Stream-based SHA256 for large files
2. **Parallel Processing**: Configurable concurrent operations
3. **Debouncing**: Merge rapid file changes into single event
4. **Lazy Loading**: Load content only when needed
5. **Background Queue**: Non-blocking file processing

## Security Considerations

1. **Path Validation**: Prevent path traversal attacks
2. **Sensitive Files**: Exclude .env, credentials by default
3. **Permission Check**: Verify read access before tracking
4. **Symlink Handling**: Don't follow symlinks outside vault

## Implementation Status

| Component | Status |
|-----------|--------|
| VaultEntry | ✅ Complete |
| VaultManager | ✅ Complete |
| VaultPipeline | ✅ Complete |
| VaultStorageService | ✅ Complete |
| VaultQueueService | ✅ Complete |
| VaultBackgroundService | ✅ Complete |
| FileWatcherService | ✅ Complete |
| SyncStatus Management | ✅ Complete |
| Two-Phase Removal | ✅ Complete |
| Partial Removal Recovery | ✅ Complete |

## Related Documentation

- [FileVault Consumer Guide](../FILEVAULT_GUIDE.md) - Integration guide for consumer apps
- [AI Provider Integration](../AI_PROVIDER_INTEGRATION.md) - Embedding service setup
