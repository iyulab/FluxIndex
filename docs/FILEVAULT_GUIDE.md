# FileVault Consumer Application Guide

FluxIndex.Extensions.FileVault provides file-to-vector synchronization for RAG applications. This guide covers integration patterns for consumer applications.

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Core Operations](#core-operations)
- [Folder Watching](#folder-watching)
- [Sync Status Management](#sync-status-management)
- [Background Processing](#background-processing)
- [Advanced Integration](#advanced-integration)
  - [With FileFlux](#with-fileflux-document-processing)
  - [With FluxIndex](#with-fluxindex-full-rag-stack)
  - [Vector Store Integration Flow](#vector-store-integration-flow)
  - [Multi-Tenant Usage](#multi-tenant-scope-isolated-usage)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

---

## Overview

FileVault bridges the gap between your file system and vector store by:

- **Tracking files** in designated folders
- **Detecting changes** via content hashing
- **Processing pipelines**: extract → chunk → embed → index
- **Maintaining sync status** between source files and vector store
- **Managing artifacts** (extracted text, images, chunks)

### Architecture

```
Source Files → FileVault → Vector Store
     ↓             ↓            ↓
  .docx        .vault/   Embeddings
  .pdf         ├── meta.json
  .txt         ├── vault/
               │   └── refined.md
               └── images/
```

### Key Concepts

| Concept | Description |
|---------|-------------|
| **VaultEntry** | Represents a tracked file with its processing state |
| **SyncStatus** | Tracks synchronization state (InSync, SourceModified, etc.) |
| **ProcessingStage** | Pipeline progress (Source → Extracted → Memorized) |
| **WatchedFolder** | A folder being monitored for file changes |

---

## Quick Start

### 1. Install the Package

```bash
dotnet add package FluxIndex.Extensions.FileVault
```

### 2. Register Services

```csharp
using FluxIndex.Extensions.FileVault.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// Basic registration
builder.Services.AddFileVault(options =>
{
    options.VaultBasePath = @"C:\MyApp\.vault";
});

// With background processing (recommended for production)
builder.Services.AddFileVaultWithBackgroundProcessing(options =>
{
    options.VaultBasePath = @"C:\MyApp\.vault";
    options.MaxConcurrentProcessing = 4;
});
```

### 3. Use the Vault

```csharp
public class MyService
{
    private readonly IVault _vault;

    public MyService(IVault vault)
    {
        _vault = vault;
    }

    public async Task IndexDocumentAsync(string filePath)
    {
        // Memorize a file (extract → chunk → embed → index)
        var entry = await _vault.MemorizeAsync(filePath);

        Console.WriteLine($"Indexed {entry.FileName}: {entry.ChunkCount} chunks");
    }
}
```

---

## Configuration

### FileVaultOptions

```csharp
services.AddFileVault(options =>
{
    // Storage location for vault data
    options.VaultBasePath = @"D:\VaultData\.vault";

    // File size limits
    options.MaxFileSizeMB = 100;

    // File patterns
    options.DefaultIncludePatterns = ["*.pdf", "*.docx", "*.md", "*.txt"];
    options.DefaultExcludePatterns = ["~$*", "*.tmp", ".*"];

    // Real-time watching
    options.EnableRealTimeWatch = true;
    options.DebounceDelayMs = 500;
    options.WatcherBufferSize = 65536;

    // Queue processing
    options.EnableBackgroundProcessing = true;
    options.MaxConcurrentProcessing = 4;
    options.QueuePollingIntervalMs = 1000;

    // Retry behavior
    options.EnableAutoRetry = true;
    options.MaxRetryCount = 3;
    options.RetryDelayMs = 5000;

    // Chunking defaults
    options.Chunking.MaxChunkSize = 1024;
    options.Chunking.OverlapSize = 128;
    options.Chunking.Strategy = "Intelligent";
    options.Chunking.Language = "en"; // null for auto-detect

    // Maintenance
    options.VersionRetentionCount = 5;
    options.AutoCleanupOrphans = false;
});
```

### Configuration via appsettings.json

```json
{
  "FileVault": {
    "VaultBasePath": "D:\\VaultData\\.vault",
    "MaxFileSizeMB": 100,
    "EnableRealTimeWatch": true,
    "DebounceDelayMs": 500,
    "EnableBackgroundProcessing": true,
    "MaxConcurrentProcessing": 4,
    "Chunking": {
      "MaxChunkSize": 1024,
      "OverlapSize": 128,
      "Strategy": "Intelligent"
    },
    "DefaultIncludePatterns": ["*.pdf", "*.docx", "*.md"],
    "DefaultExcludePatterns": ["~$*", "*.tmp"]
  }
}
```

```csharp
builder.Services.Configure<FileVaultOptions>(
    builder.Configuration.GetSection(FileVaultOptions.SectionName));
builder.Services.AddFileVault();
```

---

## Core Operations

### Memorize (Full Pipeline)

Process a file through the complete pipeline: extract → chunk → embed → index.

```csharp
// Memorize a single file
var entry = await vault.MemorizeAsync("path/to/document.pdf");

// Check result
if (entry.Stage == ProcessingStage.Memorized)
{
    Console.WriteLine($"Success: {entry.ChunkCount} chunks indexed");
}
```

### Refresh (Re-chunk without Re-extract)

When vault files (refined.md, qa.md) are manually edited:

```csharp
// Skip extraction, re-chunk and re-embed
var entry = await vault.RefreshAsync("path/to/document.pdf");
```

### Detect Changes

Check if a file needs re-processing:

```csharp
var changes = await vault.DetectChangesAsync("path/to/document.pdf");

if (changes.SourceChanged)
{
    Console.WriteLine("Source file modified - needs re-memorization");
}

if (changes.VaultChanged)
{
    Console.WriteLine("Vault files modified - needs refresh");
}

// Get recommended action
switch (changes.RecommendedAction)
{
    case ChangeAction.Memorize:
        await vault.MemorizeAsync(changes.FilePath);
        break;
    case ChangeAction.Refresh:
        await vault.RefreshAsync(changes.FilePath);
        break;
    case ChangeAction.Remove:
        await vault.RemoveAsync(changes.FilePath);
        break;
    case ChangeAction.None:
        // Already in sync
        break;
}
```

### Get Entry Information

```csharp
// Get by file path
var entry = await vault.GetAsync("path/to/document.pdf");

// Get by filepath hash
var entry = await vault.GetByHashAsync("83d095c10b2f28b1");

// List all entries
var allEntries = await vault.ListAsync();

// List by processing stage
var memorized = await vault.ListAsync(ProcessingStage.Memorized);
```

### Remove Entry

```csharp
// Remove file from vault and vector store
await vault.RemoveAsync("path/to/document.pdf");
```

### View History and Diff

```csharp
// Get git diff for vault files
var diff = await vault.DiffAsync("path/to/document.pdf");

// Get commit history
var commits = await vault.LogAsync("path/to/document.pdf", maxCount: 10);
foreach (var commit in commits)
{
    Console.WriteLine($"{commit.Hash[..7]} - {commit.Message} ({commit.Date})");
}
```

---

## Folder Watching

### Add a Watched Folder

```csharp
var folder = await vault.AddWatchedFolderAsync(
    folderPath: @"D:\Documents\Research",
    name: "Research Papers",
    isRecursive: true,
    autoMemorize: true,  // Auto-process new files
    includePatterns: ["*.pdf", "*.docx"],
    excludePatterns: ["~$*", "draft_*"]);

Console.WriteLine($"Watching folder: {folder.Id}");
```

### Manage Watched Folders

```csharp
// List all watched folders
var folders = await vault.GetAllWatchedFoldersAsync();

// Get specific folder
var folder = await vault.GetWatchedFolderAsync(folderId);

// Pause watching
await vault.PauseWatchingAsync(folderId);

// Resume watching
await vault.ResumeWatchingAsync(folderId);

// Remove folder (optionally remove tracked files)
await vault.RemoveWatchedFolderAsync(folderId, removeTrackedFiles: true);
```

### Scan Folder

Trigger a manual scan to detect changes:

```csharp
// Scan by path
var result = await vault.ScanFolderAsync(@"D:\Documents\Research");

// Scan by folder ID
var result = await vault.ScanFolderAsync(folderId);

Console.WriteLine($"Scanned: {result.ScannedCount} files");
Console.WriteLine($"New: {result.NewFilesCount}");
Console.WriteLine($"Changed: {result.ChangedFilesCount}");
Console.WriteLine($"Orphaned: {result.OrphanedFilesCount}");
```

### Sync All

Synchronize all watched folders:

```csharp
var syncResult = await vault.SyncAsync();

Console.WriteLine($"Folders scanned: {syncResult.FoldersScanned}");
Console.WriteLine($"Memorize queued: {syncResult.MemorizeQueuedCount}");
Console.WriteLine($"Refresh queued: {syncResult.RefreshQueuedCount}");
Console.WriteLine($"Remove queued: {syncResult.RemoveQueuedCount}");
Console.WriteLine($"Duration: {syncResult.Duration.TotalSeconds:F1}s");

if (!syncResult.IsSuccess)
{
    foreach (var error in syncResult.Errors)
    {
        Console.WriteLine($"Error: {error.FilePath} - {error.ErrorMessage}");
    }
}
```

---

## Sync Status Management

FileVault tracks synchronization state for each entry:

### SyncStatus Enum

| Status | Description |
|--------|-------------|
| `InSync` | Source file and vector store are synchronized |
| `SourceModified` | Source file changed, needs re-memorization |
| `VaultModified` | Vault files changed, needs refresh |
| `SourceDeleted` | Source file deleted, pending removal |
| `RemovalPending` | Removal queued, not yet started |
| `RemovalPartial` | Removal in progress (vector deleted, storage pending) |
| `Error` | Error occurred during processing |

### Query by Status

```csharp
// Get entries needing sync (SourceModified or VaultModified)
var needingSync = await vault.GetEntriesNeedingSyncAsync();

// Get entries pending removal
var pendingRemovals = await vault.GetPendingRemovalsAsync();

// Get entries in error state
var errors = await vault.GetErrorEntriesAsync();

// Get entries by specific status
var sourceDeleted = await vault.ListByStatusAsync(SyncStatus.SourceDeleted);
```

### Status Overview

```csharp
var status = await vault.StatusAsync();

Console.WriteLine($"Total entries: {status.TotalEntries}");
Console.WriteLine($"  In sync: {status.InSyncCount}");
Console.WriteLine($"  Source modified: {status.SourceModifiedCount}");
Console.WriteLine($"  Source deleted: {status.SourceDeletedCount}");
Console.WriteLine($"  Errors: {status.ErrorCount}");
Console.WriteLine();
Console.WriteLine($"Processing stages:");
Console.WriteLine($"  Source: {status.SourceCount}");
Console.WriteLine($"  Extracted: {status.ExtractedCount}");
Console.WriteLine($"  Memorized: {status.MemorizedCount}");
Console.WriteLine();
Console.WriteLine($"Queue: {status.QueuedCount} queued, {status.ProcessingCount} processing");
Console.WriteLine($"Watchers: {status.ActiveWatcherCount} active, {status.PausedWatcherCount} paused");
```

---

## Background Processing

### Enable Background Service

```csharp
// Registers VaultBackgroundService as IHostedService
services.AddFileVaultWithBackgroundProcessing(options =>
{
    options.MaxConcurrentProcessing = 4;
    options.QueuePollingIntervalMs = 1000;
    options.EnableAutoRetry = true;
    options.MaxRetryCount = 3;
});
```

### Queue Management

```csharp
// Get queue status
var queueStatus = await vault.GetQueueStatusAsync();
Console.WriteLine($"Queued: {queueStatus.QueuedCount}");
Console.WriteLine($"Processing: {queueStatus.ProcessingCount}");
Console.WriteLine($"Failed: {queueStatus.FailedCount}");
Console.WriteLine($"Paused: {queueStatus.IsPaused}");

// Pause processing
await vault.PauseQueueAsync();

// Resume processing
await vault.ResumeQueueAsync();
```

### Recovery from Partial Failures

The background service automatically recovers from partial removal failures:

```csharp
// On startup, VaultBackgroundService calls:
// await RecoverPartialRemovalsAsync(ct);
//
// This finds entries with SyncStatus.RemovalPartial and completes
// the removal process (storage deletion after vector deletion).
```

---

## Advanced Integration

### With FileFlux (Document Processing)

```csharp
// Register FileFlux services first
services.AddFileFluxIntegration(options =>
{
    options.DefaultChunkingStrategy = ChunkingStrategies.Intelligent;
    options.DefaultLanguage = "en";
});

// Then register FileVault with FileFlux integration
services.AddFileVaultWithFileFlux(options =>
{
    options.VaultBasePath = vaultPath;
});
```

### With FluxIndex (Full RAG Stack)

```csharp
// Register FluxIndex services
services.AddFluxIndex(options =>
{
    options.VectorStore = VectorStoreType.SQLite;
    options.DatabasePath = "fluxindex.db";
});

// Register embedding service (consumer app provides this)
services.AddSingleton<IEmbeddingService>(sp =>
    LMSupplyEmbedder.CreateAsync().GetAwaiter().GetResult());

// Register vector store
services.AddSingleton<IVectorStore, SqliteVectorStore>();

// Register FileVault with full FluxIndex integration
services.AddFileVaultWithFluxIndex(options =>
{
    options.VaultBasePath = vaultPath;
});
```

#### Vector Store Integration Flow

When you call `vault.MemorizeAsync()`, FileVault orchestrates the following pipeline:

```
Source File → Extract → Chunk → Embed → Index
    ↓            ↓         ↓        ↓        ↓
 .pdf/.docx   refined.md  chunks   float[]   IVectorStore
                                     ↓
                              IEmbeddingService
```

**Pipeline Steps**:

1. **Extract** (`IExtractor`): Converts source file to text/markdown
   - Uses FileFlux for PDF, DOCX, HTML, etc.
   - Output saved to `.vault/{hash}/vault/refined.md`

2. **Chunk** (`IChunker`): Splits content into semantic chunks
   - Configurable via `ChunkingOptions` (MaxChunkSize, OverlapSize, Strategy)
   - Output: `IReadOnlyList<ChunkResult>`

3. **Embed** (`IEmbeddingService`): Generates vector embeddings
   - Consumer app provides the embedding service (LMSupply, OpenAI, etc.)
   - Each chunk gets a float[] embedding vector

4. **Index** (`IVectorStore`): Stores chunks with embeddings
   - Chunks stored with `FilepathHash` as document identifier
   - Enables search by file or across all files

**Key Integration Points**:

```csharp
// The IVectorStore receives chunks like this:
await vectorStore.UpsertAsync(new DocumentChunk
{
    Id = chunkId,
    DocumentId = entry.FilepathHash,  // Links chunk to VaultEntry
    Content = chunk.Text,
    Embedding = embeddingVector,
    Metadata = new Dictionary<string, object>
    {
        ["source_path"] = entry.SourcePath,
        ["file_name"] = entry.FileName,
        ["chunk_index"] = chunk.Index
    }
});
```

**Querying Indexed Content**:

```csharp
// Search across all indexed files
var results = await vectorStore.SearchAsync(
    queryEmbedding,
    topK: 10);

// Filter by specific file
var results = await vectorStore.SearchAsync(
    queryEmbedding,
    topK: 10,
    filter: new { DocumentId = entry.FilepathHash });
```

### Multi-Tenant (Scope-Isolated) Usage

For multi-tenant applications (e.g., per-user or per-workspace vaults), create isolated IVault instances:

```csharp
public interface IScopedVaultService
{
    Task<VaultEntry> MemorizeAsync(string scopeId, string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<VaultEntry>> ListAsync(string scopeId, CancellationToken ct = default);
    Task RemoveAsync(string scopeId, string filePath, CancellationToken ct = default);
}

public class ScopedVaultService : IScopedVaultService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _basePath;

    public ScopedVaultService(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _basePath = configuration["FileVault:BasePath"] ?? "./data/vaults";
    }

    public async Task<VaultEntry> MemorizeAsync(string scopeId, string filePath, CancellationToken ct = default)
    {
        var vault = GetOrCreateVault(scopeId);
        return await vault.MemorizeAsync(filePath, ct);
    }

    public async Task<IReadOnlyList<VaultEntry>> ListAsync(string scopeId, CancellationToken ct = default)
    {
        var vault = GetOrCreateVault(scopeId);
        return await vault.ListAsync(ct: ct);
    }

    public async Task RemoveAsync(string scopeId, string filePath, CancellationToken ct = default)
    {
        var vault = GetOrCreateVault(scopeId);
        await vault.RemoveAsync(filePath, ct);
    }

    private IVault GetOrCreateVault(string scopeId)
    {
        // Each scope gets its own vault directory
        var scopePath = Path.Combine(_basePath, scopeId, ".vault");

        // Create vault with scope-specific path
        var options = new FileVaultOptions
        {
            VaultBasePath = scopePath
        };

        return ActivatorUtilities.CreateInstance<Vault>(
            _serviceProvider,
            Options.Create(options));
    }
}
```

**Directory Structure for Multi-Tenant**:

```
/data/vaults/
├── tenant-a/
│   └── .vault/
│       ├── {hash1}/
│       │   ├── meta.json
│       │   └── vault/refined.md
│       └── {hash2}/
├── tenant-b/
│   └── .vault/
│       └── {hash3}/
└── tenant-c/
    └── .vault/
```

**DI Registration for Multi-Tenant**:

```csharp
// Register base services without vault instance
services.AddFileVaultServices();  // Registers IExtractor, IChunker, etc.

// Register scoped vault service
services.AddSingleton<IScopedVaultService, ScopedVaultService>();

// Each tenant gets isolated:
// - Vault metadata (.vault directory)
// - Vector store data (via FilepathHash prefixing or separate DBs)
// - File watching (optional, per-tenant folders)
```

### Custom Pipeline Components

```csharp
// Custom extractor
public class MyExtractor : IExtractor
{
    public Task<ExtractionResult> ExtractAsync(string filePath, CancellationToken ct)
    {
        // Custom extraction logic
    }
}

// Custom chunker
public class MyChunker : IChunker
{
    public Task<IReadOnlyList<ChunkResult>> ChunkAsync(
        string content, ChunkingOptions options, CancellationToken ct)
    {
        // Custom chunking logic
    }
}

// Register custom components
services.AddSingleton<IExtractor, MyExtractor>();
services.AddSingleton<IChunker, MyChunker>();
services.AddFileVault();
```

### Custom Git Service

```csharp
// Disable git versioning
public class NoOpGitService : IGitService
{
    public Task InitAsync(string path, CancellationToken ct) => Task.CompletedTask;
    public Task<string> CommitAsync(string path, string message, CancellationToken ct)
        => Task.FromResult(string.Empty);
    public Task<GitStatus> StatusAsync(string path, CancellationToken ct)
        => Task.FromResult(new GitStatus());
    // ... other methods
}

services.UseFileVaultGitService<NoOpGitService>();
```

---

## Best Practices

### 1. Use Background Processing for Production

```csharp
// Always use background processing for production workloads
services.AddFileVaultWithBackgroundProcessing(options =>
{
    options.MaxConcurrentProcessing = Environment.ProcessorCount;
});
```

### 2. Handle Large File Collections

```csharp
// For large collections, batch operations
var entries = await vault.ListAsync(ProcessingStage.Memorized);
var batches = entries.Chunk(100);

foreach (var batch in batches)
{
    foreach (var entry in batch)
    {
        // Process entry
    }

    // Allow other operations
    await Task.Delay(100);
}
```

### 3. Monitor Sync Status

```csharp
// Periodically check for issues
var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
while (await timer.WaitForNextTickAsync())
{
    var errors = await vault.GetErrorEntriesAsync();
    if (errors.Any())
    {
        logger.LogWarning("Found {Count} entries in error state", errors.Count);
        // Alert or retry logic
    }
}
```

### 4. Graceful Shutdown

```csharp
// The background service handles graceful shutdown automatically
// But for manual operations:
public async Task ShutdownAsync(CancellationToken ct)
{
    await vault.PauseQueueAsync(ct);

    // Wait for current operations to complete
    var status = await vault.GetQueueStatusAsync(ct);
    while (status.ProcessingCount > 0 && !ct.IsCancellationRequested)
    {
        await Task.Delay(100, ct);
        status = await vault.GetQueueStatusAsync(ct);
    }
}
```

### 5. Organize Watched Folders

```csharp
// Use descriptive names and appropriate patterns
await vault.AddWatchedFolderAsync(
    @"D:\Documents\Contracts",
    name: "Legal Contracts",
    isRecursive: true,
    autoMemorize: true,
    includePatterns: ["*.pdf", "*.docx"],
    excludePatterns: ["draft_*", "old_*", "~$*"]);

await vault.AddWatchedFolderAsync(
    @"D:\Documents\TechDocs",
    name: "Technical Documentation",
    isRecursive: true,
    autoMemorize: false,  // Manual review before indexing
    includePatterns: ["*.md", "*.txt", "*.html"]);
```

---

## Troubleshooting

### Common Issues

#### Files Not Being Detected

```csharp
// Check if file matches include patterns
var options = serviceProvider.GetRequiredService<IOptions<FileVaultOptions>>();
var patterns = options.Value.DefaultIncludePatterns;
Console.WriteLine($"Include patterns: {string.Join(", ", patterns)}");

// Check if file is excluded
var excludes = options.Value.DefaultExcludePatterns;
Console.WriteLine($"Exclude patterns: {string.Join(", ", excludes)}");

// Check file size
var fileSize = new FileInfo(filePath).Length;
var maxSize = options.Value.MaxFileSizeBytes;
Console.WriteLine($"File size: {fileSize}, Max: {maxSize}");
```

#### Queue Not Processing

```csharp
// Check queue status
var status = await vault.GetQueueStatusAsync();
if (status.IsPaused)
{
    Console.WriteLine("Queue is paused - resuming...");
    await vault.ResumeQueueAsync();
}

// Check for failed items
Console.WriteLine($"Failed items: {status.FailedCount}");
```

#### Partial Removal Stuck

```csharp
// Check for entries stuck in RemovalPartial state
var partial = await vault.ListByStatusAsync(SyncStatus.RemovalPartial);
foreach (var entry in partial)
{
    Console.WriteLine($"Stuck: {entry.SourcePath}, Phase: {entry.RemovalPhase}");

    // The background service should recover these automatically
    // If not, check logs for errors
}
```

#### High Memory Usage

```csharp
// Reduce concurrent processing
options.MaxConcurrentProcessing = 2;

// Reduce watcher buffer
options.WatcherBufferSize = 32768;

// Process files sequentially for very large files
options.Chunking.MaxChunkSize = 512;  // Smaller chunks
```

### Logging

Enable detailed logging for troubleshooting:

```csharp
builder.Logging.AddFilter("FluxIndex.Extensions.FileVault", LogLevel.Debug);
```

### Diagnostic Commands

```csharp
// Full status report
var status = await vault.StatusAsync();
var queueStatus = await vault.GetQueueStatusAsync();
var folders = await vault.GetAllWatchedFoldersAsync();

Console.WriteLine("=== FileVault Diagnostic Report ===");
Console.WriteLine($"Vault Path: {vault.VaultBasePath}");
Console.WriteLine($"Total Entries: {status.TotalEntries}");
Console.WriteLine($"Queue Status: {queueStatus.QueuedCount} queued, {queueStatus.ProcessingCount} processing");
Console.WriteLine($"Watched Folders: {folders.Count}");
Console.WriteLine($"Active Watchers: {status.ActiveWatcherCount}");
Console.WriteLine($"Errors: {status.ErrorCount}");
Console.WriteLine($"Storage Size: {status.TotalStorageSizeBytes / 1024 / 1024:F1} MB");
```

---

## API Reference

### IVault Interface

| Method | Description |
|--------|-------------|
| `MemorizeAsync` | Full pipeline processing |
| `RefreshAsync` | Re-chunk without re-extraction |
| `SyncAsync` | Sync all watched folders |
| `DetectChangesAsync` | Check for file changes |
| `GetAsync` | Get entry by file path |
| `GetByHashAsync` | Get entry by hash |
| `ListAsync` | List entries with optional filter |
| `RemoveAsync` | Remove entry and vector data |
| `StatusAsync` | Get vault status summary |
| `DiffAsync` | Get git diff for entry |
| `LogAsync` | Get commit history |
| `AddWatchedFolderAsync` | Add folder to watch |
| `RemoveWatchedFolderAsync` | Stop watching folder |
| `ScanFolderAsync` | Manual folder scan |
| `PauseQueueAsync` | Pause background processing |
| `ResumeQueueAsync` | Resume background processing |
| `ListByStatusAsync` | Query by sync status |
| `GetPendingRemovalsAsync` | Get removal-pending entries |
| `GetErrorEntriesAsync` | Get entries in error state |

### VaultEntry Properties

| Property | Type | Description |
|----------|------|-------------|
| `SourcePath` | string | Full path to source file |
| `FileName` | string | File name with extension |
| `FilepathHash` | string | Unique hash of file path |
| `Stage` | ProcessingStage | Current pipeline stage |
| `SyncStatus` | SyncStatus | Synchronization state |
| `ChunkCount` | int | Number of indexed chunks |
| `SourceContentHash` | ContentHash? | Hash of source content |
| `LastProcessedAt` | DateTimeOffset? | Last successful processing |
| `LastSyncCheckAt` | DateTimeOffset? | Last sync check time |
| `LastError` | string? | Last error message |
| `RetryCount` | int | Number of retry attempts (resets on success) |
| `RemovalPhase` | string? | Current removal phase |

---

## Version History

| Version | Changes |
|---------|---------|
| 0.5.0 | Initial release with core functionality |
| 0.5.1 | Added SyncStatus state management |
| 0.5.2 | Added partial removal recovery |
| 0.5.3 | Added status-based query APIs |
| 0.5.7 | Added RetryCount tracking, Vector Store integration docs, Multi-tenant usage examples |
