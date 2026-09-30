using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Judges retrieved content before a search hands it out — where RAG poisoning and indirect prompt injection detection
/// plug in. Opt-in: with none registered, results are returned as retrieved.
/// </summary>
/// <remarks>
/// <para>
/// Every public search path of the SDK applies the registered guard once to what it returns: a blocked item is dropped,
/// an item with a replacement is handed out with that content (on a copy — the stored chunk is not changed), anything
/// else passes through.
/// </para>
/// <para>
/// FluxIndex ships one implementation over FluxGuard in the <c>FluxIndex.Integrations.FluxGuard</c> package
/// (<c>services.AddFluxGuardRetrievalGuard()</c>). The SDK itself depends on no guard library.
/// </para>
/// </remarks>
public interface IRetrievalGuard
{
    /// <summary>
    /// Judges <paramref name="items"/> in one call.
    /// </summary>
    /// <returns>One verdict per item, in the order of <paramref name="items"/>.</returns>
    Task<IReadOnlyList<RetrievalVerdict>> JudgeAsync(
        IReadOnlyList<RetrievedItem> items,
        CancellationToken cancellationToken = default);
}

/// <summary>What a <see cref="IRetrievalGuard"/> sees of one search result.</summary>
/// <param name="Id">The chunk id.</param>
/// <param name="Content">The content the search would hand out.</param>
/// <param name="Source">The document the content comes from.</param>
/// <param name="Score">The result's relevance score.</param>
public sealed record RetrievedItem(string Id, string Content, string Source, double Score);

/// <summary>A <see cref="IRetrievalGuard"/>'s decision about one item. The default keeps it as retrieved.</summary>
/// <param name="Block">Drop the item from the results.</param>
/// <param name="Replacement">Hand the item out with this content instead (ignored when <paramref name="Block"/>).</param>
/// <param name="RiskScore">The guard's risk score, for logging.</param>
public readonly record struct RetrievalVerdict(bool Block, string? Replacement, double RiskScore)
{
    /// <summary>Keep the item as retrieved.</summary>
    public static RetrievalVerdict Keep => default;
}
