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
public sealed partial class VaultStorageService : IVaultStorageService
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

        // Initialize git in vault/ subdirectory (non-fatal — vault works without git)
        try
        {
            await _gitService.InitAsync(entry.VaultPath, ct);
        }
        catch (Exception ex)
        {
            LogGitInitFailed(_logger, entry.EntryPath, ex.Message);
        }

        // Save entry metadata — must always execute to prevent zombie entries
        entry.SaveMetadata();

        LogInitializedEntry(_logger, entry.EntryPath);
    }

    public async Task CreateGitignoreAsync(VaultEntry entry, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(entry.GitignorePath, GitignoreContent, ct);
    }

    public async Task StoreExtractedContentAsync(VaultEntry entry, string content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(entry.EntryPath);
        await File.WriteAllTextAsync(entry.ExtractedMdPath, content, ct);
        LogStoredExtracted(_logger, entry.Id);
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
        LogStoredRefined(_logger, entry.Id);
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
            // Use original image ID for consistent naming (fixes index mismatch bug)
            var fileName = $"{image.Id}{extension}";
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

        LogStoredImages(_logger, index, entry.Id);
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
        LogStoredAppendText(_logger, entry.Id);
    }

    public async Task StoreQaContentAsync(VaultEntry entry, string content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(entry.VaultPath);
        await File.WriteAllTextAsync(entry.QaPath, content, ct);
        LogStoredQa(_logger, entry.Id);
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
            LogDeletedStorage(_logger, entry.Id);
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

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initialized entry storage at {EntryPath}")]
    private static partial void LogInitializedEntry(ILogger logger, string entryPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored extracted content for entry {EntryId}")]
    private static partial void LogStoredExtracted(ILogger logger, Guid entryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored refined content for entry {EntryId}")]
    private static partial void LogStoredRefined(ILogger logger, Guid entryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored {Count} images for entry {EntryId}")]
    private static partial void LogStoredImages(ILogger logger, int count, Guid entryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored append-text for entry {EntryId}")]
    private static partial void LogStoredAppendText(ILogger logger, Guid entryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored QA content for entry {EntryId}")]
    private static partial void LogStoredQa(ILogger logger, Guid entryId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted storage for entry {EntryId}")]
    private static partial void LogDeletedStorage(ILogger logger, Guid entryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Git initialization failed for {EntryPath}: {Error}. Vault will operate without version tracking.")]
    private static partial void LogGitInitFailed(ILogger logger, string entryPath, string error);

    #endregion

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
