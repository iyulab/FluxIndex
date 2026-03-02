using FluxIndex.Core.Application.Services.Base;
using LMSupply.Embedder;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Adapts LMSupply's <see cref="IEmbeddingModel"/> to FluxIndex's
/// <see cref="FluxIndex.Core.Application.Interfaces.IEmbeddingService"/>
/// using the <see cref="EmbeddingServiceBase"/> template.
/// </summary>
/// <remarks>
/// Uses ONNX runtime for local inference — no API key required.
/// Native batch embedding via <see cref="IEmbeddingModel.EmbedAsync(IReadOnlyList{string}, CancellationToken)"/>.
/// </remarks>
public sealed class LMSupplyEmbeddingService : EmbeddingServiceBase, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;

    /// <summary>
    /// Initializes a new instance wrapping the given <paramref name="model"/>.
    /// </summary>
    /// <param name="model">LMSupply embedding model.</param>
    public LMSupplyEmbeddingService(IEmbeddingModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    /// <summary>
    /// Creates an embedding service by loading a local ONNX model.
    /// </summary>
    /// <param name="modelId">Model ID (e.g., "all-MiniLM-L6-v2") or "default".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ready-to-use embedding service.</returns>
    public static async Task<LMSupplyEmbeddingService> CreateAsync(
        string modelId = "default",
        CancellationToken cancellationToken = default)
    {
        var model = await LocalEmbedder.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyEmbeddingService(model);
    }

    /// <inheritdoc />
    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
        => await _model.EmbedAsync(text, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Overrides the sequential default to use LMSupply's native batch embedding,
    /// which is more efficient for ONNX inference.
    /// </remarks>
    public override async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
        => await _model.EmbedAsync(texts.ToList(), cancellationToken);

    /// <inheritdoc />
    public override int GetEmbeddingDimension() => _model.Dimensions;

    /// <inheritdoc />
    public override string GetModelName() => _model.ModelId;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _model.DisposeAsync();
}
