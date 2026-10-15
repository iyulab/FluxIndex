using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Domain.Models;

/// <summary>
/// Options of <see cref="Application.Interfaces.IGraphChunkRetriever.RetrieveAsync"/>.
/// </summary>
public record GraphRetrievalOptions
{
    /// <summary>The graph partition to read (<see cref="Application.Interfaces.GraphEntity.Partition"/>). The default, <see cref="Application.Interfaces.GraphPartition.Default"/> (empty), reads the default partition, not every partition.</summary>
    public string Partition { get; init; } = "";

    /// <summary>At most this many matched entities seed the walk — longer names first, then the most mentioned. Default 8.</summary>
    public int MaxSeedEntities { get; init; } = 8;

    /// <summary>Relationships walked from each seed. 0 uses the seeds' own chunks only. Default 1.</summary>
    public int MaxHops { get; init; } = 1;

    /// <summary>At most this many entities are visited from one seed (<see cref="Application.Interfaces.GraphStoreTraversalOptions.MaxNodes"/>). Default 50.</summary>
    public int MaxNodesPerSeed { get; init; } = 50;

    /// <summary>Weight multiplier per relationship walked: an entity <c>n</c> hops from a seed weighs <c>HopDecay^n</c>. Between 0 and 1; default 0.5.</summary>
    public double HopDecay { get; init; } = 0.5;

    /// <summary>Chunks returned. Default 20.</summary>
    public int MaxResults { get; init; } = 20;

    /// <summary>
    /// Metadata scope, with the vector store's filter semantics: keys AND-combine, a collection value matches any of its
    /// elements, <see cref="Application.Interfaces.FilterKeys.DocumentId"/> scopes by document. A document scope also cuts entities by their
    /// <see cref="Application.Interfaces.GraphEntity.DocumentIds"/> before any chunk is fetched. Null or empty = no scope.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Filters { get; init; }
}

/// <summary>
/// One chunk <see cref="Application.Interfaces.IGraphChunkRetriever"/> reached through the graph.
/// </summary>
/// <param name="Chunk">The chunk, as the vector store holds it.</param>
/// <param name="Score">Sum of the weights of the entities this chunk mentions (see <see cref="Application.Interfaces.IGraphChunkRetriever"/>).</param>
/// <param name="Rank">1-based position in the result.</param>
/// <param name="SeedEntityIds">The seeds whose walks reached this chunk.</param>
/// <param name="Hops">The fewest relationships between a seed and an entity of this chunk (0 = the chunk mentions a seed).</param>
public sealed record GraphChunkHit(
    DocumentChunk Chunk,
    double Score,
    int Rank,
    IReadOnlyList<string> SeedEntityIds,
    int Hops);

/// <summary>
/// The graph leg of a hybrid search (<see cref="HybridSearchOptions.GraphOptions"/>): the
/// <see cref="Application.Interfaces.IGraphChunkRetriever"/> options plus the leg's fusion weight.
/// </summary>
/// <remarks>
/// <see cref="GraphRetrievalOptions.Filters"/> left empty takes the query-level <see cref="HybridSearchOptions.Filters"/>,
/// like the other two legs. The leg fetches <c>max(MaxResults, HybridSearchOptions.MaxResults)</c> chunks.
/// </remarks>
public record GraphLegOptions : GraphRetrievalOptions
{
    /// <summary>
    /// Weight of the graph leg in the fusion. It is added on top of the vector and keyword weights, not renormalised with
    /// them: a search with the leg off scores exactly as before, and with it on a row the graph also found gains up to this
    /// much (RRF: <c>Weight / (k + rank)</c>). <see cref="HybridSearchOptions.MinFusedScore"/> reads that sum. The service
    /// never fills or changes it. Default 0.3.
    /// </summary>
    public double Weight { get; init; } = 0.3;
}
