using System.Text.Json;
using FluxIndex.Stack.Vault.Interfaces;
using FluxIndex.Stack.Vault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Stack.Vault.Services;

/// <summary>
/// Service for managing vault artifact storage on the file system.
/// </summary>
public partial class VaultStorageService : IVaultStorageService
{
    private readonly ILogger<VaultStorageService> _logger;
    private readonly VaultOptions _options;
    private readonly string _basePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public VaultStorageService(
        ILogger<VaultStorageService> logger,
        IOptions<VaultOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _basePath = Path.GetFullPath(_options.StoragePath);

        // Ensure base directory exists
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
            _ => throw new ArgumentOutOfRangeException(nameof(artifactType))
        };
    }

    public async Task StoreExtractAsync(
        Guid trackedFileId,
        string markdownContent,
        string? plainTextContent = null,
        CancellationToken cancellationToken = default)
    {
        var extractPath = GetArtifactPath(trackedFileId, ArtifactType.Extract);
        Directory.CreateDirectory(extractPath);

        var mdPath = Path.Combine(extractPath, "content.md");
        await File.WriteAllTextAsync(mdPath, markdownContent, cancellationToken);
        LogStoredMarkdownExtract(_logger, trackedFileId, mdPath);

        if (!string.IsNullOrEmpty(plainTextContent))
        {
            var txtPath = Path.Combine(extractPath, "content.txt");
            await File.WriteAllTextAsync(txtPath, plainTextContent, cancellationToken);
        }
    }

    public async Task StoreImagesAsync(
        Guid trackedFileId,
        IEnumerable<ImageArtifact> images,
        CancellationToken cancellationToken = default)
    {
        var imagesPath = GetArtifactPath(trackedFileId, ArtifactType.Images);
        Directory.CreateDirectory(imagesPath);

        var imageList = images.ToList();
        var manifest = new List<ImageManifestEntry>();

        foreach (var image in imageList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imagePath = Path.Combine(imagesPath, image.FileName);
            await File.WriteAllBytesAsync(imagePath, image.Data, cancellationToken);

            manifest.Add(new ImageManifestEntry
            {
                FileName = image.FileName,
                ContentType = image.ContentType,
                Width = image.Width,
                Height = image.Height,
                AltText = image.AltText,
                Size = image.Data.Length
            });
        }

        var manifestPath = Path.Combine(imagesPath, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken);

        var imageCount = imageList.Count;
        LogStoredImages(_logger, imageCount, trackedFileId);
    }

    public async Task StoreChunksAsync(
        Guid trackedFileId,
        object chunkData,
        CancellationToken cancellationToken = default)
    {
        var chunksPath = GetArtifactPath(trackedFileId, ArtifactType.Chunks);
        Directory.CreateDirectory(chunksPath);

        var filePath = Path.Combine(chunksPath, "chunks.json");
        var json = JsonSerializer.Serialize(chunkData, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        LogStoredChunks(_logger, trackedFileId);
    }

    public async Task StoreQAPairsAsync(
        Guid trackedFileId,
        object qaPairs,
        CancellationToken cancellationToken = default)
    {
        var qaPath = GetArtifactPath(trackedFileId, ArtifactType.QA);
        Directory.CreateDirectory(qaPath);

        var filePath = Path.Combine(qaPath, "pairs.json");
        var json = JsonSerializer.Serialize(qaPairs, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        LogStoredQAPairs(_logger, trackedFileId);
    }

    public async Task StoreEnrichmentAsync(
        Guid trackedFileId,
        object enrichmentData,
        CancellationToken cancellationToken = default)
    {
        var enrichPath = GetArtifactPath(trackedFileId, ArtifactType.Enrichment);
        Directory.CreateDirectory(enrichPath);

        var filePath = Path.Combine(enrichPath, "enrichment.json");
        var json = JsonSerializer.Serialize(enrichmentData, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        LogStoredEnrichment(_logger, trackedFileId);
    }

    public async Task<(string? Markdown, string? PlainText)> GetExtractAsync(
        Guid trackedFileId,
        CancellationToken cancellationToken = default)
    {
        var extractPath = GetArtifactPath(trackedFileId, ArtifactType.Extract);

        string? markdown = null;
        string? plainText = null;

        var mdPath = Path.Combine(extractPath, "content.md");
        if (File.Exists(mdPath))
        {
            markdown = await File.ReadAllTextAsync(mdPath, cancellationToken);
        }

        var txtPath = Path.Combine(extractPath, "content.txt");
        if (File.Exists(txtPath))
        {
            plainText = await File.ReadAllTextAsync(txtPath, cancellationToken);
        }

        return (markdown, plainText);
    }

    public async Task<List<ImageArtifact>> GetImagesAsync(
        Guid trackedFileId,
        CancellationToken cancellationToken = default)
    {
        var imagesPath = GetArtifactPath(trackedFileId, ArtifactType.Images);
        var manifestPath = Path.Combine(imagesPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return new List<ImageArtifact>();
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<List<ImageManifestEntry>>(json, JsonOptions)
            ?? new List<ImageManifestEntry>();

        var images = new List<ImageArtifact>();
        foreach (var entry in manifest)
        {
            var imagePath = Path.Combine(imagesPath, entry.FileName);
            if (File.Exists(imagePath))
            {
                var data = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                images.Add(new ImageArtifact
                {
                    FileName = entry.FileName,
                    Data = data,
                    ContentType = entry.ContentType,
                    Width = entry.Width,
                    Height = entry.Height,
                    AltText = entry.AltText
                });
            }
        }

        return images;
    }

    public async Task CreateVersionSnapshotAsync(
        Guid trackedFileId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var versionsPath = GetArtifactPath(trackedFileId, ArtifactType.Versions);
        var versionPath = Path.Combine(versionsPath, $"v{version}");
        Directory.CreateDirectory(versionPath);

        var manifest = new VersionManifest
        {
            Version = version,
            CreatedAt = DateTime.UtcNow,
            Artifacts = new List<string>()
        };

        // Check which artifacts exist and record in manifest
        foreach (ArtifactType artifactType in Enum.GetValues<ArtifactType>())
        {
            if (artifactType == ArtifactType.Versions) continue;

            var artifactPath = GetArtifactPath(trackedFileId, artifactType);
            if (Directory.Exists(artifactPath))
            {
                manifest.Artifacts.Add(artifactType.ToString().ToLowerInvariant());
            }
        }

        var manifestPath = Path.Combine(versionPath, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken);

        LogCreatedVersionSnapshot(_logger, version, trackedFileId);
    }

    public Task DeleteArtifactsAsync(Guid trackedFileId, CancellationToken cancellationToken = default)
    {
        var basePath = GetFileStoragePath(trackedFileId);

        if (Directory.Exists(basePath))
        {
            Directory.Delete(basePath, recursive: true);
            LogDeletedArtifacts(_logger, trackedFileId);
        }

        return Task.CompletedTask;
    }

    public Task<long> GetStorageSizeAsync(Guid trackedFileId, CancellationToken cancellationToken = default)
    {
        var basePath = GetFileStoragePath(trackedFileId);

        if (!Directory.Exists(basePath))
        {
            return Task.FromResult(0L);
        }

        var size = new DirectoryInfo(basePath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);

        return Task.FromResult(size);
    }

    public Task<bool> ArtifactsExistAsync(Guid trackedFileId, CancellationToken cancellationToken = default)
    {
        var basePath = GetFileStoragePath(trackedFileId);
        return Task.FromResult(Directory.Exists(basePath) &&
            Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories).Any());
    }

    private sealed class ImageManifestEntry
    {
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string? AltText { get; init; }
        public long Size { get; init; }
    }

    private sealed class VersionManifest
    {
        public int Version { get; init; }
        public DateTime CreatedAt { get; init; }
        public List<string> Artifacts { get; init; } = new();
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored markdown extract for {FileId}: {Path}")]
    private static partial void LogStoredMarkdownExtract(ILogger logger, Guid fileId, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored {Count} images for {FileId}")]
    private static partial void LogStoredImages(ILogger logger, int count, Guid fileId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored chunks for {FileId}")]
    private static partial void LogStoredChunks(ILogger logger, Guid fileId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored QA pairs for {FileId}")]
    private static partial void LogStoredQAPairs(ILogger logger, Guid fileId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored enrichment for {FileId}")]
    private static partial void LogStoredEnrichment(ILogger logger, Guid fileId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created version {Version} snapshot for {FileId}")]
    private static partial void LogCreatedVersionSnapshot(ILogger logger, int version, Guid fileId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted artifacts for {FileId}")]
    private static partial void LogDeletedArtifacts(ILogger logger, Guid fileId);

    #endregion
}
