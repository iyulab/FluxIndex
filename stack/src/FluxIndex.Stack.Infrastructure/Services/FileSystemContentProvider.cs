using FluxIndex.Stack.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// File system based implementation of IDocumentContentProvider.
/// Stores document content on disk for efficient large file handling.
/// </summary>
public partial class FileSystemContentProvider : IDocumentContentProvider
{
    private readonly string _basePath;
    private readonly ILogger<FileSystemContentProvider> _logger;

    public FileSystemContentProvider(
        IConfiguration configuration,
        ILogger<FileSystemContentProvider> logger)
    {
        _basePath = configuration["FluxIndex:Content:StoragePath"] ?? "./content";
        _logger = logger;

        // Ensure base directory exists
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
            LogCreatedStorageDirectory(_logger, _basePath);
        }
    }

    public async Task<string> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var path = GetContentPath(documentId, ".txt");

        if (!File.Exists(path))
        {
            LogContentFileNotFound(_logger, documentId);
            return string.Empty;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task<byte[]> GetContentBytesAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var path = GetContentPath(documentId, ".bin");

        // Try binary file first, then text file
        if (!File.Exists(path))
        {
            path = GetContentPath(documentId, ".txt");
        }

        if (!File.Exists(path))
        {
            LogContentFileNotFound(_logger, documentId);
            return Array.Empty<byte>();
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public async Task StoreContentAsync(Guid documentId, string content, CancellationToken cancellationToken = default)
    {
        var path = GetContentPath(documentId, ".txt");

        // Ensure directory exists
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Sanitize null bytes (0x00) which are invalid in PostgreSQL TEXT columns
        // This is a workaround until FileFlux releases the TextSanitizer fix
        var sanitizedContent = SanitizeContent(content);

        await File.WriteAllTextAsync(path, sanitizedContent, cancellationToken);
        LogStoredTextContent(_logger, documentId, path);
    }

    public async Task StoreContentBytesAsync(Guid documentId, byte[] content, CancellationToken cancellationToken = default)
    {
        var path = GetContentPath(documentId, ".bin");

        // Ensure directory exists
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, content, cancellationToken);
        LogStoredBinaryContent(_logger, documentId, path);
    }

    public Task<bool> ContentExistsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var textPath = GetContentPath(documentId, ".txt");
        var binPath = GetContentPath(documentId, ".bin");

        return Task.FromResult(File.Exists(textPath) || File.Exists(binPath));
    }

    public Task DeleteContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var textPath = GetContentPath(documentId, ".txt");
        var binPath = GetContentPath(documentId, ".bin");

        if (File.Exists(textPath))
        {
            File.Delete(textPath);
            LogDeletedTextContent(_logger, documentId);
        }

        if (File.Exists(binPath))
        {
            File.Delete(binPath);
            LogDeletedBinaryContent(_logger, documentId);
        }

        return Task.CompletedTask;
    }

    public async Task StoreImageAsync(Guid documentId, string imageId, byte[] imageData, string contentType, CancellationToken cancellationToken = default)
    {
        var extension = GetExtensionForContentType(contentType);
        var path = GetImagePath(documentId, imageId, extension);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Store metadata file with content type
        var metaPath = GetImagePath(documentId, imageId, ".meta");
        await File.WriteAllTextAsync(metaPath, contentType, cancellationToken);

        // Store image data
        await File.WriteAllBytesAsync(path, imageData, cancellationToken);
        LogStoredImage(_logger, imageId, documentId, path);
    }

    public async Task<(byte[] Data, string ContentType)?> GetImageAsync(Guid documentId, string imageId, CancellationToken cancellationToken = default)
    {
        var imagesDir = GetImagesDirectory(documentId);
        if (!Directory.Exists(imagesDir))
        {
            return null;
        }

        // Find the image file (could be any extension)
        var pattern = $"{imageId}.*";
        var files = Directory.GetFiles(imagesDir, pattern);
        var imageFile = files.FirstOrDefault(f => !f.EndsWith(".meta", StringComparison.Ordinal));

        if (imageFile == null || !File.Exists(imageFile))
        {
            return null;
        }

        // Read content type from metadata
        var metaPath = GetImagePath(documentId, imageId, ".meta");
        var contentType = "image/png"; // default
        if (File.Exists(metaPath))
        {
            contentType = await File.ReadAllTextAsync(metaPath, cancellationToken);
        }

        var data = await File.ReadAllBytesAsync(imageFile, cancellationToken);
        return (data, contentType);
    }

    public Task<IReadOnlyList<string>> GetImageIdsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var imagesDir = GetImagesDirectory(documentId);
        if (!Directory.Exists(imagesDir))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        // Get all image files (exclude .meta files) and extract image IDs
        var imageIds = Directory.GetFiles(imagesDir)
            .Where(f => !f.EndsWith(".meta", StringComparison.Ordinal))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(imageIds);
    }

    public Task DeleteImagesAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var imagesDir = GetImagesDirectory(documentId);
        if (Directory.Exists(imagesDir))
        {
            Directory.Delete(imagesDir, recursive: true);
            LogDeletedImagesDirectory(_logger, documentId);
        }

        return Task.CompletedTask;
    }

    private string GetContentPath(Guid documentId, string extension)
    {
        // Use directory sharding based on first 2 characters of GUID for better filesystem performance
        var idString = documentId.ToString("N");
        var shard = idString.Substring(0, 2);

        return Path.Combine(_basePath, shard, $"{documentId}{extension}");
    }

    private string GetImagesDirectory(Guid documentId)
    {
        // Images are stored in a subdirectory: content/{shard}/{documentId}/images/
        var idString = documentId.ToString("N");
        var shard = idString.Substring(0, 2);

        return Path.Combine(_basePath, shard, documentId.ToString(), "images");
    }

    private string GetImagePath(Guid documentId, string imageId, string extension)
    {
        var imagesDir = GetImagesDirectory(documentId);
        return Path.Combine(imagesDir, $"{imageId}{extension}");
    }

    private static string GetExtensionForContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            _ => ".png" // default to PNG
        };
    }

    /// <summary>
    /// Removes null bytes (0x00) from text content.
    /// PDF/DOCX files may contain null bytes from embedded binary objects, form fields, or encoding artifacts.
    /// PostgreSQL TEXT columns reject null bytes as invalid UTF-8.
    /// </summary>
    private static string SanitizeContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        // Fast path: check if sanitization is needed
        if (!content.Contains('\0'))
            return content;

        return content.Replace("\0", string.Empty);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Created content storage directory: {Path}")]
    private static partial void LogCreatedStorageDirectory(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Content file not found for document {DocumentId}")]
    private static partial void LogContentFileNotFound(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored text content for document {DocumentId} at {Path}")]
    private static partial void LogStoredTextContent(ILogger logger, Guid documentId, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored binary content for document {DocumentId} at {Path}")]
    private static partial void LogStoredBinaryContent(ILogger logger, Guid documentId, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted text content for document {DocumentId}")]
    private static partial void LogDeletedTextContent(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted binary content for document {DocumentId}")]
    private static partial void LogDeletedBinaryContent(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored image {ImageId} for document {DocumentId} at {Path}")]
    private static partial void LogStoredImage(ILogger logger, string imageId, Guid documentId, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted images directory for document {DocumentId}")]
    private static partial void LogDeletedImagesDirectory(ILogger logger, Guid documentId);

    #endregion
}
