namespace FluxIndex.Core.Domain.Entities;

/// <summary>
/// Represents an image extracted from document content (e.g., base64 embedded images).
/// </summary>
public class ExtractedImage
{
    /// <summary>
    /// Unique identifier for the extracted image.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// ID of the document this image was extracted from.
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// ID of the chunk this image was found in (if applicable).
    /// </summary>
    public string? ChunkId { get; set; }

    /// <summary>
    /// Original position/index of the image in the source content.
    /// </summary>
    public int PositionIndex { get; set; }

    /// <summary>
    /// MIME type of the image (e.g., "image/png", "image/jpeg").
    /// </summary>
    public string MimeType { get; set; } = "image/png";

    /// <summary>
    /// File extension derived from MIME type (e.g., ".png", ".jpg").
    /// </summary>
    public string FileExtension { get; set; } = ".png";

    /// <summary>
    /// Storage path or key where the image is stored.
    /// </summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// Original alt text from markdown if available.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// LLM-generated description of the image content.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Size of the image in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Image width in pixels (if known).
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Image height in pixels (if known).
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Hash of the image content for deduplication.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the image was extracted.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata for the image.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Creates a new ExtractedImage instance.
    /// </summary>
    public static ExtractedImage Create(
        string documentId,
        string mimeType,
        byte[] imageData,
        int positionIndex = 0,
        string? chunkId = null,
        string? altText = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(mimeType);
        ArgumentNullException.ThrowIfNull(imageData);

        var extension = GetExtensionFromMimeType(mimeType);
        var hash = ComputeHash(imageData);

        return new ExtractedImage
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = documentId,
            ChunkId = chunkId,
            PositionIndex = positionIndex,
            MimeType = mimeType,
            FileExtension = extension,
            AltText = altText,
            SizeBytes = imageData.Length,
            ContentHash = hash,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets file extension from MIME type.
    /// </summary>
    public static string GetExtensionFromMimeType(string mimeType)
    {
        return mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "image/ico" or "image/x-icon" => ".ico",
            _ => ".bin"
        };
    }

    /// <summary>
    /// Computes SHA256 hash of image data.
    /// </summary>
    private static string ComputeHash(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Generates a storage filename based on document ID and image ID.
    /// </summary>
    public string GenerateStorageFileName()
    {
        return $"{DocumentId}_{Id}{FileExtension}";
    }

    /// <summary>
    /// Sets the description from LLM image-to-text processing.
    /// </summary>
    public void SetDescription(string description)
    {
        Description = description;
        Metadata["DescriptionGeneratedAt"] = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets image dimensions.
    /// </summary>
    public void SetDimensions(int width, int height)
    {
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Result of image extraction from content.
/// </summary>
public class ImageExtractionResult
{
    /// <summary>
    /// The cleaned content with images replaced by placeholders.
    /// </summary>
    public string CleanedContent { get; set; } = string.Empty;

    /// <summary>
    /// List of extracted images.
    /// </summary>
    public List<ExtractedImageData> ExtractedImages { get; set; } = new();

    /// <summary>
    /// Whether any images were extracted.
    /// </summary>
    public bool HasImages => ExtractedImages.Count > 0;

    /// <summary>
    /// Total size of all extracted images in bytes.
    /// </summary>
    public long TotalImageBytes => ExtractedImages.Sum(img => img.Data.Length);
}

/// <summary>
/// Raw extracted image data before storage.
/// </summary>
public class ExtractedImageData
{
    /// <summary>
    /// Position index in original content.
    /// </summary>
    public int PositionIndex { get; set; }

    /// <summary>
    /// MIME type of the image.
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Raw image bytes.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// Original alt text from markdown.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Original markdown string that was extracted.
    /// </summary>
    public string OriginalMarkdown { get; set; } = string.Empty;

    /// <summary>
    /// Placeholder text that replaced the image in cleaned content.
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;
}
