using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// The graph leg of a hybrid search (<see cref="HybridSearchOptions.GraphOptions"/>): off by default and then invisible,
/// fused as a third leg when asked for, scoped like the other two, and refused where it cannot run.
/// </summary>
public class HybridSearchGraphLegTests
{
    private static DocumentChunk Chunk(string id) => new() { Id = id, DocumentId = $"doc-{id}", Content = id };

    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IKeywordSearchService _keyword = Substitute.For<IKeywordSearchService>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();
    private readonly IGraphChunkRetriever _graph = Substitute.For<IGraphChunkRetriever>();

    public HybridSearchGraphLegTests()
    {
        _embedding.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([0.1f, 0.2f]);
        _vectorStore.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { var a = Chunk("a"); a.Score = 0.9f; var b = Chunk("b"); b.Score = 0.5f; return (IEnumerable<DocumentChunk>)[a, b]; });
        _keyword.SearchAsync(Arg.Any<string>(), Arg.Any<KeywordSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<KeywordSearchResult>)[new KeywordSearchResult { Chunk = Chunk("b"), Score = 3.0 }]);
        _graph.RetrieveAsync(Arg.Any<string>(), Arg.Any<GraphRetrievalOptions>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<GraphChunkHit>)[new GraphChunkHit(Chunk("g"), 2.0, 1, ["e1", "e2"], 0), new GraphChunkHit(Chunk("a"), 1.0, 2, ["e1"], 0)]);
    }

    private HybridSearchService Service(bool withGraph = true) => new(
        _vectorStore, _keyword, _embedding, NullLogger<HybridSearchService>.Instance,
        quantizer: null, dynamicFusion: null, graphRetriever: withGraph ? _graph : null);

    private static HybridSearchOptions Rrf(GraphLegOptions? graph = null) => new()
    {
        FusionMethod = FusionMethod.RRF,
        VectorWeight = 0.7,
        SparseWeight = 0.3,
        GraphOptions = graph,
    };

    [Fact]
    public async Task With_the_leg_off_the_graph_is_not_read_and_rows_carry_no_graph_rank()
    {
        var rows = await Service().SearchAsync("q", Rrf(), TestContext.Current.CancellationToken);

        await _graph.DidNotReceive().RetrieveAsync(Arg.Any<string>(), Arg.Any<GraphRetrievalOptions>(), Arg.Any<CancellationToken>());
        Assert.All(rows, r => Assert.Equal(int.MaxValue, r.GraphRank));
        Assert.All(rows, r => Assert.False(r.Source.HasFlag(SearchSource.Graph)));
        Assert.All(rows, r => Assert.Equal(0, r.Fusion.GraphWeight));
    }

    [Fact]
    public async Task With_the_leg_on_a_graph_only_chunk_joins_and_a_shared_one_gains()
    {
        var off = await Service().SearchAsync("q", Rrf(), TestContext.Current.CancellationToken);
        var on = await Service().SearchAsync("q", Rrf(new GraphLegOptions { Weight = 0.3 }), TestContext.Current.CancellationToken);

        var g = Assert.Single(on, r => r.Chunk.Id == "g");
        Assert.Equal(SearchSource.Graph, g.Source);
        Assert.Equal(1, g.GraphRank);
        Assert.Equal(0.3 / 61, g.FusedScore, 9);

        var a = on.Single(r => r.Chunk.Id == "a");
        Assert.Equal(SearchSource.Vector | SearchSource.Graph, a.Source);
        Assert.Equal(off.Single(r => r.Chunk.Id == "a").FusedScore + 0.3 / 62, a.FusedScore, 9);
        Assert.All(on, r => Assert.Equal(0.3, r.Fusion.GraphWeight));
    }

    [Fact]
    public async Task The_leg_takes_the_query_scope_and_fetches_at_least_the_result_count()
    {
        var options = Rrf(new GraphLegOptions { MaxResults = 5, Partition = "tenant-a" });
        options.MaxResults = 12;
        options.Filters = new Dictionary<string, object> { ["tenant"] = "a" };

        await Service().SearchAsync("q", options, TestContext.Current.CancellationToken);

        await _graph.Received(1).RetrieveAsync(
            "q",
            Arg.Is<GraphRetrievalOptions>(o => o.MaxResults == 12 && o.Partition == "tenant-a" && o.Filters != null && Equals(o.Filters["tenant"], "a")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_legs_own_scope_wins_over_the_query_scope()
    {
        var options = Rrf(new GraphLegOptions { Filters = new Dictionary<string, object> { ["tenant"] = "b" } });
        options.Filters = new Dictionary<string, object> { ["tenant"] = "a" };

        await Service().SearchAsync("q", options, TestContext.Current.CancellationToken);

        await _graph.Received(1).RetrieveAsync(
            "q", Arg.Is<GraphRetrievalOptions>(o => Equals(o.Filters!["tenant"], "b")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Asking_for_the_leg_without_a_retriever_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(withGraph: false).SearchAsync("q", Rrf(new GraphLegOptions()), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(FusionMethod.Product)]
    [InlineData(FusionMethod.HarmonicMean)]
    public async Task An_intersecting_method_the_caller_set_is_refused(FusionMethod method)
    {
        var options = Rrf(new GraphLegOptions());
        options.FusionMethod = method;

        await Assert.ThrowsAsync<ArgumentException>(() => Service().SearchAsync("q", options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_intersecting_method_the_service_chose_becomes_RRF()
    {
        var chosen = new AppliedFusion(FusionMethod.Product, 0.6, 0.4, 60, FusionSelection.DynamicAlphaTuning);

        var applied = HybridGraphLeg.Prepare(chosen, new HybridSearchOptions { GraphOptions = new GraphLegOptions { Weight = 0.2 } }, _graph);

        Assert.Equal(FusionMethod.RRF, applied.Method);
        Assert.Equal(FusionSelection.DynamicAlphaTuning, applied.SelectedBy);
        Assert.Equal(0.2, applied.GraphWeight);
    }

    [Fact]
    public async Task A_failing_graph_backend_degrades_the_leg_to_empty()
    {
        _graph.RetrieveAsync(Arg.Any<string>(), Arg.Any<GraphRetrievalOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("graph store down"));

        var rows = await Service().SearchAsync("q", Rrf(new GraphLegOptions()), TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], rows.Select(r => r.Chunk.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_malformed_graph_scope_reaches_the_caller()
    {
        _graph.RetrieveAsync(Arg.Any<string>(), Arg.Any<GraphRetrievalOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ArgumentException("unsupported filter value"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().SearchAsync("q", Rrf(new GraphLegOptions()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancelling_the_search_cancels_rather_than_empties_the_leg()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _graph.RetrieveAsync(Arg.Any<string>(), Arg.Any<GraphRetrievalOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<GraphChunkHit>>>(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().SearchAsync("q", Rrf(new GraphLegOptions()), cts.Token));
    }

    [Fact]
    public async Task A_negative_weight_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Service().SearchAsync("q", Rrf(new GraphLegOptions { Weight = -0.1 }), TestContext.Current.CancellationToken));
    }
}
