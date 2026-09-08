using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using LMSupply.Reranker;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Adapts LMSupply's <see cref="IRerankerModel"/> to FluxIndex's <see cref="IReranker"/> using the
/// <see cref="RerankerBase"/> template. Either wraps an already loaded model, or loads it on first use
/// (see <see cref="LMSupplyRerankerService(LMSupplyRerankerOptions, ILogger)"/>).
/// </summary>
public sealed partial class LMSupplyRerankerService : RerankerBase, IAsyncDisposable, ILazilyLoadedModel
{
    private readonly IRerankerModel? _eager;
    private readonly LazyModelHandle<IRerankerModel>? _handle;
    private readonly string _configuredModelId;
    private readonly ILogger _logger;

    /// <summary>Wraps an already loaded <paramref name="model"/>.</summary>
    public LMSupplyRerankerService(IRerankerModel model, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(logger);
        _eager = model;
        _configuredModelId = model.ModelId;
        _logger = logger;
    }

    /// <summary>
    /// Loads the model on the first rerank call (or <see cref="EnsureLoadedAsync"/>) under
    /// <paramref name="options"/>' progress reporting and timeout; the container never blocks on it.
    /// </summary>
    public LMSupplyRerankerService(LMSupplyRerankerOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelId);
        ArgumentNullException.ThrowIfNull(logger);
        _configuredModelId = options.ModelId;
        _logger = logger;
        _handle = new LazyModelHandle<IRerankerModel>(
            (progress, ct) => LocalReranker.LoadAsync(options.ModelId, options.Reranker, progress, ct),
            options.Progress,
            options.LoadTimeout);
    }

    /// <summary>Creates a reranker by loading a local cross-encoder model now.</summary>
    public static async Task<LMSupplyRerankerService> CreateAsync(
        string modelId = "default",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var model = await LocalReranker.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyRerankerService(model, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <inheritdoc />
    public bool IsLoaded => _eager is not null || _handle!.IsLoaded;

    /// <inheritdoc />
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_handle is not null)
            await _handle.GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<IRerankerModel> GetModelAsync(CancellationToken cancellationToken) =>
        _eager is not null
            ? ValueTask.FromResult(_eager)
            : new ValueTask<IRerankerModel>(_handle!.GetAsync(cancellationToken));

    /// <inheritdoc />
    protected override async Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken)
    {
        LogReranking(_logger, query.Length, documents.Count, topN);
        var model = await GetModelAsync(cancellationToken).ConfigureAwait(false);
        var results = await model.RerankAsync(query, documents, topK: topN, cancellationToken: cancellationToken).ConfigureAwait(false);
        return results.Select(r => (r.OriginalIndex, r.Score));
    }

    /// <inheritdoc />
    public override RerankModelInfo GetModelInfo() => new()
    {
        Name = _eager?.ModelId ?? _handle!.LoadedModel?.ModelId ?? _configuredModelId,
        Type = RerankModel.Local,
        RequiresApiKey = false,
    };

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        _eager is not null ? _eager.DisposeAsync() : _handle!.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reranking {DocumentCount} documents for query (length={QueryLength}), topN={TopN}")]
    private static partial void LogReranking(ILogger logger, int queryLength, int documentCount, int topN);
}
