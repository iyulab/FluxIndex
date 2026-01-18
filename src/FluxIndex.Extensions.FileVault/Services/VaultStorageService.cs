using System.Text.Json;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// File-based vault artifact storage service.
/// </summary>
public sealed class VaultStorageService : IVaultStorageService
{
    private readonly ILogger<VaultStorageService> _logger;
    private readonly FileVaultOptions _options;
    private readonly string _basePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public VaultStorageService(
        ILogger<VaultStorageService> logger,
        IOptions<FileVaultOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new FileVaultOptions();
        _basePath = _options.VaultBasePath ?? Path.Combine(Directory.GetCurrentDirectory(), _options.VaultDirectoryName);

        // Ensure base path exists
        Directory.CreateDirectory(_basePath);
    }

    public string GetFileStoragePath(Guid trackedFileId)
    {
        return Path.Combine(_basePath, trackedFileId.ToString("N"));
    }

    public string GetArtifactPath(Guid trackedFileId, ArtifactType artifactType)
    {
        var basePath = GetFileStoragePath(trackedFileId);
        return artifactType switch
        {
            ArtifactType.Extract => Path.Combine(basePath, "extract"),
            ArtifactType.Images => Path.Combine(basePath, "images"),
            ArtifactType.Chunks => Path.Combine(basePath, "chunks"),
            ArtifactType.QA => Path.Combine(basePath, "qa"),
            ArtifactType.Enrichment => Path.Combine(basePath, "refine"),
            ArtifactType.Versions => Path.Combine(basePath, "versions"),
            _ => basePath
        };
    }

    public async Task StoreExtractAsync(Guid fileId, string markdown, string? plainText = null, CancellationToken ct = default)
    {
        var path = GetArtifactPath(fileId, ArtifactType.Extract);
        Directory.CreateDirectory(path);

        await File.WriteAllTextAsync(Path.Combine(path, "content.md"), markdown, ct);

        if (plainText != null)
        {
            await File.WriteAllTextAsync(Path.Combine(path, "content.txt"), plainText, ct);
        }

        _logger.LogDebug("Stored extract for file {FileId}", fileId);
    }

    public async Task StoreImagesAsync(Guid fileId, IEnumerable<ImageArtifact> images, CancellationToken ct = default)
    {
        var path = GetArtifactPath(fileId, ArtifactType.Images);
        Directory.CreateDirectory(path);

        var manifest = new List<ImageManifestEntry>();
        var index = 0;

        foreach (var image in images)
        {
            var extension = GetExtensionFromContentType(image.ContentType);
            var fileName = $"image_{index:D3}{extension}";
            var filePath = Path.Combine(path, fileName);

            await File.WriteAllBytesAsync(filePath, image.Data, ct);

            manifest.Add(new ImageManifestEntry
            {
                Id = image.Id,
                FileName = fileName,
                ContentType = image.ContentType,
                Description = image.Description,
                Width = image.Width,
                Height = image.Height,
                Size = image.Data.Length
            });

            index++;
        }

        // Write manifest
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(path, "manifest.json"), manifestJson, ct);

