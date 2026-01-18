namespace FluxIndex.Extensions.FileVault.Interfaces;

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
    /// Stores extracted content (markdown and plain text).
    /// </summary>
    Task StoreExtractAsync(Guid fileId, string markdown, string? plainText = null, CancellationToken ct = default);

    /// <summary>
    /// Stores extracted images.
    /// </summary>
    Task StoreImagesAsync(Guid fileId, IEnumerable<ImageArtifact> images, CancellationToken ct = default);

    /// <summary>
    /// Stores chunk data.
    /// </summary>
    Task StoreChunksAsync(Guid fileId, IEnumerable<ChunkArtifact> chunks, CancellationToken ct = default);

    /// <summary>
    /// Stores QA pairs.
    /// </summary>
    Task StoreQAPairsAsync(Guid fileId, IEnumerable<QAPairArtifact> qaPairs, CancellationToken ct = default);

    /// <summary>
    /// Stores enrichment data.
    /// </summary>
    Task StoreEnrichmentAsync(Guid fileId, EnrichmentArtifact enrichment, CancellationToken ct = default);

    /// <summary>
    /// Gets extracted content.
    /// </summary>
    Task<(string? Markdown, string? PlainText)> GetExtractAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Gets extracted images.
    /// </summary>
    Task<IReadOnlyList<ImageArtifact>> GetImagesAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Gets chunk data.
    /// </summary>
    Task<IReadOnlyList<ChunkArtifact>> GetChunksAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Gets QA pairs.
    /// </summary>
    Task<IReadOnlyList<QAPairArtifact>> GetQAPairsAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Gets enrichment data.
    /// </summary>
    Task<EnrichmentArtifact?> GetEnrichmentAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Creates a version snapshot.
    /// </summary>
    Task CreateVersionSnapshotAsync(Guid fileId, int version, CancellationToken ct = default);

    /// <summary>
    /// Gets version snapshots.
    /// </summary>
    Task<IReadOnlyList<VersionSnapshot>> GetVersionSnapshotsAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all artifacts for a file.
    /// </summary>
    Task DeleteArtifactsAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Gets total storage size for a file.
    /// </summary>
    Task<long> GetStorageSizeAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Checks if artifacts exist for a file.
    /// </summary>
    Task<bool> ArtifactsExistAsync(Guid fileId, CancellationToken ct = default);
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
/// Represents an extracted image.
/// </summary>
public sealed class ImageArtifact
{
    public string Id { get; init; } = string.Empty;
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "application/octet-stream";
    public string? Description { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>
/// Represents a content chunk.
/// </summary>
public sealed class ChunkArtifact
{
    public int Index { get; init; }
    public string Content { get; init; } = string.Empty;
    public int TokenCount { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Represents a QA pair.
/// </summary>
public sealed class QAPairArtifact
{
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string? Context { get; init; }
    public float Confidence { get; init; }
}

/// <summary>
/// Represents enrichment data.
/// </summary>
public sealed class EnrichmentArtifact
{
    public string? Summary { get; init; }
    public IReadOnlyList<string>? Keywords { get; init; }
    public IReadOnlyList<string>? Entities { get; init; }
    public IReadOnlyList<string>? Topics { get; init; }
    public Dictionary<string, object>? CustomData { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a version snapshot.
/// </summary>
public sealed class VersionSnapshot
{
    public int Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? ContentHash { get; init; }
    public long StorageSize { get; init; }
}
