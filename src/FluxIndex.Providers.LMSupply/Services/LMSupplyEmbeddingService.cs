using FluxIndex.Core.Application.Services.Base;
using LMSupply;
using LMSupply.Embedder;
using LMSupply.Embedder.Utils;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Adapts LMSupply's <see cref="IEmbeddingModel"/> to FluxIndex's
/// <see cref="FluxIndex.Core.Application.Interfaces.IEmbeddingService"/>
/// using the <see cref="EmbeddingServiceBase"/> template.
/// </summary>
/// <remarks>
/// <para>Uses ONNX runtime for local inference — no API key required.
/// Native batch embedding via <see cref="IEmbeddingModel.EmbedAsync(IReadOnlyList{string}, CancellationToken)"/>.</para>
/// <para>
/// Two construction modes. <see cref="LMSupplyEmbeddingService(IEmbeddingModel)"/> (and
/// <see cref="CreateAsync"/>) wrap an already loaded model. <see cref="LMSupplyEmbeddingService(LMSupplyEmbeddingOptions)"/>
/// loads the model on first use — or at host start with <see cref="LMSupplyServiceOptionsBase.WarmUpOnStart"/> —
/// so building a container never blocks on a download. In that mode the embedding identity (model name and
/// dimension, hence the vector collection's name) is announced <i>before</i> the load from the LMSupply catalog,
/// and verified against the loaded model: a mismatch fails the load instead of silently splitting the index.
/// </para>
/// </remarks>
public sealed class LMSupplyEmbeddingService : EmbeddingServiceBase, IAsyncDisposable, ILazilyLoadedModel
{
    private readonly IEmbeddingModel? _eager;
    private readonly LazyModelHandle<IEmbeddingModel>? _handle;
    private readonly string _configuredModelId;
    private readonly string? _announcedName;
    private readonly int? _announcedDimension;

    /// <summary>
    /// Initializes a new instance wrapping the given, already loaded <paramref name="model"/>.
    /// </summary>
    /// <param name="model">LMSupply embedding model.</param>
    public LMSupplyEmbeddingService(IEmbeddingModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _eager = model;
        _configuredModelId = model.ModelId;
    }

    /// <summary>
    /// Initializes a lazily loading instance: the model is downloaded (when not cached) and loaded on the
    /// first embedding call or <see cref="EnsureLoadedAsync"/>, under <paramref name="options"/>'
    /// progress reporting and timeout.
    /// </summary>
    /// <param name="options">Model id, revision, loader options, progress, timeout and pre-load identity overrides.</param>
    public LMSupplyEmbeddingService(LMSupplyEmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelId);

        _configuredModelId = options.ModelId;
        Revision = options.Revision;

        var (name, dimension) = AnnounceIdentity(options.ModelId);
        _announcedName = options.ModelName ?? name;
        _announcedDimension = options.Dimensions ?? dimension;

