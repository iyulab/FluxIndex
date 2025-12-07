using FluxIndex.SDK.Interfaces;
using FluxIndex.Stack.Application.Interfaces.Services;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// IEmbeddingProvider implementation using FluxIndex SDK embedding service.
/// </summary>
public class FluxIndexEmbeddingProvider : IEmbeddingProvider
{
    private readonly FluxIndex.SDK.Interfaces.IEmbeddingService _embeddingService;

    public FluxIndexEmbeddingProvider(FluxIndex.SDK.Interfaces.IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    public int EmbeddingDimension => _embeddingService.GetEmbeddingDimension();

    public string ModelName => _embeddingService.GetModelInfo().ModelName;

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await _embeddingService.GenerateEmbeddingAsync(text, cancellationToken);
    }

    public async Task<float[][]> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var embeddings = await _embeddingService.GenerateEmbeddingsBatchAsync(texts, cancellationToken);
        return embeddings.ToArray();
    }
}
