using FluxIndex.Core.Domain.Models;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Retrieves chunks through the persisted entity graph: the entities a query names, the entities around them, and the
/// chunks those entities were extracted from. It reads the <see cref="IGraphStore"/> directly — nothing is loaded into
/// memory per query — and honours the same metadata scope as the vector and keyword legs, so it can serve as a third
/// retrieval leg of a scoped hybrid search or on its own.
/// </summary>
/// <remarks>
/// <para>
/// Seeds are matched by name: every run of one to four words of the query, and every substring of a word written in a
/// script without word spacing or with attached particles (Hangul, CJK), is looked up exactly against the stored
/// <see cref="GraphEntity.NormalizedName"/> in one round trip. «삼성전자의» therefore finds the entity «삼성전자» without a
/// language rule. A match whose every occurrence lies inside a longer match is dropped («전자» inside «삼성전자»).
/// </para>
/// <para>
/// From each seed the graph is walked up to <see cref="GraphRetrievalOptions.MaxHops"/> relationships in both directions.
/// An entity's weight is <c>HopDecay^hops</c> (a seed weighs 1); a chunk's score is the sum of the weights of the entities
/// it mentions, so a chunk that mentions two of the query's entities ranks above one that mentions one.
/// </para>
/// </remarks>
public interface IGraphChunkRetriever
{
    /// <summary>
    /// Returns the chunks the graph connects to the entities <paramref name="query"/> names, best first, within the scope of
    /// <see cref="GraphRetrievalOptions.Filters"/>. An empty list when the query names no stored entity.
    /// </summary>
    /// <exception cref="ArgumentException">A filter value is not supported (the same contract as <see cref="IVectorStore.SearchAsync"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside its range.</exception>
    Task<IReadOnlyList<GraphChunkHit>> RetrieveAsync(
        string query,
        GraphRetrievalOptions options,
        CancellationToken cancellationToken = default);
}
