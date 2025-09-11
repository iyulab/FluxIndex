using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Interface for two-stage retrieval: Fast bi-encoder recall → Precise cross-encoder reranking
/// </summary>
public interface ITwoStageRetriever
{
    /// <summary>
    /// Perform two-stage search: Stage 1 (recall) → Stage 2 (reranking)
    /// </summary>
    Task<TwoStageResult> SearchAsync(
        string query,
        TwoStageSearchOptions? searchOptions = null,
        CancellationToken cancellationToken = default);
}