using FluxIndex.Stack.Domain.Entities;
using FluxIndex.Stack.Vault.Enums;

namespace FluxIndex.Stack.Vault.Entities;

/// <summary>
/// Represents a file being tracked by the vault.
/// </summary>
public class TrackedFile
{
    public Guid Id { get; private set; }
    public string SourcePath { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string FileExtension { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string? ContentHash { get; private set; }
    public DateTime? FileModifiedAt { get; private set; }
    public TrackedFileStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? MemorizedAt { get; private set; }
    public DateTime? LastSyncedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    // Foreign keys
    public Guid? WatchedFolderId { get; private set; }
    public Guid? DocumentId { get; private set; }

    // Navigation properties
    public WatchedFolder? WatchedFolder { get; private set; }
    public Document? Document { get; private set; }
    public List<TrackedFileVersion> Versions { get; private set; } = new();

    private TrackedFile() { } // EF Core

    public static TrackedFile Create(
        string sourcePath,
        Guid? watchedFolderId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fileName = Path.GetFileName(sourcePath);
        var extension = Path.GetExtension(sourcePath);

        return new TrackedFile
        {
            Id = Guid.NewGuid(),
            SourcePath = sourcePath,
            FileName = fileName,
            FileExtension = extension,
            Status = TrackedFileStatus.Untracked,
            Version = 0,
            WatchedFolderId = watchedFolderId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateFileInfo(long fileSize, DateTime fileModifiedAt, string contentHash)
    {
        FileSize = fileSize;
        FileModifiedAt = fileModifiedAt;
        ContentHash = contentHash;
    }

    public void MarkAsQueued()
    {
        Status = TrackedFileStatus.Queued;
        ErrorMessage = null;
    }

    public void MarkAsProcessing()
    {
        Status = TrackedFileStatus.Processing;
    }

    public void MarkAsMemorized(Guid documentId)
    {
        Status = TrackedFileStatus.Memorized;
        DocumentId = documentId;
        Version++;
        MemorizedAt = DateTime.UtcNow;
        LastSyncedAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkAsStale()
    {
        Status = TrackedFileStatus.Stale;
    }

    public void MarkAsOrphaned()
    {
        Status = TrackedFileStatus.Orphaned;
    }

    public void MarkAsRemoved()
    {
        Status = TrackedFileStatus.Removed;
    }

    public void MarkAsError(string errorMessage)
    {
        Status = TrackedFileStatus.Error;
        ErrorMessage = errorMessage;
    }

    public void ResetToUntracked()
    {
        Status = TrackedFileStatus.Untracked;
        DocumentId = null;
        ErrorMessage = null;
    }

    public void SetDocumentId(Guid documentId)
    {
        DocumentId = documentId;
    }

    public void UpdateSyncTime()
    {
        LastSyncedAt = DateTime.UtcNow;
    }

    public bool HasChangedSince(string newContentHash)
    {
        return ContentHash != newContentHash;
    }
}
