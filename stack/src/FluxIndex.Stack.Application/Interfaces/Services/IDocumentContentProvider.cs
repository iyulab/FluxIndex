namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Interface for retrieving document content from storage.
/// Decouples content retrieval from the indexing service.
/// </summary>
public interface IDocumentContentProvider
{
    /// <summary>
    /// Gets the content of a document by its ID.
    /// </summary>
    Task<string> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the raw content bytes of a document by its ID.
    /// </summary>
    Task<byte[]> GetContentBytesAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores content for a document.
    /// </summary>
    Task StoreContentAsync(Guid documentId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores raw content bytes for a document.
    /// </summary>
    Task StoreContentBytesAsync(Guid documentId, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if content exists for a document.
    /// </summary>
    Task<bool> ContentExistsAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes content for a document.
    /// </summary>
    Task DeleteContentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an image for a document.
    /// </summary>
    /// <param name="documentId">The document ID.</param>
    /// <param name="imageId">The image identifier (e.g., "img_001").</param>
    /// <param name="imageData">The image binary data.</param>
    /// <param name="contentType">The MIME type of the image (e.g., "image/png").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreImageAsync(Guid documentId, string imageId, byte[] imageData, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an image for a document.
    /// </summary>
    /// <param name="documentId">The document ID.</param>
    /// <param name="imageId">The image identifier (e.g., "img_001").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (imageData, contentType) or null if not found.</returns>
    Task<(byte[] Data, string ContentType)?> GetImageAsync(Guid documentId, string imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all image IDs for a document.
    /// </summary>
    /// <param name="documentId">The document ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of image IDs.</returns>
    Task<IReadOnlyList<string>> GetImageIdsAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all images for a document.
    /// </summary>
    Task DeleteImagesAsync(Guid documentId, CancellationToken cancellationToken = default);
}
