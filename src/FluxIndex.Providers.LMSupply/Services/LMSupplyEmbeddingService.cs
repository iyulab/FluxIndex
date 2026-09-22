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
    private readonly Func<CancellationToken, Task<string?>>? _preRead;
    private string? _preReadRevision;

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
        : this(options, preRead: null)
    {
    }

    /// <param name="options">See the public constructor.</param>
    /// <param name="preRead">Replaces the files-only read (<c>LocalEmbedder.GetVectorSpaceRevisionAsync</c>) — tests only.</param>
    internal LMSupplyEmbeddingService(LMSupplyEmbeddingOptions options, Func<CancellationToken, Task<string?>>? preRead)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelId);

        _configuredModelId = options.ModelId;
        Revision = options.Revision;
        UseVectorSpaceRevision = options.UseVectorSpaceRevision;

        var (name, dimension) = AnnounceIdentity(options.ModelId);
        _announcedName = options.ModelName ?? name;
        _announcedDimension = options.Dimensions ?? dimension;

        _handle = new LazyModelHandle<IEmbeddingModel>(
            async (progress, ct) =>
            {
                var model = await LocalEmbedder.LoadAsync(options.ModelId, options.Embedder, progress, ct).ConfigureAwait(false);
                VerifyAnnouncedIdentity(model);
                VerifyPreReadRevision(model);
                return model;
            },
            options.Progress,
            options.LoadTimeout);
        _preRead = preRead ?? (ct => LocalEmbedder.GetVectorSpaceRevisionAsync(options.ModelId, options.Embedder, ct));
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
    /// <param name="useVectorSpaceRevision">See <see cref="UseVectorSpaceRevision"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ready-to-use embedding service.</returns>
    public static async Task<LMSupplyEmbeddingService> CreateAsync(
        string modelId = "default",
        string? revision = null,
        bool useVectorSpaceRevision = false,
        CancellationToken cancellationToken = default)
    {
        var model = await LocalEmbedder.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyEmbeddingService(model) { Revision = revision, UseVectorSpaceRevision = useVectorSpaceRevision };
    }

    /// <summary>
    /// Fold the loaded model's <c>VectorSpaceRevision</c> into <see cref="EmbeddingServiceBase.Revision"/> — and so into
    /// the fingerprint — when no revision is set by hand. See <see cref="LMSupplyEmbeddingOptions.UseVectorSpaceRevision"/>
    /// for what that moves. Default: false (the value is reported on <c>EmbeddingIdentity.VectorSpaceRevision</c> only).
    /// </summary>
    public bool UseVectorSpaceRevision { get; init; }

    private IEmbeddingModel? LoadedModel => _eager ?? _handle!.LoadedModel;

    /// <inheritdoc />
    /// <remarks>
    /// The loaded model's value; before the load, the value <see cref="PreReadVectorSpaceRevisionAsync"/> read from the
    /// cached files, if it was called. <c>null</c> otherwise and for a model that computes none — never throws.
    /// </remarks>
    protected override string? GetVectorSpaceRevision() => LoadedModel?.VectorSpaceRevision ?? Volatile.Read(ref _preReadRevision);

    /// <summary>
    /// Reads the vector-space revision from the cached model files, without loading the model (LMSupply 0.72.0
    /// <c>LocalEmbedder.GetVectorSpaceRevisionAsync</c>) — no inference session, no download, no request. Once it has
    /// a value, <see cref="UseVectorSpaceRevision"/> no longer needs the model loaded before the identity is read, so a
    /// lazily loaded service can announce its final identity at start and load on first use.
    /// </summary>
    /// <returns>
    /// The revision, or <c>null</c> when it cannot be known without loading (the model is not cached, is GGUF, or its
    /// dimension is declared nowhere) — then the identity still needs the load, as before. The loaded model's value
    /// once it is loaded.
    /// </returns>
    /// <remarks>
    /// When the load later reports a different value while <see cref="UseVectorSpaceRevision"/> folded the pre-read one
    /// into the identity, the load fails: the collection was already named after the pre-read value, and embedding into
    /// it with a different vector space is the mix this option exists to prevent.
    /// </remarks>
    public async Task<string?> PreReadVectorSpaceRevisionAsync(CancellationToken cancellationToken = default)
    {
        if (LoadedModel is { } loaded)
            return loaded.VectorSpaceRevision;
        if (Volatile.Read(ref _preReadRevision) is not null || _preRead is null)
            return Volatile.Read(ref _preReadRevision);

        var value = await _preRead(cancellationToken).ConfigureAwait(false);
        if (value is not null)
            Interlocked.CompareExchange(ref _preReadRevision, value, null);
        return Volatile.Read(ref _preReadRevision);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// <see cref="UseVectorSpaceRevision"/> is set, no hand revision is, and the model is not loaded yet — the revision is
    /// derived from what the loader did, so an identity read now would name a different collection than the one after
    /// the load.
    /// </exception>
    protected override string? GetRevision()
    {
        if (Revision is not null || !UseVectorSpaceRevision)
            return Revision;

        if (LoadedModel is { } model)
            return model.VectorSpaceRevision;
        if (Volatile.Read(ref _preReadRevision) is { } preRead)
            return preRead;

        throw new InvalidOperationException(
            $"UseVectorSpaceRevision is set for '{_configuredModelId}' but the vector-space revision is not known yet: the model is not loaded and its revision was not read from the cached files. " +
            "Read it first (await PreReadVectorSpaceRevisionAsync — AddLMSupplyEmbedding does this at host start) or load the model (await EnsureLoadedAsync, or LMSupplyEmbeddingOptions.WarmUpOnStart) " +
            "before anything asks for the embedding identity: an identity announced without the revision would name a different collection than the identity after the load.");
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

    /// <summary>
    /// Fails the load when the identity was announced with a pre-read revision the loaded model does not report.
    /// Only when that value was folded into the identity — an informational pre-read changes no collection name.
    /// </summary>
    internal void VerifyPreReadRevision(IEmbeddingModel model)
    {
        if (!UseVectorSpaceRevision || Revision is not null)
            return;
        if (Volatile.Read(ref _preReadRevision) is not { } preRead)
            return;
        if (string.Equals(model.VectorSpaceRevision, preRead, StringComparison.Ordinal))
            return;

        throw new InvalidOperationException(
            $"The loaded model '{model.ModelId}' reports vector-space revision '{model.VectorSpaceRevision ?? "(none)"}' but '{preRead}' was read from its cached files before the load " +
            "and the embedding identity — and the collection named after it — was announced with that value. Embedding now would put a different vector space into that collection. " +
            "Set LMSupplyEmbeddingOptions.WarmUpOnStart = true so the identity is read from the loaded model instead, and report the mismatch to LMSupply (the files-only read and the load disagree).");
    }

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
