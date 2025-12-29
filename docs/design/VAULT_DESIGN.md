# FluxIndex.Vault Design Document

## Overview

FluxIndex.Vault is a file system synchronization layer that maintains bidirectional sync between source files and the FluxIndex indexing system. It provides automatic change detection, versioning, and artifact management.

## Design Goals

1. **Transparent Sync**: Files are tracked and indexed without user intervention
2. **Change Detection**: Detect file modifications, deletions, and renames
3. **Version History**: Maintain history of changes for rollback capability
4. **Artifact Organization**: Store extracted content, images, chunks, Q&A in organized structure
5. **Resilient Watching**: Handle edge cases (locked files, rapid changes, network drives)

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          FluxIndex.Vault                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐              │
│  │ FileWatcher  │───▶│  SyncEngine  │───▶│ VaultStorage │              │
│  │ (Detection)  │    │ (Orchestrate)│    │  (Artifacts) │              │
│  └──────────────┘    └──────────────┘    └──────────────┘              │
│         │                   │                   │                       │
│         ▼                   ▼                   ▼                       │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐              │
│  │ ChangeDetect │    │  Pipeline    │    │   Artifact   │              │
│  │ (Hash/Debounce)   │  Executor    │    │   Manager    │              │
│  └──────────────┘    └──────────────┘    └──────────────┘              │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## File State Machine

```
    ┌───────────────────────┐
    │     Untracked         │  (New file discovered)
    └───────────┬───────────┘
                │ memorize()
                ▼
    ┌───────────────────────┐
    │       Queued          │  (Waiting in queue)
    └───────────┬───────────┘
                │ process()
                ▼
    ┌───────────────────────┐
    │     Processing        │  (Extracting/Chunking/Embedding)
    └───────────┬───────────┘
                │ complete()
                ▼
    ┌───────────────────────┐  file changed     ┌───────────────────┐
    │      Memorized        │──────────────────▶│      Stale        │
    │  (Indexed, in sync)   │                   │  (Change detected)│
    └───────────┬───────────┘◀──────────────────└───────────────────┘
                │                   reprocess()
                │ file deleted
                ▼
    ┌───────────────────────┐
    │       Orphaned        │  (Source file deleted)
    └───────────┬───────────┘
                │ unmemorize() / auto-cleanup
                ▼
    ┌───────────────────────┐
    │       Removed         │  (Cleaned from vault)
    └───────────────────────┘
```

## Data Model

### TrackedFile

Primary entity representing a file being tracked by the vault.

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Primary key |
| SourcePath | string | Full path to source file |
| FileName | string | File name with extension |
| FileExtension | string | Extension (.docx, .pdf) |
| FileSize | long | Size in bytes |
| ContentHash | string | SHA256 hash of content |
| FileModifiedAt | DateTime | Source file last modified |
| Status | TrackedFileStatus | Current state |
| Version | int | Current version number |
| MemorizedAt | DateTime? | When first memorized |
| LastSyncedAt | DateTime? | Last successful sync |
| WatchedFolderId | Guid? | Parent folder |
| DocumentId | Guid? | Linked Document entity |

### WatchedFolder

Represents a folder being monitored for changes.

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Primary key |
| Path | string | Full folder path |
| Name | string | Folder display name |
| IsRecursive | bool | Include subdirectories |
| IncludePatterns | string[] | File patterns to include |
| ExcludePatterns | string[] | File patterns to exclude |
| AutoMemorize | bool | Auto-memorize new files |
| Status | WatcherStatus | Active/Paused/Error |
| CollectionId | Guid? | Target collection |

### TrackedFileVersion

Version history for tracked files.

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Primary key |
| TrackedFileId | Guid | Parent tracked file |
| Version | int | Version number |
| ContentHash | string | Hash at this version |
| FileSize | long | Size at this version |
| CreatedAt | DateTime | When version created |
| HasExtract | bool | Extract artifact exists |
| HasChunks | bool | Chunks artifact exists |
| HasImages | bool | Images extracted |
| HasQA | bool | Q&A pairs generated |

