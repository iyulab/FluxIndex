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
        cache.GetCachedResultAsync(Arg.Any<string>(), Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                asked = true;
                threshold = call.ArgAt<float?>(1);
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
}
