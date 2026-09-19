using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// A registered <see cref="IReranker"/> is used when a search asks for it, and only then. Every test goes through the
/// retriever the builder assembles: a retriever constructed by hand would prove the constructor takes a reranker, not
/// that a registered one reaches it.
/// </summary>
public class RetrieverRerankerTests
{
    private static readonly string[] Documents =
    [
        "retention policy for archived records",
        "retention of build artifacts",
        "retention schedule for backups",
        "retention rules for audit logs",
        "retention window for metrics",
        "retention period for invoices",
    ];

    private static async Task<IFluxIndexContext> BuildAsync(IReranker? reranker)
    {
        var context = FluxIndexContext.CreateBuilder()
            .UseInMemoryEmbedding()
            .SuppressStartupMessages()
            .ConfigureServices(services =>
            {
                if (reranker is not null)
                    services.AddSingleton(reranker);
            })
            .Build();

        for (var i = 0; i < Documents.Length; i++)
            await context.Indexer.IndexDocumentAsync(Documents[i], $"doc-{i}", cancellationToken: TestContext.Current.CancellationToken);
        return context;
    }

    [Fact]
    public async Task UseReranker_ReturnsTheRerankersOrder_CutToTopK()
    {
        var reranker = new ReversingReranker();
        var context = await BuildAsync(reranker);
        var ct = TestContext.Current.CancellationToken;

        var retrieval = await context.Retriever.SearchAsync("retention", new SearchOptions { TopK = 6, UseHybridSearch = false, MinSimilarity = -1f }, ct);
        var reranked = await context.Retriever.SearchAsync("retention", new SearchOptions { TopK = 2, UseReranker = true, UseHybridSearch = false, MinSimilarity = -1f }, ct);

        retrieval.Results.Should().HaveCount(6);
        reranker.Calls.Should().ContainSingle();
        reranker.Calls[0].CandidateCount.Should().Be(6, "retrieval fetches TopK x 3 candidates for the reranker to order");
        reranked.Results.Select(r => r.Id).Should().Equal(
            retrieval.Results.Select(r => r.Id).Reverse().Take(2),
            "the reversing reranker promotes what retrieval ranked last — a result TopK alone would never have fetched");
        reranked.Results.Should().OnlyContain(r => r.RetrievalScore != null && r.Score != r.RetrievalScore);
        reranked.Metadata["reranked"].Should().Be(true);
    }

    [Fact]
    public async Task RerankCandidateCount_SetsHowManyCandidatesRetrievalFetches()
    {
        var reranker = new ReversingReranker();
        var context = await BuildAsync(reranker);

        var response = await context.Retriever.SearchAsync("retention",
            new SearchOptions { TopK = 2, UseReranker = true, RerankCandidateCount = 4, UseHybridSearch = false, MinSimilarity = -1f }, TestContext.Current.CancellationToken);

        reranker.Calls.Should().ContainSingle().Which.CandidateCount.Should().Be(4);
        response.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task ARegisteredReranker_IsNotCalled_UnlessTheSearchAsksForIt()
    {
        var reranker = new ReversingReranker();
        var context = await BuildAsync(reranker);

        var response = await context.Retriever.SearchAsync("retention", new SearchOptions { TopK = 3, UseHybridSearch = false, MinSimilarity = -1f }, TestContext.Current.CancellationToken);

        reranker.Calls.Should().BeEmpty();
        response.Results.Should().OnlyContain(r => r.RetrievalScore == null);
        response.Metadata["reranked"].Should().Be(false);
    }

    [Fact]
    public async Task UseReranker_WithNoRerankerRegistered_Throws_RatherThanReturningUnrerankedResults()
    {
        var context = await BuildAsync(reranker: null);

        var search = () => context.Retriever.SearchAsync("retention", new SearchOptions { UseReranker = true }, TestContext.Current.CancellationToken);

        (await search.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IReranker*AddLMSupplyReranker*");
    }

    /// <summary>Deterministic: ranks the candidates in the reverse of the order retrieval gave them.</summary>
    private sealed class ReversingReranker : IReranker
    {
        public List<(string Query, int CandidateCount, int TopN)> Calls { get; } = [];

        public Task<IEnumerable<RerankResult>> RerankAsync(
            string query,
            IEnumerable<RetrievalCandidate> candidates,
            RerankOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = candidates.ToList();
            Calls.Add((query, list.Count, options?.TopN ?? -1));
            var results = list
                .OrderByDescending(c => c.InitialRank)
                .Select((c, i) => new RerankResult
                {
                    Id = c.Id,
                    DocumentId = c.DocumentId,
                    ChunkId = c.ChunkId,
                    Content = c.Content,
                    InitialScore = c.InitialScore,
                    InitialRank = c.InitialRank,
                    RerankScore = 10f - i,
                    NewRank = i + 1,
                })
                .Take(options?.TopN ?? list.Count);
            return Task.FromResult<IEnumerable<RerankResult>>(results.ToList());
        }

        public RerankModelInfo GetModelInfo() => new();
    }
}
