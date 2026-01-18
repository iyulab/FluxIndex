using FluxIndex.Extensions.FileVault.Domain.Enums;
using FluxIndex.Extensions.FileVault.Domain.ValueObjects;

namespace FluxIndex.Extensions.FileVault.Domain.Entities;

/// <summary>
/// Represents a file being tracked by the vault.
/// Extends VaultEntry with additional status tracking for the file lifecycle.
/// </summary>
public sealed class TrackedFile
{
    public Guid Id { get; private set; }
    public string SourcePath { get; private set; }
    public string FileName => Path.GetFileName(SourcePath);
    public string FileExtension => Path.GetExtension(SourcePath);
    public long FileSize { get; private set; }
    public ContentHash ContentHash { get; private set; }
    public DateTimeOffset? FileModifiedAt { get; private set; }
    public TrackedFileStatus Status { get; private set; }
    public ProcessingStage ProcessingStage { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? MemorizedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? WatchedFolderId { get; private set; }
    public string? DocumentId { get; private set; }

    /// <summary>
    /// Associated VaultEntry for this tracked file.
    /// </summary>
    public VaultEntry? VaultEntry { get; private set; }

    private TrackedFile()
    {
        Id = Guid.NewGuid();
        SourcePath = string.Empty;
        ContentHash = ContentHash.Empty;
        Status = TrackedFileStatus.Untracked;
        ProcessingStage = ProcessingStage.Source;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Creates a new tracked file.
    /// </summary>
    public static TrackedFile Create(string sourcePath, Guid? watchedFolderId = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path cannot be empty", nameof(sourcePath));

        var normalizedPath = Path.GetFullPath(sourcePath);

        return new TrackedFile
        {
            SourcePath = normalizedPath,
            WatchedFolderId = watchedFolderId
        };
    }

    /// <summary>
    /// Associates this tracked file with a VaultEntry.
    /// </summary>
    public void SetVaultEntry(VaultEntry entry)
    {
        VaultEntry = entry ?? throw new ArgumentNullException(nameof(entry));
        ContentHash = entry.SourceHash;
    }

    /// <summary>
    /// Updates file information from the file system.
    /// </summary>
    public void UpdateFileInfo(long fileSize, DateTimeOffset fileModifiedAt, ContentHash contentHash)
    {
        FileSize = fileSize;
        FileModifiedAt = fileModifiedAt;
        ContentHash = contentHash;
    }

    /// <summary>
    /// Marks the file as queued for processing.
    /// </summary>
    public void MarkAsQueued()
    {
        Status = TrackedFileStatus.Queued;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marks the file as currently processing.
    /// </summary>
    public void MarkAsProcessing()
    {
        Status = TrackedFileStatus.Processing;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marks the file as successfully memorized.
    /// </summary>
    public void MarkAsMemorized(string? documentId = null)
    {
        Status = TrackedFileStatus.Memorized;
        ProcessingStage = ProcessingStage.Memorized;
        MemorizedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = DateTimeOffset.UtcNow;
        Version++;
        ErrorMessage = null;

        if (documentId != null)
            DocumentId = documentId;
    }

    /// <summary>
    /// Marks the file as stale (content changed since last memorization).
    /// </summary>
    public void MarkAsStale()
    {
        if (Status == TrackedFileStatus.Memorized)
        {
            Status = TrackedFileStatus.Stale;
        }
    }

    /// <summary>
    /// Marks the file as orphaned (source file deleted).
    /// </summary>
    public void MarkAsOrphaned()
    {
        Status = TrackedFileStatus.Orphaned;
    }

    /// <summary>
    /// Marks the file as removed from vault.
    /// </summary>
    public void MarkAsRemoved()
    {
        Status = TrackedFileStatus.Removed;
    }

    /// <summary>
    /// Marks the file as having an error.
    /// </summary>
    public void MarkAsError(string errorMessage)
    {
        Status = TrackedFileStatus.Error;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Resets the file to untracked status.
    /// </summary>
    public void ResetToUntracked()
    {
        Status = TrackedFileStatus.Untracked;
        ProcessingStage = ProcessingStage.Source;
        ErrorMessage = null;
    }

    /// <summary>
    /// Updates the processing stage.
    /// </summary>
    public void UpdateProcessingStage(ProcessingStage stage)
    {
        ProcessingStage = stage;
    }

    /// <summary>
    /// Sets the document ID after indexing.
    /// </summary>
    public void SetDocumentId(string documentId)
    {
        DocumentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
    }

    /// <summary>
    /// Updates the last synced timestamp.
    /// </summary>
    public void UpdateSyncTime()
    {
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Checks if the content has changed since last sync.
    /// </summary>
    public bool HasChangedSince(ContentHash newContentHash)
    {
        return !ContentHash.Equals(newContentHash);
    }

    /// <summary>
    /// Checks if the source file exists.
    /// </summary>
    public bool SourceExists => File.Exists(SourcePath);

    /// <summary>
    /// Gets the effective status combining TrackedFileStatus and ProcessingStage.
    /// </summary>
    public string EffectiveStatus => Status switch
    {
        TrackedFileStatus.Processing => ProcessingStage switch
        {
            ProcessingStage.Extracted => "Extracting",
            ProcessingStage.Refined => "Refining",
            ProcessingStage.Chunked => "Chunking",
            _ => "Processing"
        },
        TrackedFileStatus.Memorized => "Indexed",
        TrackedFileStatus.Stale => "Changed",
        TrackedFileStatus.Queued => "Queued",
        TrackedFileStatus.Orphaned => "Orphaned",
        TrackedFileStatus.Removed => "Removed",
        TrackedFileStatus.Error => "Error",
        _ => "Untracked"
    };
}
