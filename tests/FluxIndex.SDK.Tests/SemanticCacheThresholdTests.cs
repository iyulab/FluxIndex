using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>SemanticCacheOptions.SimilarityThreshold</c> reaches the semantic cache <c>FluxIndexContext.SearchAsync</c>
/// consults. Before it was read, the context asked every cache for 0.95 whatever the option said; unset, it now asks
/// for nothing and the cache's own configured threshold decides.
/// </summary>
public class SemanticCacheThresholdTests
{
    private static async Task<float?> ThresholdTheSearchAsksFor(Action<Configuration.SemanticCacheOptions> configure)
    {
        var asked = false;
        float? threshold = null;
        var cache = Substitute.For<ISemanticCacheService>();
        cache.GetCachedResultAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                asked = true;
                threshold = call.ArgAt<float?>(2);
                return Task.FromResult<CachedSearchResult?>(null);
            });

        using var context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .AddSQLiteStorage()
            .UseInMemoryEmbedding()
            .ConfigureServices(s => s.AddSingleton(cache))
            // Provider stays "None" (the default): the cache registered through ConfigureServices is the one consulted.
            .WithSemanticCacheOptions(configure)
            .Build();

        await context.SearchAsync("what is fluxindex", cancellationToken: TestContext.Current.CancellationToken);

        asked.Should().BeTrue("the search must consult the registered semantic cache");
        return threshold;
    }

    [Fact]
    public async Task SearchAsync_AsksTheCacheForTheConfiguredThreshold()
        => (await ThresholdTheSearchAsksFor(o => o.SimilarityThreshold = 0.5f)).Should().Be(0.5f);

    [Fact]
    public async Task SearchAsync_WithTheThresholdUnset_LetsTheCacheDecide()
        => (await ThresholdTheSearchAsksFor(_ => { })).Should().BeNull();

    [Fact]
    public async Task AFilteredSearch_IsLookedUpAndStoredUnderItsScope()
    {
        // A filtered search used to bypass the semantic cache; it now reads and fills its own scope's entries.
        var lookedUp = new List<string>();
        var stored = new List<string>();
        var cache = Substitute.For<ISemanticCacheService>();
        cache.GetCachedResultAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lookedUp.Add(call.ArgAt<string>(1));
                return Task.FromResult<CachedSearchResult?>(null);
            });
        cache.SetCachedResultAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Core.Domain.Models.CacheDocumentChunk>>(),
                Arg.Any<SearchMetadata?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                stored.Add(call.ArgAt<string>(1));
                return Task.CompletedTask;
            });

        using var context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .AddSQLiteStorage()
            .UseInMemoryEmbedding()
            .ConfigureServices(s => s.AddSingleton(cache))
            .Build();
        var ct = TestContext.Current.CancellationToken;
        await context.Indexer.IndexDocumentAsync("fluxindex is a search library", "doc-1",
            new Dictionary<string, object> { ["tenant"] = "a" }, cancellationToken: ct);

        var filter = new Dictionary<string, object> { ["tenant"] = new[] { "a" } };
        await context.SearchAsync("what is fluxindex", minScore: -1f, filter: filter, cancellationToken: ct);
        await context.SearchAsync("what is fluxindex", minScore: -1f, cancellationToken: ct);

        lookedUp.Should().Equal(FilterScope.Of(filter), FilterScope.Unscoped);
        stored.Should().Equal(FilterScope.Of(filter), FilterScope.Unscoped);
    }
}
