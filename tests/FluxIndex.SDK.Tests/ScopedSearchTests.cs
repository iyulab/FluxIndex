using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <see cref="SearchOptions.MetadataFilters"/> takes a collection (match any) through <see cref="Retriever.SearchAsync(string, SearchOptions?, CancellationToken)"/>,
/// and the hybrid path caches its results per scope like the vector path. Before, the SDK filter type was
/// <c>Dictionary&lt;string, string&gt;</c> — a «search within these documents» scope could only be expressed by bypassing
/// the retriever (and its reranker and cache) — and the hybrid path never read or filled the search cache.
/// </summary>
public class ScopedSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IFluxIndexContext> SeededAsync()
    {
        var context = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages().Build();
        await context.Indexer.IndexDocumentAsync("alpha text about invoices", "doc-a", new Dictionary<string, object> { ["desk"] = "d1", ["tag"] = "finance" }, cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("beta text about invoices", "doc-b", new Dictionary<string, object> { ["desk"] = "d1", ["tag"] = "legal" }, cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("gamma text about invoices", "doc-c", new Dictionary<string, object> { ["desk"] = "d2", ["tag"] = "finance" }, cancellationToken: Ct);
        return context;
    }

    private static async Task<List<string>> DocumentIds(IFluxIndexContext context, Dictionary<string, object> filters, bool hybrid)
        => (await context.Retriever.SearchAsync("invoices", new SearchOptions
            {
                TopK = 50,
                MinSimilarity = -1f,
                UseHybridSearch = hybrid,
                MetadataFilters = filters
            }, Ct))
            .Results.Select(r => r.DocumentId).Distinct().Order().ToList();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectionValue_MatchesAnyElement(bool hybrid)
    {
        var context = await SeededAsync();

        (await DocumentIds(context, new() { ["tag"] = new[] { "legal", "absent" } }, hybrid)).Should().Equal("doc-b");
        (await DocumentIds(context, new() { [FilterKeys.DocumentId] = new List<string> { "doc-a", "doc-c" } }, hybrid))
            .Should().Equal("doc-a", "doc-c");
        (await DocumentIds(context, new() { ["desk"] = "d1", ["tag"] = new[] { "finance", "legal" } }, hybrid))
            .Should().Equal("doc-a", "doc-b");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedSearches_AreCachedPerScope_NotShared(bool hybrid)
    {
        var context = await SeededAsync();

        // Cache two scopes, then change the store behind the indexer's back: each scope answers from its own entry.
        (await DocumentIds(context, new() { ["tag"] = new[] { "finance" } }, hybrid)).Should().Equal("doc-a", "doc-c");
        (await DocumentIds(context, new() { ["tag"] = new[] { "legal" } }, hybrid)).Should().Equal("doc-b");

        await context.ServiceProvider.GetRequiredService<IVectorStore>().DeleteByDocumentIdAsync("doc-a", Ct);

        (await DocumentIds(context, new() { ["tag"] = new[] { "finance" } }, hybrid)).Should().Equal(["doc-a", "doc-c"],
            "the scope's results come from the cache (positive control: the search is cached at all)");
        (await DocumentIds(context, new() { ["tag"] = new[] { "legal" } }, hybrid)).Should().Equal(["doc-b"],
            "another scope must not be served the first scope's entry");
        (await DocumentIds(context, new() { ["tag"] = "finance" }, hybrid)).Should().Equal(["doc-a", "doc-c"],
            "a scalar and a one-element collection name the same scope");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWriteThroughTheIndexer_InvalidatesScopedEntries(bool hybrid)
    {
        var context = await SeededAsync();
        (await DocumentIds(context, new() { ["tag"] = new[] { "finance" } }, hybrid)).Should().Equal("doc-a", "doc-c");

        await context.Indexer.IndexDocumentAsync("delta text about invoices", "doc-d", new Dictionary<string, object> { ["desk"] = "d3", ["tag"] = "finance" }, cancellationToken: Ct);

        (await DocumentIds(context, new() { ["tag"] = new[] { "finance" } }, hybrid)).Should().Equal("doc-a", "doc-c", "doc-d");
    }

    [Fact]
    public async Task VectorOverload_TwoCollectionScopesOnOneKey_DoNotShareACacheEntry()
    {
        // The filter overload always took collections, but the cache key printed each value — every List<string> as the
        // same type name — so the second scope was answered with the first scope's cached results.
        var context = await SeededAsync();

        async Task<List<string>> Ids(List<string> tags)
            => (await context.Retriever.SearchAsync("invoices", maxResults: 50, minScore: -1f,
                    filter: new Dictionary<string, object> { ["tag"] = tags }, cancellationToken: Ct))
                .Select(r => r.DocumentChunk.DocumentId).Distinct().Order().ToList();

        (await Ids(["finance"])).Should().Equal("doc-a", "doc-c");
        (await Ids(["legal"])).Should().Equal("doc-b");
    }

    [Fact]
    public async Task AnUnsupportedFilterValue_FailsWhenTheSearchStarts()
    {
        var context = await SeededAsync();

        var act = () => context.Retriever.SearchAsync("invoices", new SearchOptions
        {
            MetadataFilters = { ["tag"] = Array.Empty<string>() }
        }, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
