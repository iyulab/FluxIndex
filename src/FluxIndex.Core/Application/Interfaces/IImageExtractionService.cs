using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Service for extracting embedded images (e.g., base64) from document content.
/// </summary>
public interface IImageExtractionService
{
    /// <summary>
    /// Extracts all embedded base64 images from markdown/HTML content.
    /// </summary>
    /// <param name="content">The content containing embedded images.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result with cleaned content and extracted images.</returns>
    Task<ImageExtractionResult> ExtractImagesAsync(
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts images and stores them, returning cleaned content with references.
    /// </summary>
    /// <param name="documentId">ID of the document being processed.</param>
    /// <param name="content">The content containing embedded images.</param>
    /// <param name="imageStore">Image store for persisting extracted images.</param>
    /// <param name="placeholderFormat">Format for image placeholders. Use {id} for image ID, {url} for URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result with cleaned content and stored image references.</returns>
    Task<ImageProcessingResult> ExtractAndStoreAsync(
        string documentId,
        string content,
        IImageStore imageStore,
        string placeholderFormat = "[Image: {id}]",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if content contains embedded base64 images.
    /// </summary>
    /// <param name="content">The content to check.</param>
    /// <returns>True if embedded images are found.</returns>
    bool HasEmbeddedImages(string content);

    /// <summary>
    /// Counts the number of embedded images in content.
    /// </summary>
    /// <param name="content">The content to analyze.</param>
    /// <returns>Number of embedded images found.</returns>
    int CountEmbeddedImages(string content);

    /// <summary>
    /// Estimates the total size of embedded images in bytes.
    /// </summary>
    /// <param name="content">The content to analyze.</param>
    /// <returns>Estimated total size in bytes.</returns>
    long EstimateEmbeddedImageSize(string content);
}

/// <summary>
/// Result of image extraction and storage operation.
/// </summary>
public class ImageProcessingResult
{
    /// <summary>
    /// Content with embedded images replaced by placeholders or references.
    /// </summary>
    public string ProcessedContent { get; set; } = string.Empty;

    /// <summary>
    /// Original content before processing.
    /// </summary>
    public string OriginalContent { get; set; } = string.Empty;

    /// <summary>
    /// List of extracted and stored images.
    /// </summary>
    public List<ExtractedImage> StoredImages { get; set; } = new();

    /// <summary>
    /// Whether any images were processed.
    /// </summary>
    public bool HasImages => StoredImages.Count > 0;

    /// <summary>
    /// Total number of images extracted.
    /// </summary>
    public int ImageCount => StoredImages.Count;

    /// <summary>
    /// Total size of extracted images in bytes.
    /// </summary>
    public long TotalSizeBytes => StoredImages.Sum(img => img.SizeBytes);

    /// <summary>
    /// Any errors that occurred during processing.
    /// </summary>
    public List<ImageProcessingError> Errors { get; set; } = new();

    /// <summary>
    /// Whether all images were processed successfully.
    /// </summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>
    /// Content size reduction from image extraction.
    /// </summary>
    public long ContentSizeReduction => OriginalContent.Length - ProcessedContent.Length;
}

/// <summary>
/// Error that occurred during image processing.
/// </summary>
public class ImageProcessingError
{
    /// <summary>
    /// Index of the image that failed.
    /// </summary>
    public int ImageIndex { get; set; }

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Exception if available.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Position in content where the image was found.
    /// </summary>
    public int ContentPosition { get; set; }
}