        _logger.LogDebug("Stored {Count} images for file {FileId}", index, fileId);
    }

    public async Task StoreChunksAsync(Guid fileId, IEnumerable<ChunkArtifact> chunks, CancellationToken ct = default)
    {
        var path = GetArtifactPath(fileId, ArtifactType.Chunks);
        Directory.CreateDirectory(path);

        var chunkList = chunks.ToList();
        var json = JsonSerializer.Serialize(chunkList, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(path, "chunks.json"), json, ct);

        _logger.LogDebug("Stored {Count} chunks for file {FileId}", chunkList.Count, fileId);
    }

    public async Task StoreQAPairsAsync(Guid fileId, IEnumerable<QAPairArtifact> qaPairs, CancellationToken ct = default)
    {
        var path = GetArtifactPath(fileId, ArtifactType.QA);
        Directory.CreateDirectory(path);

        var qaList = qaPairs.ToList();
        var json = JsonSerializer.Serialize(qaList, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(path, "pairs.json"), json, ct);

        _logger.LogDebug("Stored {Count} QA pairs for file {FileId}", qaList.Count, fileId);
    }

    public async Task StoreEnrichmentAsync(Guid fileId, EnrichmentArtifact enrichment, CancellationToken ct = default)
    {
        var path = GetArtifactPath(fileId, ArtifactType.Enrichment);
        Directory.CreateDirectory(path);

        var json = JsonSerializer.Serialize(enrichment, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(path, "enrichment.json"), json, ct);

        _logger.LogDebug("Stored enrichment for file {FileId}", fileId);
    }

    public async Task<(string? Markdown, string? PlainText)> GetExtractAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = GetArtifactPath(fileId, ArtifactType.Extract);
        string? markdown = null;
        string? plainText = null;

        var mdPath = Path.Combine(path, "content.md");
        if (File.Exists(mdPath))
        {
            markdown = await File.ReadAllTextAsync(mdPath, ct);
        }

        var txtPath = Path.Combine(path, "content.txt");
        if (File.Exists(txtPath))
        {
            plainText = await File.ReadAllTextAsync(txtPath, ct);
        }

        return (markdown, plainText);
    }

    public async Task<IReadOnlyList<ImageArtifact>> GetImagesAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = GetArtifactPath(fileId, ArtifactType.Images);
        var manifestPath = Path.Combine(path, "manifest.json");

        if (!File.Exists(manifestPath))
            return Array.Empty<ImageArtifact>();

        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<List<ImageManifestEntry>>(json, JsonOptions);

        if (manifest == null)
            return Array.Empty<ImageArtifact>();

        var images = new List<ImageArtifact>();
        foreach (var entry in manifest)
        {
            var imagePath = Path.Combine(path, entry.FileName);
            if (File.Exists(imagePath))
            {
                var data = await File.ReadAllBytesAsync(imagePath, ct);
                images.Add(new ImageArtifact
                {
                    Id = entry.Id,
                    Data = data,
                    ContentType = entry.ContentType,
                    Description = entry.Description,
                    Width = entry.Width,
                    Height = entry.Height
                });
            }
        }

        return images;
    }

    public async Task<IReadOnlyList<ChunkArtifact>> GetChunksAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = Path.Combine(GetArtifactPath(fileId, ArtifactType.Chunks), "chunks.json");

        if (!File.Exists(path))
            return Array.Empty<ChunkArtifact>();

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<List<ChunkArtifact>>(json, JsonOptions) ?? [];
    }

    public async Task<IReadOnlyList<QAPairArtifact>> GetQAPairsAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = Path.Combine(GetArtifactPath(fileId, ArtifactType.QA), "pairs.json");

        if (!File.Exists(path))
            return Array.Empty<QAPairArtifact>();

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<List<QAPairArtifact>>(json, JsonOptions) ?? [];
    }

    public async Task<EnrichmentArtifact?> GetEnrichmentAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = Path.Combine(GetArtifactPath(fileId, ArtifactType.Enrichment), "enrichment.json");

        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<EnrichmentArtifact>(json, JsonOptions);
    }

    public async Task CreateVersionSnapshotAsync(Guid fileId, int version, CancellationToken ct = default)
    {
        var basePath = GetFileStoragePath(fileId);
        var versionsPath = GetArtifactPath(fileId, ArtifactType.Versions);
        var versionPath = Path.Combine(versionsPath, $"v{version}");

        Directory.CreateDirectory(versionPath);

        // Copy current artifacts to version snapshot
        var extractPath = GetArtifactPath(fileId, ArtifactType.Extract);
        if (Directory.Exists(extractPath))
        {
            CopyDirectory(extractPath, Path.Combine(versionPath, "extract"));
        }

        var chunksPath = GetArtifactPath(fileId, ArtifactType.Chunks);
        if (Directory.Exists(chunksPath))
        {
            CopyDirectory(chunksPath, Path.Combine(versionPath, "chunks"));
        }

        // Write version manifest
        var manifest = new VersionManifest
        {
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            StorageSize = await GetStorageSizeAsync(fileId, ct)
        };

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(versionPath, "manifest.json"), json, ct);

        _logger.LogDebug("Created version {Version} snapshot for file {FileId}", version, fileId);

        // Cleanup old versions if exceeds retention
        await CleanupOldVersionsAsync(fileId, ct);
    }

    public async Task<IReadOnlyList<VersionSnapshot>> GetVersionSnapshotsAsync(Guid fileId, CancellationToken ct = default)
    {
        var versionsPath = GetArtifactPath(fileId, ArtifactType.Versions);

        if (!Directory.Exists(versionsPath))
            return Array.Empty<VersionSnapshot>();

        var snapshots = new List<VersionSnapshot>();

        foreach (var versionDir in Directory.GetDirectories(versionsPath, "v*"))
        {
            var manifestPath = Path.Combine(versionDir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var json = await File.ReadAllTextAsync(manifestPath, ct);
                var manifest = JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions);
                if (manifest != null)
                {
                    snapshots.Add(new VersionSnapshot
                    {
                        Version = manifest.Version,
                        CreatedAt = manifest.CreatedAt,
                        ContentHash = manifest.ContentHash,
                        StorageSize = manifest.StorageSize
                    });
                }
            }
        }

        return snapshots.OrderByDescending(v => v.Version).ToList();
    }

    public Task DeleteArtifactsAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = GetFileStoragePath(fileId);

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            _logger.LogDebug("Deleted artifacts for file {FileId}", fileId);
        }

        return Task.CompletedTask;
    }

    public Task<long> GetStorageSizeAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = GetFileStoragePath(fileId);

        if (!Directory.Exists(path))
            return Task.FromResult(0L);

        var size = new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);

        return Task.FromResult(size);
    }

    public Task<bool> ArtifactsExistAsync(Guid fileId, CancellationToken ct = default)
    {
        var path = GetFileStoragePath(fileId);
        return Task.FromResult(Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any());
    }

    private async Task CleanupOldVersionsAsync(Guid fileId, CancellationToken ct)
    {
        var versionsPath = GetArtifactPath(fileId, ArtifactType.Versions);

        if (!Directory.Exists(versionsPath))
            return;

        var versions = (await GetVersionSnapshotsAsync(fileId, ct))
            .OrderByDescending(v => v.Version)
            .ToList();

        if (versions.Count <= _options.VersionRetentionCount)
            return;

        var toDelete = versions.Skip(_options.VersionRetentionCount).ToList();
        foreach (var version in toDelete)
        {
            var versionPath = Path.Combine(versionsPath, $"v{version.Version}");
            if (Directory.Exists(versionPath))
            {
                Directory.Delete(versionPath, recursive: true);
                _logger.LogDebug("Deleted old version {Version} for file {FileId}", version.Version, fileId);
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    private static string GetExtensionFromContentType(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".bin"
    };

    private sealed class ImageManifestEntry
    {
        public string Id { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public string? Description { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public long Size { get; init; }
    }

    private sealed class VersionManifest
    {
        public int Version { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string? ContentHash { get; init; }
        public long StorageSize { get; init; }
    }
}
