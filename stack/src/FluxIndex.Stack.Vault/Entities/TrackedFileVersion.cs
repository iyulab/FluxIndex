namespace FluxIndex.Stack.Vault.Entities;

/// <summary>
/// Represents a version snapshot of a tracked file.
/// </summary>
public class TrackedFileVersion
{
    public Guid Id { get; private set; }
    public Guid TrackedFileId { get; private set; }
    public int Version { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Artifact availability flags
    public bool HasExtract { get; private set; }
    public bool HasChunks { get; private set; }
    public bool HasImages { get; private set; }
    public bool HasQA { get; private set; }
    public bool HasEnrichment { get; private set; }

    // Navigation
    public TrackedFile? TrackedFile { get; private set; }

    private TrackedFileVersion() { } // EF Core

    public static TrackedFileVersion Create(
        Guid trackedFileId,
        int version,
        string contentHash,
        long fileSize)
    {
        return new TrackedFileVersion
        {
            Id = Guid.NewGuid(),
            TrackedFileId = trackedFileId,
            Version = version,
            ContentHash = contentHash,
            FileSize = fileSize,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkExtractAvailable()
    {
        HasExtract = true;
    }

    public void MarkChunksAvailable()
    {
        HasChunks = true;
    }

    public void MarkImagesAvailable()
    {
        HasImages = true;
    }

    public void MarkQAAvailable()
    {
        HasQA = true;
    }

    public void MarkEnrichmentAvailable()
    {
        HasEnrichment = true;
    }
}
