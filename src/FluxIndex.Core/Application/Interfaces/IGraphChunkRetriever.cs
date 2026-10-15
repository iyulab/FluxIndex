using FluxIndex.Core.Domain.Entities;

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

/// <summary>
/// Options of <see cref="IGraphChunkRetriever.RetrieveAsync"/>.
/// </summary>
public record GraphRetrievalOptions
{
    /// <summary>The graph partition to read (<see cref="GraphEntity.Partition"/>). <see cref="GraphPartition.Default"/> reads the default partition, not every partition.</summary>
    public string Partition { get; init; } = GraphPartition.Default;

    /// <summary>At most this many matched entities seed the walk — longer names first, then the most mentioned. Default 8.</summary>
    public int MaxSeedEntities { get; init; } = 8;

    /// <summary>Relationships walked from each seed. 0 uses the seeds' own chunks only. Default 1.</summary>
    public int MaxHops { get; init; } = 1;

    /// <summary>At most this many entities are visited from one seed (<see cref="GraphStoreTraversalOptions.MaxNodes"/>). Default 50.</summary>
    public int MaxNodesPerSeed { get; init; } = 50;

    /// <summary>Weight multiplier per relationship walked: an entity <c>n</c> hops from a seed weighs <c>HopDecay^n</c>. Between 0 and 1; default 0.5.</summary>
    public double HopDecay { get; init; } = 0.5;

    /// <summary>Chunks returned. Default 20.</summary>
    public int MaxResults { get; init; } = 20;

    /// <summary>
    /// Metadata scope, with the vector store's filter semantics: keys AND-combine, a collection value matches any of its
    /// elements, <see cref="FilterKeys.DocumentId"/> scopes by document. A document scope also cuts entities by their
    /// <see cref="GraphEntity.DocumentIds"/> before any chunk is fetched. Null or empty = no scope.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Filters { get; init; }
}

/// <summary>
/// One chunk <see cref="IGraphChunkRetriever"/> reached through the graph.
/// </summary>
/// <param name="Chunk">The chunk, as the vector store holds it.</param>
/// <param name="Score">Sum of the weights of the entities this chunk mentions (see <see cref="IGraphChunkRetriever"/>).</param>
/// <param name="Rank">1-based position in the result.</param>
/// <param name="SeedEntityIds">The seeds whose walks reached this chunk.</param>
/// <param name="Hops">The fewest relationships between a seed and an entity of this chunk (0 = the chunk mentions a seed).</param>
public sealed record GraphChunkHit(
    DocumentChunk Chunk,
    double Score,
    int Rank,
    IReadOnlyList<string> SeedEntityIds,
    int Hops);
