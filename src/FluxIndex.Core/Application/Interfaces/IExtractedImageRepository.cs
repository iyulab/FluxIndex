using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Repository for tracking extracted images from documents.
/// </summary>
public interface IExtractedImageRepository
{
    /// <summary>
    /// Gets an extracted image by ID.
    /// </summary>
    /// <param name="id">Image ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted image or null if not found.</returns>
    Task<ExtractedImage?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all extracted images for a document.
    /// </summary>
    /// <param name="documentId">Document ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of extracted images.</returns>
    Task<IEnumerable<ExtractedImage>> GetByDocumentIdAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all extracted images for a chunk.
    /// </summary>
    /// <param name="chunkId">Chunk ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of extracted images.</returns>
    Task<IEnumerable<ExtractedImage>> GetByChunkIdAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an extracted image record.
    /// </summary>
    /// <param name="image">The extracted image to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the added image.</returns>
    Task<string> AddAsync(ExtractedImage image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds multiple extracted image records.
    /// </summary>
    /// <param name="images">The extracted images to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of images added.</returns>
    Task<int> AddRangeAsync(
        IEnumerable<ExtractedImage> images,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an extracted image record.
    /// </summary>
    /// <param name="image">The extracted image to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(ExtractedImage image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an extracted image record.
    /// </summary>
    /// <param name="id">Image ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all extracted images for a document.
    /// </summary>
    /// <param name="documentId">Document ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of images deleted.</returns>
    Task<int> DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all extracted images for a chunk.
    /// </summary>
    /// <param name="chunkId">Chunk ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of images deleted.</returns>
    Task<int> DeleteByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an image with the given content hash exists for a document.
    /// </summary>
    /// <param name="documentId">Document ID.</param>
    /// <param name="contentHash">Content hash of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The existing image or null.</returns>
    Task<ExtractedImage?> FindByHashAsync(
        string documentId,
        string contentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets count of images for a document.
    /// </summary>
    /// <param name="documentId">Document ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of images.</returns>
    Task<int> CountByDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total storage size of images for a document.
    /// </summary>
    /// <param name="documentId">Document ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total size in bytes.</returns>
    Task<long> GetTotalSizeByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets images that are missing descriptions (for LLM processing).
    /// </summary>
    /// <param name="limit">Maximum number to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Images without descriptions.</returns>
    Task<IEnumerable<ExtractedImage>> GetWithoutDescriptionAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);
}