## Vault Storage Structure

```
<vault-path>/
├── .vault/
│   ├── config.json           # Vault configuration
│   └── state.json            # Runtime state
│
├── <file-id>/
│   ├── .meta.json            # File metadata
│   │   {
│   │     "id": "guid",
│   │     "sourcePath": "d:/data/folder-a/file-1.docx",
│   │     "contentHash": "sha256:abc123...",
│   │     "version": 2,
│   │     "memorizedAt": "2024-01-15T10:30:00Z",
│   │     "artifacts": ["extract", "images", "chunks"]
│   │   }
│   │
│   ├── extract/
│   │   ├── content.md        # Markdown converted
│   │   └── content.txt       # Plain text
│   │
│   ├── images/
│   │   ├── img_001.png
│   │   ├── img_002.jpg
│   │   └── manifest.json     # Image metadata
│   │
│   ├── chunks/
│   │   └── chunks.json       # Chunk data with embeddings
│   │
│   ├── refine/
│   │   └── enrichment.json   # FluxImprover results
│   │
│   ├── qa/
│   │   └── pairs.json        # Generated Q&A pairs
│   │
│   └── versions/
│       ├── v1/
│       │   └── manifest.json # Version 1 state
│       └── v2/
│           └── manifest.json # Version 2 state
│
└── <file-id-2>/
    └── ...
```

## FileSystemWatcher Strategy

