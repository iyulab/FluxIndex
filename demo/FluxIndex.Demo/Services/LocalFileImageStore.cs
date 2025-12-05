using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Demo.Services;

/// <summary>
/// Local filesystem implementation of IImageStore for the Demo application.
/// Stores extracted images in a configurable directory.
/// </summary>
public class LocalFileImageStore : IImageStore
{
    private readonly string _basePath;
    private readonly string? _publicUrlBase;
    private readonly ILogger<LocalFileImageStore>? _logger;
    private readonly Dictionary<string, ImageMetadata> _imageIndex = new();
    private readonly object _lock = new();

    public LocalFileImageStore(
        string basePath,
        string? publicUrlBase = null,
        ILogger<LocalFileImageStore>? logger = null)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _publicUrlBase = publicUrlBase;
        _logger = logger;

        // Ensure the base directory exists
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
            _logger?.LogInformation("Created image store directory: {Path}", _basePath);
        }

        // Load existing image index
        LoadIndex();
    }

    /// <inheritdoc />
    public async Task<string> StoreAsync(
        string documentId,
        byte[] imageData,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var imageId = Guid.NewGuid().ToString();
        return await StoreAsync(imageId, documentId, imageData, mimeType, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> StoreAsync(
        string imageId,
        string documentId,
        byte[] imageData,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageId);
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(imageData);
        ArgumentNullException.ThrowIfNull(mimeType);

        var extension = ExtractedImage.GetExtensionFromMimeType(mimeType);
        var fileName = $"{documentId}_{imageId}{extension}";
        var documentDir = Path.Combine(_basePath, documentId);
        var filePath = Path.Combine(documentDir, fileName);
        var storagePath = Path.Combine(documentId, fileName);

        // Ensure document directory exists
        if (!Directory.Exists(documentDir))
        {
            Directory.CreateDirectory(documentDir);
        }

        // Write the image file
        await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);

        // Update index
        lock (_lock)
        {
            _imageIndex[storagePath] = new ImageMetadata
            {
                ImageId = imageId,
                DocumentId = documentId,
                MimeType = mimeType,
                StoragePath = storagePath,
                SizeBytes = imageData.Length,
                StoredAt = DateTime.UtcNow
            };
        }

        // Save index
        await SaveIndexAsync();

        _logger?.LogDebug(
            "Stored image {ImageId} for document {DocumentId} at {Path} ({Size} bytes)",
            imageId, documentId, storagePath, imageData.Length);

        return storagePath;
    }

    /// <inheritdoc />
    public async Task<ImageData?> GetAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        var filePath = Path.Combine(_basePath, storagePath);

        if (!File.Exists(filePath))
        {
            _logger?.LogDebug("Image not found at path: {Path}", storagePath);
            return null;
        }

        ImageMetadata? metadata;
        lock (_lock)
        {
            _imageIndex.TryGetValue(storagePath, out metadata);
        }

        var data = await File.ReadAllBytesAsync(filePath, cancellationToken);

        return new ImageData
        {
            Data = data,
            MimeType = metadata?.MimeType ?? GuessMimeType(storagePath),
            Extension = Path.GetExtension(storagePath),
            StoragePath = storagePath,
            StoredAt = metadata?.StoredAt
        };
    }

    /// <inheritdoc />
    public async Task<ImageData?> GetByIdAsync(string imageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageId);

        string? storagePath;
        lock (_lock)
        {
            storagePath = _imageIndex
                .FirstOrDefault(kvp => kvp.Value.ImageId == imageId)
                .Key;
        }

        if (storagePath == null)
        {
            return null;
        }

        return await GetAsync(storagePath, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        var filePath = Path.Combine(_basePath, storagePath);

        if (!File.Exists(filePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(filePath);

        lock (_lock)
        {
            _imageIndex.Remove(storagePath);
        }

        _ = SaveIndexAsync();

        _logger?.LogDebug("Deleted image at path: {Path}", storagePath);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<int> DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        var documentDir = Path.Combine(_basePath, documentId);
        var deletedCount = 0;

        if (Directory.Exists(documentDir))
        {
            var files = Directory.GetFiles(documentDir);
            deletedCount = files.Length;

            // Delete all files
            foreach (var file in files)
            {
                File.Delete(file);
            }

            // Delete directory
            Directory.Delete(documentDir);

            // Remove from index
            lock (_lock)
            {
                var keysToRemove = _imageIndex
                    .Where(kvp => kvp.Value.DocumentId == documentId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _imageIndex.Remove(key);
                }
            }

            _ = SaveIndexAsync();

            _logger?.LogInformation(
                "Deleted {Count} images for document {DocumentId}",
                deletedCount, documentId);
        }

        return Task.FromResult(deletedCount);
    }

    /// <inheritdoc />
    public Task<IEnumerable<string>> ListByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        var documentDir = Path.Combine(_basePath, documentId);

        if (!Directory.Exists(documentDir))
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        var files = Directory.GetFiles(documentDir)
            .Select(f => Path.Combine(documentId, Path.GetFileName(f)))
            .ToList();

        return Task.FromResult<IEnumerable<string>>(files);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        var filePath = Path.Combine(_basePath, storagePath);
        return Task.FromResult(File.Exists(filePath));
    }

    /// <inheritdoc />
    public string? GetPublicUrl(string storagePath)
    {
        if (string.IsNullOrEmpty(_publicUrlBase))
        {
            return null;
        }

        return $"{_publicUrlBase.TrimEnd('/')}/{storagePath.Replace('\\', '/')}";
    }

    /// <inheritdoc />
    public Task<long> GetStorageSizeAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        var documentDir = Path.Combine(_basePath, documentId);

        if (!Directory.Exists(documentDir))
        {
            return Task.FromResult(0L);
        }

        var totalSize = Directory.GetFiles(documentDir)
            .Sum(f => new FileInfo(f).Length);

        return Task.FromResult(totalSize);
    }

    private void LoadIndex()
    {
        var indexPath = Path.Combine(_basePath, ".image-index.json");

        if (File.Exists(indexPath))
        {
            try
            {
                var json = File.ReadAllText(indexPath);
                var index = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ImageMetadata>>(json);
                if (index != null)
                {
                    lock (_lock)
                    {
                        foreach (var kvp in index)
                        {
                            _imageIndex[kvp.Key] = kvp.Value;
                        }
                    }
                    _logger?.LogDebug("Loaded image index with {Count} entries", _imageIndex.Count);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load image index, starting fresh");
            }
        }
    }

    private async Task SaveIndexAsync()
    {
        var indexPath = Path.Combine(_basePath, ".image-index.json");

        try
        {
            Dictionary<string, ImageMetadata> indexCopy;
            lock (_lock)
            {
                indexCopy = new Dictionary<string, ImageMetadata>(_imageIndex);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(indexCopy, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(indexPath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save image index");
        }
    }

    private static string GuessMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };
    }

    private class ImageMetadata
    {
        public string ImageId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string StoragePath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime StoredAt { get; set; }
    }
}

/// <summary>
/// Options for LocalFileImageStore configuration.
/// </summary>
public class LocalFileImageStoreOptions
{
    /// <summary>
    /// Base path for storing images. Defaults to "./images".
    /// </summary>
    public string BasePath { get; set; } = "./images";

    /// <summary>
    /// Base URL for public image access. If null, GetPublicUrl returns null.
    /// </summary>
    public string? PublicUrlBase { get; set; }
}
