namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Interface for embedding generation abstraction.
/// Decouples the application layer from specific embedding implementations.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts.
    /// </summary>
    Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the dimension of the embedding vectors.
    /// </summary>
    int EmbeddingDimension { get; }

    /// <summary>
    /// Gets the name of the embedding model being used.
    /// </summary>
    string ModelName { get; }
}