        _handle = new LazyModelHandle<IEmbeddingModel>(
            async (progress, ct) =>
            {
                var model = await LocalEmbedder.LoadAsync(options.ModelId, options.Embedder, progress, ct).ConfigureAwait(false);
                VerifyAnnouncedIdentity(model);
                return model;
            },
            options.Progress,
            options.LoadTimeout);
    }

    /// <summary>
    /// Creates an embedding service by loading a local ONNX model now.
    /// </summary>
    /// <param name="modelId">LMSupply catalog alias (e.g., "default", "fast", "large") or model ID.</param>
    /// <param name="revision">
    /// Optional pipeline revision. Raise it when the same model starts producing vectors that are
    /// incomparable with what is already indexed — an upgrade that changes tokenization, pooling or
    /// quantization does exactly that while leaving the model id untouched. See
    /// <see cref="FluxIndex.Core.Domain.ValueObjects.EmbeddingIdentity.Revision"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ready-to-use embedding service.</returns>
    public static async Task<LMSupplyEmbeddingService> CreateAsync(
        string modelId = "default",
        string? revision = null,
        CancellationToken cancellationToken = default)
    {
        var model = await LocalEmbedder.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyEmbeddingService(model) { Revision = revision };
    }

    /// <inheritdoc />
    public bool IsLoaded => _eager is not null || _handle!.IsLoaded;

    /// <inheritdoc />
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_handle is not null)
            await _handle.GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<IEmbeddingModel> GetModelAsync(CancellationToken cancellationToken) =>
        _eager is not null
            ? ValueTask.FromResult(_eager)
            : new ValueTask<IEmbeddingModel>(_handle!.GetAsync(cancellationToken));

    /// <inheritdoc />
    protected override async Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken)
    {
        var model = await GetModelAsync(cancellationToken).ConfigureAwait(false);
        return await model.EmbedAsync(text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Overrides the sequential default to use LMSupply's native batch embedding,
    /// which is more efficient for ONNX inference.
    /// </remarks>
    public override async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var model = await GetModelAsync(cancellationToken).ConfigureAwait(false);
        return await model.EmbedAsync(texts.ToList(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The model is not loaded yet and its dimension is not known up front — it is not an LMSupply
    /// catalog model and <see cref="LMSupplyEmbeddingOptions.Dimensions"/> was not set.
    /// </exception>
    public override int GetEmbeddingDimension() =>
        _eager?.Dimensions
        ?? _handle!.LoadedModel?.Dimensions
        ?? _announcedDimension
        ?? throw new InvalidOperationException(
            $"The embedding dimension of '{_configuredModelId}' is not known before the model is loaded: it is not an LMSupply catalog model, " +
            "so nothing announces it. Either load it first (await EnsureLoadedAsync, or set LMSupplyEmbeddingOptions.WarmUpOnStart = true in a host), " +
            "or set LMSupplyEmbeddingOptions.Dimensions (verified against the loaded model).");

    /// <inheritdoc />
    public override string GetModelName() =>
        _eager?.ModelId
        ?? _handle!.LoadedModel?.ModelId
        ?? _announcedName!;

    /// <inheritdoc />
    protected override string GetProviderName() => "LMSupply";

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        _eager is not null ? _eager.DisposeAsync() : _handle!.DisposeAsync();

    /// <summary>
    /// Derives, without loading anything, the name and dimension the loaded model will report.
    /// Mirrors <see cref="LocalEmbedder.LoadAsync"/>'s naming: a local model file is named by its file
    /// name; everything else keeps the id as given, after the qualifier split and the user-alias rewrite
    /// (the registry path uses that string as the model id, for catalog models and HuggingFace repo ids
    /// alike). The dimension is only known for catalog models — the registry fabricates a placeholder
    /// entry (384) for unknown repo ids and paths, which must not be announced.
    /// </summary>
    internal static (string Name, int? Dimension) AnnounceIdentity(string modelId)
    {
        var (baseId, _) = LMSupplyOptionsBase.SplitQualifier(modelId);
        var registry = EmbedderModelRegistry.Default;

        var name = registry.TryGetUserAliasTarget(baseId, out var aliasTarget) && aliasTarget is not null
            ? aliasTarget
            : baseId;

        if (File.Exists(name) || name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            return (Path.GetFileNameWithoutExtension(name), null);

        int? dimension = registry.TryResolve(modelId, out var info) && info is not null && IsCatalogEntry(info)
            ? info.Dimensions
            : null;

        return (name, dimension);
    }

    // The registry answers every "org/repo" id and local path with a synthesized ModelInfo whose
    // Dimensions is a placeholder; only entries that are actually in the catalog carry a real one.
    private static bool IsCatalogEntry(ModelInfo info) =>
        LocalEmbedder.GetAllModels().Any(catalog => catalog == info);

    private void VerifyAnnouncedIdentity(IEmbeddingModel model)
    {
        if (_announcedName is not null && !string.Equals(model.ModelId, _announcedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The loaded model reports the name '{model.ModelId}' but '{_announcedName}' was announced for '{_configuredModelId}' before the load. " +
                "The embedding identity — and the vector collection named after it — would change once the model is loaded. " +
                $"Set LMSupplyEmbeddingOptions.ModelName = \"{model.ModelId}\" so the announced identity matches.");
        }

        if (_announcedDimension is { } announced && model.Dimensions != announced)
        {
            throw new InvalidOperationException(
                $"The loaded model '{model.ModelId}' has dimension {model.Dimensions} but {announced} was announced before the load. " +
                $"Set LMSupplyEmbeddingOptions.Dimensions = {model.Dimensions} (or remove the override for a catalog model).");
        }
    }
}
