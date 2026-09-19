using AwesomeAssertions;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The builder registers a hybrid search service, so <c>Retriever.SearchAsync(query, options)</c> runs hybrid by
/// default. Two option meanings must survive that: <c>MinSimilarity</c> is a similarity floor, and <c>TopK</c> is how
/// many results come back. Both were lost in the mapping to the hybrid service — the threshold went to the fused
/// score (rank-sized, so any similarity-sized value dropped everything) and each leg fetched its default of 10.
/// </summary>
public class RetrieverHybridContractTests
{
    private static async Task<IFluxIndexContext> BuildAsync(int documents)
    {
        var context = FluxIndexContext.CreateBuilder()
            .UseInMemoryEmbedding()
            .SuppressStartupMessages()
            .Build();
        for (var i = 0; i < documents; i++)
            await context.Indexer.IndexDocumentAsync($"retention schedule number {i} for the archive", $"doc-{i}", cancellationToken: TestContext.Current.CancellationToken);
        return context;
    }

    [Fact]
    public async Task MinSimilarity_UnderTheDefaultHybridSearch_IsASimilarityFloor_NotAFusedScoreFloor()
    {
        var context = await BuildAsync(8);
        var ct = TestContext.Current.CancellationToken;
        // The in-memory embedder gives an identical text similarity 1 and anything else about 0.
        const string query = "retention schedule number 3 for the archive";

        var vector = await context.Retriever.SearchAsync(query, new SearchOptions { MinSimilarity = 0.5f, UseHybridSearch = false }, ct);
        vector.Results.Should().ContainSingle("the control: exactly one document clears the threshold on the vector path");

        var hybrid = await context.Retriever.SearchAsync(query, new SearchOptions { MinSimilarity = 0.5f }, ct);

        hybrid.Metadata["useHybridSearch"].Should().Be(true);
        hybrid.Results.Should().NotBeEmpty("a document clears the similarity threshold, so the threshold must not empty the result");
        hybrid.Results[0].DocumentId.Should().Be("doc-3");
    }

    [Fact]
    public async Task TopK_UnderTheDefaultHybridSearch_IsHowManyResultsComeBack()
    {
        var context = await BuildAsync(30);

        var hybrid = await context.Retriever.SearchAsync("retention schedule archive", new SearchOptions { TopK = 25 }, TestContext.Current.CancellationToken);

        hybrid.Metadata["useHybridSearch"].Should().Be(true);
        hybrid.Results.Should().HaveCount(25, "30 documents match the keyword leg and 25 were asked for");
    }
}
