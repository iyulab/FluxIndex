using System.Text.Json;
using FluxIndex.Extensions.FileVault.Domain.Entities;
using FluxIndex.Extensions.FileVault.Interfaces;
using FluxIndex.Extensions.FileVault.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Extensions.FileVault.Services;

/// <summary>
/// File-based vault storage service implementing the new directory structure.
/// </summary>
public sealed class VaultStorageService : IVaultStorageService
{
    private readonly ILogger<VaultStorageService> _logger;
    private readonly IGitService _gitService;
    private readonly string _basePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Content of .gitignore file in entry directory.
    /// Excludes meta.json, images/, and extracted.md from git tracking.
    /// </summary>
    private const string GitignoreContent = """
        # FileVault gitignore - only vault/ directory is tracked
        meta.json
        images/
        extracted.md
        """;

    public VaultStorageService(
        ILogger<VaultStorageService> logger,
        IGitService gitService,
        IOptions<FileVaultOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));

        var opts = options?.Value ?? new FileVaultOptions();
        _basePath = opts.VaultBasePath ?? Path.Combine(Directory.GetCurrentDirectory(), opts.VaultDirectoryName);

        // Ensure base path exists
        Directory.CreateDirectory(_basePath);
    }

    public string BasePath => _basePath;

    public async Task InitializeEntryAsync(VaultEntry entry, CancellationToken ct = default)
    {
        // Create entry directory
        Directory.CreateDirectory(entry.EntryPath);

        // Create vault subdirectory
        Directory.CreateDirectory(entry.VaultPath);

        // Create .gitignore to exclude meta.json and images/
        await CreateGitignoreAsync(entry, ct);

        // Initialize git in vault/ subdirectory
        await _gitService.InitAsync(entry.VaultPath, ct);

        // Save entry metadata
        entry.SaveMetadata();

        _logger.LogDebug("Initialized entry storage at {EntryPath}", entry.EntryPath);
    }

    public async Task CreateGitignoreAsync(VaultEntry entry, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(entry.GitignorePath, GitignoreContent, ct);
    }

    public async Task StoreExtractedContentAsync(VaultEntry entry, string content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(entry.EntryPath);
        await File.WriteAllTextAsync(entry.ExtractedMdPath, content, ct);
        _logger.LogDebug("Stored extracted content for entry {EntryId}", entry.Id);
    }

    public async Task<string?> GetExtractedContentAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (!File.Exists(entry.ExtractedMdPath))
            return null;

        return await File.ReadAllTextAsync(entry.ExtractedMdPath, ct);
    }

    public async Task StoreRefinedContentAsync(VaultEntry entry, string content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(entry.VaultPath);
        await File.WriteAllTextAsync(entry.RefinedMdPath, content, ct);
        _logger.LogDebug("Stored refined content for entry {EntryId}", entry.Id);
    }

    public async Task<string?> GetRefinedContentAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (!File.Exists(entry.RefinedMdPath))
            return null;

        return await File.ReadAllTextAsync(entry.RefinedMdPath, ct);
    }

    public async Task StoreImagesAsync(VaultEntry entry, IEnumerable<ImageArtifact> images, CancellationToken ct = default)
    {
        Directory.CreateDirectory(entry.ImagesPath);

        var manifest = new List<ImageManifestEntry>();
        var index = 0;

        foreach (var image in images)
        {
            var extension = GetExtensionFromContentType(image.ContentType);
            var fileName = $"img_{index:D3}{extension}";
            var filePath = Path.Combine(entry.ImagesPath, fileName);

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
        await File.WriteAllTextAsync(entry.ImagesManifestPath, manifestJson, ct);

        _logger.LogDebug("Stored {Count} images for entry {EntryId}", index, entry.Id);
    }

    public async Task<IReadOnlyList<ImageArtifact>> GetImagesAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (!File.Exists(entry.ImagesManifestPath))
            return [];

        var json = await File.ReadAllTextAsync(entry.ImagesManifestPath, ct);
        var manifest = JsonSerializer.Deserialize<List<ImageManifestEntry>>(json, JsonOptions);

        if (manifest == null)
            return [];

        var images = new List<ImageArtifact>();
        foreach (var item in manifest)
        {
            var imagePath = Path.Combine(entry.ImagesPath, item.FileName);
            if (File.Exists(imagePath))
            {
                var data = await File.ReadAllBytesAsync(imagePath, ct);
                images.Add(new ImageArtifact
                {
                    Id = item.Id,
                    Data = data,
                    ContentType = item.ContentType,
                    Description = item.Description,
                    Width = item.Width,
                    Height = item.Height
                });
            }
        }

        return images;
    }

    public async Task<VaultTextContent> GetAllVaultContentAsync(VaultEntry entry, CancellationToken ct = default)
    {
        string? refinedContent = null;
        string? appendText = null;
        string? qaContent = null;

        if (File.Exists(entry.RefinedMdPath))
            refinedContent = await File.ReadAllTextAsync(entry.RefinedMdPath, ct);

        if (File.Exists(entry.AppendTextPath))
            appendText = await File.ReadAllTextAsync(entry.AppendTextPath, ct);

        if (File.Exists(entry.QaPath))
            qaContent = await File.ReadAllTextAsync(entry.QaPath, ct);

        return new VaultTextContent
        {
            RefinedContent = refinedContent,
            AppendText = appendText,
            QaContent = qaContent
        };
    }

    public async Task StoreAppendTextAsync(VaultEntry entry, string content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(entry.VaultPath);
        await File.WriteAllTextAsync(entry.AppendTextPath, content, ct);
        _logger.LogDebug("Stored append-text for entry {EntryId}", entry.Id);
    }

    public async Task StoreQaContentAsync(VaultEntry entry, string content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(entry.VaultPath);
        await File.WriteAllTextAsync(entry.QaPath, content, ct);
        _logger.LogDebug("Stored QA content for entry {EntryId}", entry.Id);
    }

    public Task DeleteEntryStorageAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (Directory.Exists(entry.EntryPath))
        {
            // Delete .git directory first (may have read-only files)
            var gitDir = Path.Combine(entry.VaultPath, ".git");
            if (Directory.Exists(gitDir))
            {
                SetAttributesNormal(new DirectoryInfo(gitDir));
            }

            Directory.Delete(entry.EntryPath, recursive: true);
            _logger.LogDebug("Deleted storage for entry {EntryId}", entry.Id);
        }

        return Task.CompletedTask;
    }

    public Task<long> GetStorageSizeAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (!Directory.Exists(entry.EntryPath))
            return Task.FromResult(0L);

        var size = new DirectoryInfo(entry.EntryPath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);

        return Task.FromResult(size);
    }

    public bool EntryStorageExists(VaultEntry entry)
    {
        return Directory.Exists(entry.EntryPath);
    }

    public IEnumerable<string> ListEntryDirectories()
    {
        if (!Directory.Exists(_basePath))
            yield break;

        foreach (var dir in Directory.GetDirectories(_basePath))
        {
            var dirName = Path.GetFileName(dir);
            // Only return directories that look like filepath hashes (16 hex chars)
            if (FilepathHasher.IsValidHash(dirName))
            {
                yield return dir;
            }
        }
    }

    private static void SetAttributesNormal(DirectoryInfo dir)
    {
        foreach (var subDir in dir.GetDirectories())
        {
            SetAttributesNormal(subDir);
        }

        foreach (var file in dir.GetFiles())
        {
            file.Attributes = FileAttributes.Normal;
        }
    }

    private static string GetExtensionFromContentType(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
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
}
