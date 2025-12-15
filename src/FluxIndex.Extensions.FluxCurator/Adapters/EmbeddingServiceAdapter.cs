using FluxCurator.Core.Core;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Extensions.FluxCurator.Adapters;

/// <summary>
/// Adapter that bridges FluxIndex's IEmbeddingService to FluxCurator's IEmbedder interface.
/// Enables FluxCurator to use any FluxIndex-registered embedding service for semantic chunking.
/// </summary>
public sealed class EmbeddingServiceAdapter : IEmbedder
{
    private readonly IEmbeddingService _embeddingService;

    public EmbeddingServiceAdapter(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    /// <inheritdoc />
    public int EmbeddingDimension => _embeddingService.GetEmbeddingDimension();

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await _embeddingService.GenerateEmbeddingAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var result = await _embeddingService.GenerateEmbeddingsBatchAsync(texts, cancellationToken);
        return result.ToList();
    }

    /// <inheritdoc />
    public float CalculateSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length)
            throw new ArgumentException("Embedding vectors must have the same dimension");

        // Cosine similarity calculation
        float dotProduct = 0;
        float magnitude1 = 0;
        float magnitude2 = 0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }

        magnitude1 = MathF.Sqrt(magnitude1);
        magnitude2 = MathF.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }
}
