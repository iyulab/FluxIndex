using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// The graph leg of a hybrid search (<see cref="HybridSearchOptions.GraphOptions"/>), shared by every
/// <see cref="IHybridSearchService"/> so the leg behaves the same whichever service is registered — the same reason
/// <see cref="HybridFusion"/> is shared.
/// </summary>
public static partial class HybridGraphLeg
{
    /// <summary>
    /// Fits the fusion a service resolved to the graph leg. With the leg off, returns <paramref name="fusion"/> unchanged.
    /// With it on: a fusion method the caller set that cannot fuse a graph leg (<see cref="HybridFusion.AcceptsGraphLeg"/>)
    /// throws; one the service chose is replaced by RRF; the leg's weight is stamped on the result.
    /// </summary>
    /// <exception cref="InvalidOperationException">The graph leg is asked for and <paramref name="retriever"/> is null.</exception>
    /// <exception cref="ArgumentException">The caller set Product or HarmonicMean together with the graph leg.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="GraphLegOptions.Weight"/> is negative or not a number.</exception>
    public static AppliedFusion Prepare(AppliedFusion fusion, HybridSearchOptions options, IGraphChunkRetriever? retriever)
    {
        ArgumentNullException.ThrowIfNull(fusion);
        ArgumentNullException.ThrowIfNull(options);

        if (options.GraphOptions is not { } graph)
            return fusion;

        // Asked for and unavailable: running the two text legs alone would answer under an option that said otherwise.
        if (retriever is null)
            throw new InvalidOperationException(
                "HybridSearchOptions.GraphOptions is set, but this hybrid search service has no IGraphChunkRetriever. " +
                "Register a graph store (AddSQLiteEntityGraphStore, AddPostgresEntityGraph or AddNeo4jGraphStore register it), " +
                "or call AddGraphChunkRetrieval() beside your own IGraphStore.");

        if (graph.Weight < 0 || double.IsNaN(graph.Weight))
            throw new ArgumentOutOfRangeException(nameof(options), graph.Weight, "GraphLegOptions.Weight must not be negative.");

        if (!HybridFusion.AcceptsGraphLeg(fusion.Method))
        {
            if (options.FusionMethod is not null)
                throw new ArgumentException(
                    $"HybridSearchOptions.FusionMethod is {fusion.Method}, which keeps only rows both text legs found and cannot fuse the graph leg " +
                    "(HybridSearchOptions.GraphOptions). Use RRF, WeightedSum, RelativeScoreFusion or Maximum, or leave FusionMethod unset.",
                    nameof(options));
            fusion = fusion with { Method = FusionMethod.RRF };
        }

        return fusion with { GraphWeight = graph.Weight };
    }

    /// <summary>
    /// Runs the graph leg: the retriever under the leg's options, scoped by <see cref="HybridSearchOptions.EffectiveGraphFilters"/>,
    /// fetching <c>max(leg MaxResults, search MaxResults)</c>. Empty when the leg is off. A backend failure degrades the leg
    /// to empty, as the other legs do; a malformed scope or option (<see cref="ArgumentException"/>) and cancellation of
    /// <paramref name="cancellationToken"/> reach the caller.
    /// </summary>
    public static async Task<IReadOnlyList<GraphChunkHit>> RunAsync(
        IGraphChunkRetriever? retriever,
        string query,
        HybridSearchOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (options.GraphOptions is not { } graph || retriever is null)
            return [];

        var retrieval = graph with
        {
            Filters = options.EffectiveGraphFilters,
            MaxResults = Math.Max(graph.MaxResults, options.MaxResults),
        };

        try
        {
            return await retriever.RetrieveAsync(query, retrieval, cancellationToken);
        }
        catch (ArgumentException)
        {
            // A malformed scope is the caller's error: degrading would answer a scoped query without its graph leg and
            // report success. Same rule as the keyword leg.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogGraphLegFailed(logger, ex);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Graph leg failed, returning empty results")]
    private static partial void LogGraphLegFailed(ILogger logger, Exception exception);
}
