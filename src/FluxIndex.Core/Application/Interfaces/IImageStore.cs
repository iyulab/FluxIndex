using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Interface for storing and retrieving extracted images from documents.
/// Implementations can use local filesystem, cloud storage (S3, Azure Blob), etc.
/// </summary>
public interface IImageStore
{
    /// <summary>
    /// Stores an image and returns the storage path/key.
    /// </summary>
    /// <param name="documentId">ID of the document the image belongs to.</param>
    /// <param name="imageData">Raw image bytes.</param>
    /// <param name="mimeType">MIME type of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Storage path or key where the image was stored.</returns>
    Task<string> StoreAsync(
        string documentId,
        byte[] imageData,
        string mimeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an image with a specific ID.
    /// </summary>
    /// <param name="imageId">Unique ID for the image.</param>
    /// <param name="documentId">ID of the document the image belongs to.</param>
    /// <param name="imageData">Raw image bytes.</param>
    /// <param name="mimeType">MIME type of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Storage path or key where the image was stored.</returns>
    Task<string> StoreAsync(
        string imageId,
        string documentId,
        byte[] imageData,
        string mimeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves image data by storage path.
    /// </summary>
    /// <param name="storagePath">Storage path or key of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Image data if found, null otherwise.</returns>
    Task<ImageData?> GetAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves image data by image ID.
    /// </summary>
    /// <param name="imageId">Unique ID of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Image data if found, null otherwise.</returns>
    Task<ImageData?> GetByIdAsync(string imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an image by storage path.
    /// </summary>
    /// <param name="storagePath">Storage path or key of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all images associated with a document.
    /// </summary>
    /// <param name="documentId">ID of the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of images deleted.</returns>
    Task<int> DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all image storage paths for a document.
    /// </summary>
    /// <param name="documentId">ID of the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of storage paths.</returns>
    Task<IEnumerable<string>> ListByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an image exists at the given storage path.
    /// </summary>
    /// <param name="storagePath">Storage path or key of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if exists, false otherwise.</returns>
    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the public URL for an image (if supported by the storage implementation).
    /// </summary>
    /// <param name="storagePath">Storage path or key of the image.</param>
    /// <returns>Public URL or null if not supported.</returns>
    string? GetPublicUrl(string storagePath);

    /// <summary>
    /// Gets total storage size for a document's images in bytes.
    /// </summary>
    /// <param name="documentId">ID of the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total size in bytes.</returns>
    Task<long> GetStorageSizeAsync(string documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents image data retrieved from storage.
/// </summary>
public class ImageData
{
    /// <summary>
    /// Raw image bytes.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// MIME type of the image.
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// File extension.
    /// </summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long SizeBytes => Data.Length;

    /// <summary>
    /// Storage path or key.
    /// </summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// When the image was stored.
    /// </summary>
    public DateTime? StoredAt { get; set; }
}
