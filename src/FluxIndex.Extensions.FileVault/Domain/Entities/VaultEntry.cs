using System.Text.Json;
using System.Text.Json.Serialization;
using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Domain.ValueObjects;
using FluxIndex.Extensions.FileVault.Services;

namespace FluxIndex.Extensions.FileVault.Domain.Entities;

/// <summary>
/// Represents a file entry in the vault with its processing state.
/// Directory structure:
/// .vault/{filepath-hash}/
/// ├── meta.json          (git 추적 X)
/// ├── images/            (git 추적 X)
/// │   └── manifest.json
/// └── vault/             (git 추적 O)
///     ├── .git/
///     ├── refined.md     (추출 + 보정 결과)
///     ├── append-text.md (사용자 추가 텍스트)
///     └── qa.md          (사용자 Q and A)
/// </summary>
public sealed class VaultEntry
{
    /// <summary>
    /// Unique identifier for this vault entry.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Hash of the normalized file path (used as directory name).
    /// </summary>
    public string FilepathHash { get; private set; } = string.Empty;

    /// <summary>
    /// Original file path.
    /// </summary>
    public string SourcePath { get; private set; } = string.Empty;

    /// <summary>
    /// Content hash of the source file (for change detection).
    /// </summary>
    public ContentHash? SourceContentHash { get; private set; }

    /// <summary>
    /// File name without path.
    /// </summary>
    public string FileName => Path.GetFileName(SourcePath);

    /// <summary>
    /// Current processing stage.
    /// </summary>
    public ProcessingStage Stage { get; private set; }

    /// <summary>
    /// Base path for the vault (.vault directory).
    /// </summary>
    public string VaultBasePath { get; private set; } = string.Empty;

    /// <summary>
    /// Timestamp when source was registered.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Timestamp of last processing.
    /// </summary>
    public DateTimeOffset? LastProcessedAt { get; private set; }

    /// <summary>
    /// Last error message if processing failed.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Number of chunks indexed to DB.
    /// </summary>
    public int ChunkCount { get; private set; }

    /// <summary>
    /// Current synchronization status with source file and vector store.
    /// </summary>
    public SyncStatus SyncStatus { get; private set; }

    /// <summary>
    /// Timestamp of the last sync status check.
    /// </summary>
    public DateTimeOffset? LastSyncCheckAt { get; private set; }

    /// <summary>
    /// Current removal phase when SyncStatus is RemovalPartial.
    /// "Vector" = vector store chunks deleted, "Storage" = pending.
    /// null when not in removal.
    /// </summary>
    public string? RemovalPhase { get; private set; }

    private VaultEntry() { }

