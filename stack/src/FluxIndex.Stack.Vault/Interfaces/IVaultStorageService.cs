using FluxIndex.Stack.Vault.Entities;

namespace FluxIndex.Stack.Vault.Interfaces;

/// <summary>
/// Service for managing vault artifact storage.
/// </summary>
public interface IVaultStorageService
{
    /// <summary>
    /// Gets the base storage path for a tracked file.
    /// </summary>
    string GetFileStoragePath(Guid trackedFileId);

    /// <summary>
    /// Gets the path for a specific artifact type.
    /// </summary>
    string GetArtifactPath(Guid trackedFileId, ArtifactType artifactType);

    /// <summary>
    /// Stores extracted content for a file.
    /// </summary>
    Task StoreExtractAsync(Guid trackedFileId, string markdownContent, string? plainTextContent = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores images extracted from a file.
    /// </summary>
    Task StoreImagesAsync(Guid trackedFileId, IEnumerable<ImageArtifact> images, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores chunk data for a file.
    /// </summary>
    Task StoreChunksAsync(Guid trackedFileId, object chunkData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores Q&A pairs for a file.
    /// </summary>
    Task StoreQAPairsAsync(Guid trackedFileId, object qaPairs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores enrichment data for a file.
    /// </summary>
    Task StoreEnrichmentAsync(Guid trackedFileId, object enrichmentData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves extracted content for a file.
    /// </summary>
    Task<(string? Markdown, string? PlainText)> GetExtractAsync(Guid trackedFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves images for a file.
    /// </summary>
    Task<List<ImageArtifact>> GetImagesAsync(Guid trackedFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a version snapshot.
    /// </summary>
    Task CreateVersionSnapshotAsync(Guid trackedFileId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all artifacts for a file.
    /// </summary>
    Task DeleteArtifactsAsync(Guid trackedFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total storage size for a file's artifacts.
    /// </summary>
    Task<long> GetStorageSizeAsync(Guid trackedFileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if artifacts exist for a file.
    /// </summary>
    Task<bool> ArtifactsExistAsync(Guid trackedFileId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Types of artifacts stored in the vault.
/// </summary>
public enum ArtifactType
{
    Extract,
    Images,
    Chunks,
    QA,
    Enrichment,
    Versions
}

/// <summary>
/// Represents an extracted image artifact.
/// </summary>
public class ImageArtifact
{
    public string FileName { get; init; } = string.Empty;
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "image/png";
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? AltText { get; init; }
}
