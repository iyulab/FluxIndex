using FluxIndex.Core.Application.Services.Base;
using LMSupply.Embedder;

namespace FluxIndex.MCP.AI;

/// <summary>
/// Simple LMSupply embedding wrapper for MCP server.
/// </summary>
public sealed class LMSupplyEmbedder : EmbeddingServiceBase, IAsyncDisposable
{
    private readonly IEmbeddingModel _model;

    private LMSupplyEmbedder(IEmbeddingModel model) => _model = model;

    public static async Task<LMSupplyEmbedder> CreateAsync(
        string modelId = "default",
        CancellationToken cancellationToken = default)
    {
        var model = await LocalEmbedder.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyEmbedder(model);
    }

    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
        => await _model.EmbedAsync(text, cancellationToken);

    public override async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
        => await _model.EmbedAsync(texts.ToList(), cancellationToken);

    public override int GetEmbeddingDimension() => _model.Dimensions;
    public override string GetModelName() => _model.ModelId;
    protected override string GetProviderName() => "LMSupply";

    public ValueTask DisposeAsync() => _model.DisposeAsync();
}