    /// <summary>
    /// Creates a new vault entry for a source file.
    /// </summary>
    public static VaultEntry Create(string sourcePath, string vaultBasePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(vaultBasePath);

        var fullPath = Path.GetFullPath(sourcePath);
        var filepathHash = FilepathHasher.ComputeHash(fullPath);

        return new VaultEntry
        {
            Id = Guid.NewGuid(),
            SourcePath = fullPath,
            FilepathHash = filepathHash,
            VaultBasePath = vaultBasePath,
            Stage = ProcessingStage.Source,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Loads an existing vault entry from disk.
    /// </summary>
    public static VaultEntry? Load(string entryPath, string vaultBasePath)
    {
        var metaPath = Path.Combine(entryPath, "meta.json");
        if (!File.Exists(metaPath))
            return null;

        try
        {
            var json = File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<EntryMetadata>(json, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            });
            if (meta == null)
                return null;

            var entry = new VaultEntry
            {
                Id = meta.Id,
                SourcePath = meta.SourcePath,
                FilepathHash = meta.FilepathHash,
                SourceContentHash = !string.IsNullOrEmpty(meta.SourceContentHash)
                    ? ContentHash.FromHex(meta.SourceContentHash)
                    : null,
                VaultBasePath = vaultBasePath,
                Stage = meta.Stage,
                CreatedAt = meta.CreatedAt,
                LastProcessedAt = meta.LastProcessedAt,
                LastError = meta.LastError,
                ChunkCount = meta.ChunkCount,
                SyncStatus = meta.SyncStatus,
                LastSyncCheckAt = meta.LastSyncCheckAt,
                RemovalPhase = meta.RemovalPhase
            };

            return entry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a vault entry by filepath hash.
    /// </summary>
    public static VaultEntry? LoadByHash(string filepathHash, string vaultBasePath)
    {
        if (!FilepathHasher.IsValidHash(filepathHash))
            return null;

        var entryPath = Path.Combine(vaultBasePath, filepathHash);
        return Load(entryPath, vaultBasePath);
    }

    /// <summary>
    /// Marks the entry as extracted with the given content hash.
    /// </summary>
    public void MarkExtracted(ContentHash contentHash)
    {
        Stage = ProcessingStage.Extracted;
        SourceContentHash = contentHash;
        LastProcessedAt = DateTimeOffset.UtcNow;
        LastError = null;
    }

    /// <summary>
    /// Marks the entry as memorized (indexed to DB).
    /// </summary>
    public void MarkMemorized(int chunkCount)
    {
        Stage = ProcessingStage.Memorized;
        ChunkCount = chunkCount;
        LastProcessedAt = DateTimeOffset.UtcNow;
        LastError = null;
    }

    /// <summary>
    /// Marks the entry with an error.
    /// </summary>
    public void MarkError(string errorMessage)
    {
        LastError = errorMessage;
        LastProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Resets to Source stage for reprocessing.
    /// </summary>
    public void ResetToSource()
    {
        Stage = ProcessingStage.Source;
        ChunkCount = 0;
        LastError = null;
        SyncStatus = SyncStatus.InSync;
        RemovalPhase = null;
    }

    /// <summary>
    /// Updates the source content hash for change detection.
    /// </summary>
    public void UpdateSourceContentHash(ContentHash contentHash)
    {
        SourceContentHash = contentHash;
    }

    /// <summary>
    /// Updates the sync status and records the check time.
    /// </summary>
    public void UpdateSyncStatus(SyncStatus status)
    {
        SyncStatus = status;
        LastSyncCheckAt = DateTimeOffset.UtcNow;

        // Clear removal phase if not in removal state
        if (status != SyncStatus.RemovalPending && status != SyncStatus.RemovalPartial)
        {
            RemovalPhase = null;
        }
    }

    /// <summary>
    /// Marks the entry as having a deleted source file.
    /// </summary>
    public void MarkSourceDeleted()
    {
        SyncStatus = SyncStatus.SourceDeleted;
        LastSyncCheckAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the entry as pending removal (queued for processing).
    /// </summary>
    public void MarkRemovalPending()
    {
        SyncStatus = SyncStatus.RemovalPending;
        RemovalPhase = null;
        LastSyncCheckAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the entry as partially removed (specified phase completed).
    /// </summary>
    /// <param name="phase">The phase that was completed ("Vector" or "Storage").</param>
    public void MarkRemovalPartial(string phase)
    {
        SyncStatus = SyncStatus.RemovalPartial;
        RemovalPhase = phase;
        LastSyncCheckAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the entry as in sync after successful memorize/refresh.
    /// </summary>
    public void MarkInSync()
    {
        SyncStatus = SyncStatus.InSync;
        RemovalPhase = null;
        LastSyncCheckAt = DateTimeOffset.UtcNow;
        LastError = null;
    }

    /// <summary>
    /// Marks the entry with an error sync status.
    /// </summary>
    public void MarkSyncError(string errorMessage)
    {
        SyncStatus = SyncStatus.Error;
        LastError = errorMessage;
        LastSyncCheckAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Saves entry metadata to disk.
    /// </summary>
    public void SaveMetadata()
    {
        Directory.CreateDirectory(EntryPath);

        var meta = new EntryMetadata
        {
            Id = Id,
            SourcePath = SourcePath,
            FilepathHash = FilepathHash,
            SourceContentHash = SourceContentHash?.Value,
            Stage = Stage,
            CreatedAt = CreatedAt,
            LastProcessedAt = LastProcessedAt,
            LastError = LastError,
            ChunkCount = ChunkCount,
            SyncStatus = SyncStatus,
            LastSyncCheckAt = LastSyncCheckAt,
            RemovalPhase = RemovalPhase
        };

        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });

        File.WriteAllText(MetaPath, json);
    }

    // === Path Properties ===

    /// <summary>
    /// Path to the entry directory (.vault/{filepath-hash}/).
    /// </summary>
    public string EntryPath => Path.Combine(VaultBasePath, FilepathHash);

    /// <summary>
    /// Path to meta.json.
    /// </summary>
    public string MetaPath => Path.Combine(EntryPath, "meta.json");

    /// <summary>
    /// Path to images directory.
    /// </summary>
    public string ImagesPath => Path.Combine(EntryPath, "images");

    /// <summary>
    /// Path to images manifest.
    /// </summary>
    public string ImagesManifestPath => Path.Combine(ImagesPath, "manifest.json");

    /// <summary>
    /// Path to the vault subdirectory (git-tracked).
    /// </summary>
    public string VaultPath => Path.Combine(EntryPath, "vault");

    /// <summary>
    /// Path to refined.md (extracted + refined content).
    /// </summary>
    public string RefinedMdPath => Path.Combine(VaultPath, "refined.md");

    /// <summary>
    /// Path to append-text.md (user-added text).
    /// </summary>
    public string AppendTextPath => Path.Combine(VaultPath, "append-text.md");

    /// <summary>
    /// Path to qa.md (user Q and A).
    /// </summary>
    public string QaPath => Path.Combine(VaultPath, "qa.md");

    /// <summary>
    /// Path to .gitignore in the entry directory.
    /// </summary>
    public string GitignorePath => Path.Combine(EntryPath, ".gitignore");

    /// <summary>
    /// Checks if the source file still exists.
    /// </summary>
    public bool SourceExists => File.Exists(SourcePath);

    /// <summary>
    /// Checks if the vault directory exists.
    /// </summary>
    public bool VaultExists => Directory.Exists(VaultPath);

    /// <summary>
    /// Checks if refined.md exists.
    /// </summary>
    public bool RefinedExists => File.Exists(RefinedMdPath);

    /// <summary>
    /// Metadata for JSON serialization.
    /// </summary>
    private sealed class EntryMetadata
    {
        public Guid Id { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string FilepathHash { get; set; } = string.Empty;
        public string? SourceContentHash { get; set; }
        public ProcessingStage Stage { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastProcessedAt { get; set; }
        public string? LastError { get; set; }
        public int ChunkCount { get; set; }

        // SyncStatus fields
        public SyncStatus SyncStatus { get; set; }
        public DateTimeOffset? LastSyncCheckAt { get; set; }
        public string? RemovalPhase { get; set; }
    }
}
