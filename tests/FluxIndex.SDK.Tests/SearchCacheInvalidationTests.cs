using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The retriever's search-result cache (on by default: <c>CacheOptions.CacheProvider = "Memory"</c>) must not outlive
/// a write made through the indexer. Before 0.52.0 the indexer held no cache at all, so a deleted document came back
/// for the same query for up to <see cref="RetrieverOptions.CacheDuration"/> while the delete reported success, and
/// <see cref="Configuration.CacheOptions.EnableSearchCache"/> was never read.
/// </summary>
public class SearchCacheInvalidationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IFluxIndexContext Build(Action<FluxIndexContextBuilder>? configure = null)
    {
        var builder = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages();
        configure?.Invoke(builder);
        return builder.Build();
    }

    // A broad query that returns every stored chunk, so membership does not depend on the test embedder's similarity.
    private static async Task<List<string>> DocumentIdsFor(IFluxIndexContext context, string query)
        => (await context.Retriever.SearchAsync(query, maxResults: 50, minScore: -1f, cancellationToken: Ct))
            .Select(r => r.DocumentChunk.DocumentId).Distinct().ToList();

    [Fact]
    public async Task CacheIsOnByDefault_AWriteThatBypassesTheIndexerIsNotSeen()
    {
        // Positive control: without this, every fact below could pass because nothing was ever cached.
        var context = Build();
        await context.Indexer.IndexDocumentAsync("alpha text about invoices", "doc-a", cancellationToken: Ct);
        (await DocumentIdsFor(context, "invoices")).Should().Contain("doc-a");

        await context.ServiceProvider.GetRequiredService<IVectorStore>().DeleteByDocumentIdAsync("doc-a", Ct);

        (await DocumentIdsFor(context, "invoices")).Should().Contain("doc-a",
            "the same query is answered from the search cache when the store is changed behind the indexer's back");
    }

    [Fact]
    public async Task DeleteThroughTheIndexer_TheSameQueryNoLongerReturnsTheDocument()
    {
        var context = Build();
        await context.Indexer.IndexDocumentAsync("alpha text about invoices", "doc-a", cancellationToken: Ct);
        await context.Indexer.IndexDocumentAsync("beta text about receipts", "doc-b", cancellationToken: Ct);
        (await DocumentIdsFor(context, "invoices")).Should().Contain("doc-a");

        (await context.Indexer.DeleteByDocumentIdAsync("doc-a", Ct)).Should().BeTrue();

        (await DocumentIdsFor(context, "invoices")).Should().NotContain("doc-a",
            "a deleted document must not be served from the search cache");
    }

    [Fact]
    public async Task AddThroughTheIndexer_TheSameQueryReturnsTheNewDocument()
    {
        var context = Build();
        await context.Indexer.IndexDocumentAsync("alpha text about invoices", "doc-a", cancellationToken: Ct);
        (await DocumentIdsFor(context, "invoices")).Should().Equal("doc-a");

        await context.Indexer.IndexDocumentAsync("gamma text about invoices", "doc-c", cancellationToken: Ct);

        (await DocumentIdsFor(context, "invoices")).Should().Contain("doc-c",
            "a document added after the query was cached can belong to its results — per-document invalidation would miss it");
    }

    [Fact]
    public async Task DeleteChunkThroughTheIndexer_TheSameQueryNoLongerReturnsIt()
    {
        var context = Build();
        await context.Indexer.IndexDocumentAsync("alpha text about invoices", "doc-a", cancellationToken: Ct);
        var chunkId = (await context.Retriever.SearchAsync("invoices", maxResults: 50, minScore: -1f, cancellationToken: Ct))
            .Single().DocumentChunk.Id;

        (await context.Indexer.DeleteChunkAsync(chunkId, Ct)).Should().BeTrue();

        (await DocumentIdsFor(context, "invoices")).Should().BeEmpty();
    }

    [Fact]
    public async Task GetDocumentAfterDelete_IsNotServedFromTheDocumentCache()
    {
        var context = Build();
        await context.Indexer.IndexDocumentAsync("alpha text about invoices", "doc-a", cancellationToken: Ct);
        (await context.Retriever.GetDocumentAsync("doc-a", Ct)).Should().NotBeNull();

        await context.Indexer.DeleteByDocumentIdAsync("doc-a", Ct);

        (await context.Retriever.GetDocumentAsync("doc-a", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task EnableSearchCacheFalse_TheRetrieverCachesNothing()
    {
        var context = Build(b => b.Options.Cache.EnableSearchCache = false);
        await context.Indexer.IndexDocumentAsync("alpha text about invoices", "doc-a", cancellationToken: Ct);
        (await DocumentIdsFor(context, "invoices")).Should().Contain("doc-a");

        // The same bypassing write the positive control above shows to be hidden by the cache.
        await context.ServiceProvider.GetRequiredService<IVectorStore>().DeleteByDocumentIdAsync("doc-a", Ct);

        (await DocumentIdsFor(context, "invoices")).Should().NotContain("doc-a",
            "EnableSearchCache = false must turn the search cache off");
    }
}
