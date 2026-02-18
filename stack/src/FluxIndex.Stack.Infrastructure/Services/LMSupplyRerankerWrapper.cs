using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Base;
using LMSupply.Reranker;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Stack.Infrastructure.Services;

/// <summary>
/// Wrapper that adapts LMSupply IRerankerModel to FluxIndex IReranker (via RerankerBase).
/// Consumer app pattern: directly wraps LMSupply without SDK intermediary.
/// </summary>
internal sealed partial class LMSupplyRerankerWrapper : RerankerBase, IAsyncDisposable
{
    private readonly IRerankerModel _model;
    private readonly ILogger _logger;

    public LMSupplyRerankerWrapper(IRerankerModel model, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _logger = logger;
    }

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

    public override RerankModelInfo GetModelInfo() => new()
    {
        Name = _model.ModelId,
        Type = RerankModel.Local,
        RequiresApiKey = false,
    };

    public async ValueTask DisposeAsync()
    {
        await _model.DisposeAsync();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reranking {DocumentCount} documents for query (length={QueryLength}), topN={TopN}")]
    private static partial void LogReranking(ILogger logger, int queryLength, int documentCount, int topN);
}
