using FluxIndex.Stack.Application.Interfaces.Services;
using CoreEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Adapter that bridges Stack's IEmbeddingProvider to Core's IEmbeddingService.
/// This enables Redis semantic cache to use Stack's dynamic embedding provider.
/// </summary>
public sealed class EmbeddingProviderToEmbeddingServiceAdapter : CoreEmbeddingService
{
    private readonly IEmbeddingProvider _provider;

    public EmbeddingProviderToEmbeddingServiceAdapter(IEmbeddingProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await _provider.GetEmbeddingAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var embeddings = await _provider.GetEmbeddingsAsync(textList, cancellationToken);
        return embeddings;
    }

    /// <inheritdoc />
    public int GetEmbeddingDimension()
    {
        return _provider.EmbeddingDimension;
    }

    /// <inheritdoc />
    public string GetModelName()
    {
        return _provider.ModelName;
    }

    /// <inheritdoc />
    public int GetMaxTokens()
    {
        // Most embedding models support around 8192 tokens
        return 8192;
    }

    /// <inheritdoc />
    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        // Rough approximation: 1 token ≈ 4 characters
        return Task.FromResult(text.Length / 4);
    }
}
