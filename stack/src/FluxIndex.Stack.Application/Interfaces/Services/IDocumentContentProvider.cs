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
}
