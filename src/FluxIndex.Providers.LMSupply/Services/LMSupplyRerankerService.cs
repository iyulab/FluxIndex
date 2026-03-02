using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using LMSupply.Reranker;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Providers.LMSupply.Services;

/// <summary>
/// Adapts LMSupply's <see cref="IRerankerModel"/> to FluxIndex's
/// <see cref="IReranker"/> using the <see cref="RerankerBase"/> template.
/// </summary>
/// <remarks>
/// Uses ONNX runtime for local cross-encoder reranking — no API key required.
/// </remarks>
public sealed partial class LMSupplyRerankerService : RerankerBase, IAsyncDisposable
{
    private readonly IRerankerModel _model;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance wrapping the given <paramref name="model"/>.
    /// </summary>
    /// <param name="model">LMSupply reranker model.</param>
    /// <param name="logger">Logger instance.</param>
    public LMSupplyRerankerService(IRerankerModel model, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(logger);
        _model = model;
        _logger = logger;
    }

    /// <summary>
    /// Creates a reranker service by loading a local ONNX model.
    /// </summary>
    /// <param name="modelId">Model ID (e.g., "ms-marco-MiniLM-L6-v2") or "default".</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ready-to-use reranker service.</returns>
    public static async Task<LMSupplyRerankerService> CreateAsync(
        string modelId = "default",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var model = await LocalReranker.LoadAsync(modelId, cancellationToken: cancellationToken);
        return new LMSupplyRerankerService(model, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <inheritdoc />
    protected override async Task<IEnumerable<(int Index, float Score)>> RerankCoreAsync(
        string query,
        IReadOnlyList<string> documents,
        int topN,
        CancellationToken cancellationToken)
    {
        LogReranking(_logger, query.Length, documents.Count, topN);

        var results = await _model.RerankAsync(query, documents, topK: topN, cancellationToken: cancellationToken);
        return results.Select(r => (r.OriginalIndex, r.Score));
    }

    /// <inheritdoc />
    public override RerankModelInfo GetModelInfo() => new()
    {
        Name = _model.ModelId,
        Type = RerankModel.Local,
        RequiresApiKey = false,
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _model.DisposeAsync();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reranking {DocumentCount} documents for query (length={QueryLength}), topN={TopN}")]
    private static partial void LogReranking(ILogger logger, int queryLength, int documentCount, int topN);
}