Based on [best practices research](https://failingfast.io/a-robust-solution-for-filesystemwatcher-firing-events-multiple-times/):

### Debouncing Strategy

```csharp
// Timer-based debouncing with MemoryCache
public class DebounceService
{
    private readonly MemoryCache _cache = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);

    public async Task DebounceAsync(string key, Func<Task> action)
    {
        // Cancel previous timer if exists
        if (_cache.TryGetValue(key, out CancellationTokenSource? existing))
        {
            existing?.Cancel();
        }

        var cts = new CancellationTokenSource();
        _cache.Set(key, cts, _debounceInterval);

        try
        {
            await Task.Delay(_debounceInterval, cts.Token);
            await action();
        }
        catch (OperationCanceledException)
        {
            // Debounced - newer event will handle
        }
    }
}
```

### Buffer Overflow Prevention

- Set `InternalBufferSize` appropriately (default 8KB, can increase to 64KB)
- Use specific `NotifyFilter` values
- Process events asynchronously
- Implement periodic full scan as fallback

### Locked File Handling

```csharp
public async Task<bool> WaitForFileAccessAsync(string path, int maxRetries = 5)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            await Task.Delay(100 * (i + 1)); // Exponential backoff
        }
    }
    return false;
}
```

## Integration with Existing Stack

### Document Entity Linkage

TrackedFile links to existing Document entity via `DocumentId`:

```
TrackedFile (Vault) ──────────▶ Document (Stack)
     │                              │
     │ SourcePath                   │ Title
     │ ContentHash                  │ Status
     │ Version                      │ ChunkCount
     └──────────────────────────────┘
```

### Indexing Pipeline Integration

Vault uses existing Stack services:
- `IChunkingService` for content chunking
- `IEmbeddingProvider` for embeddings
- `IDocumentContentProvider` for content storage
- `IIndexingService` for document indexing

### Event Flow

```
FileWatcher detects change
        │
        ▼
DebounceService filters duplicates
        │
        ▼
SyncEngine updates TrackedFile status
        │
        ▼
MemorizationPipeline (reuses Stack services)
        │
        ▼
VaultStorage stores artifacts
        │
        ▼
Document entity updated via IDocumentService
```

## Configuration

```json
{
  "FluxIndex": {
    "Vault": {
      "Enabled": true,
      "StoragePath": "./vault",
      "HashAlgorithm": "SHA256",
      "EnableRealTimeWatch": true,
      "ScanIntervalMinutes": 60,
      "DebounceDelayMs": 500,
      "WatcherBufferSize": 65536,
      "MaxFileSizeMB": 100,
      "VersionRetentionCount": 5,
      "AutoCleanupOrphans": false,
      "DefaultPatterns": {
        "Include": ["*.docx", "*.pdf", "*.txt", "*.md", "*.html", "*.htm"],
        "Exclude": ["~$*", "*.tmp", "*.bak", "Thumbs.db", ".DS_Store"]
      }
    }
  }
}
```

## API Design

### Folder Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /api/v1/vault/folders | List watched folders |
| POST | /api/v1/vault/folders | Add watched folder |
| DELETE | /api/v1/vault/folders/{id} | Remove watched folder |
| POST | /api/v1/vault/folders/{id}/scan | Trigger full scan |
| POST | /api/v1/vault/folders/{id}/pause | Pause watching |
| POST | /api/v1/vault/folders/{id}/resume | Resume watching |

### File Operations

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /api/v1/vault/files | List tracked files (filterable) |
| GET | /api/v1/vault/files/{id} | Get tracked file details |
| POST | /api/v1/vault/files/{id}/memorize | Memorize file |
| POST | /api/v1/vault/files/{id}/unmemorize | Unmemorize file |
| POST | /api/v1/vault/files/{id}/reprocess | Reprocess file |
| GET | /api/v1/vault/files/{id}/versions | Get version history |

### Bulk Operations

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | /api/v1/vault/memorize-all | Memorize all untracked |
| POST | /api/v1/vault/sync | Full sync operation |
| POST | /api/v1/vault/cleanup | Clean orphaned files |

### Artifacts

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /api/v1/vault/files/{id}/extract | Get extracted content |
| GET | /api/v1/vault/files/{id}/images | Get extracted images |
| GET | /api/v1/vault/files/{id}/chunks | Get chunk data |
| GET | /api/v1/vault/files/{id}/qa | Get Q&A pairs |

## Implementation Phases

### Phase 1: Core Entities and Repositories
- TrackedFile, WatchedFolder, TrackedFileVersion entities
- Repository interfaces
- EF Core DbContext configuration
- Database migrations

### Phase 2: Vault Storage Service
- VaultStorageService implementation
- Artifact storage/retrieval
- Hash computation service
- File metadata management

### Phase 3: File Watcher Service
- FileSystemWatcher wrapper
- Debouncing implementation
- Event handling
- Locked file handling
- Periodic scan fallback

### Phase 4: Sync Engine and Pipeline
- SyncEngine orchestration
- MemorizationPipeline
- Integration with existing Stack services
- Status state machine

### Phase 5: API and Background Service
- VaultController endpoints
- VaultBackgroundService for watching
- DI registration
- Configuration binding

### Phase 6: UI Integration (Future)
- Vault dashboard page
- Folder management UI
- File status display
- Sync status indicators

## Error Handling

| Scenario | Handling |
|----------|----------|
| File locked | Retry with exponential backoff |
| Network drive disconnected | Mark folder as Error, retry periodically |
| Hash computation fails | Log error, mark file as Error status |
| Buffer overflow | Log warning, trigger full scan |
| Disk full | Throw, prevent new memorizations |
| Permission denied | Mark file as Error with reason |

## Performance Considerations

1. **Lazy Loading**: Don't load file content until needed
2. **Parallel Processing**: Process multiple files concurrently
3. **Incremental Hashing**: For large files, use streaming hash
4. **Index Optimization**: Create indexes on SourcePath, Status, WatchedFolderId
5. **Artifact Compression**: Optionally compress stored artifacts

## Security Considerations

1. **Path Validation**: Prevent path traversal attacks
2. **Symlink Handling**: Don't follow symlinks outside vault
3. **Permission Check**: Verify read access before tracking
4. **Sensitive File Detection**: Warn on .env, credentials files

## References

- [FileSystemWatcher Best Practices](https://failingfast.io/a-robust-solution-for-filesystemwatcher-firing-events-multiple-times/)
- [Microsoft FileSystemWatcher Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher)
- [Debouncing in .NET](http://writeasync.net/?p=5744)
