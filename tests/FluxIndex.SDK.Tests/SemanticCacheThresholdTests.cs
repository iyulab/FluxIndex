using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>SemanticCacheOptions.SimilarityThreshold</c> reaches the semantic cache <c>FluxIndexContext.SearchAsync</c>
/// consults. Before it was read, the context asked every cache for 0.95 whatever the option said.
/// </summary>
public class SemanticCacheThresholdTests
{
    private static async Task<float> ThresholdTheSearchAsksFor(Action<Configuration.SemanticCacheOptions> configure)
    {
        float? asked = null;
        var cache = Substitute.For<ISemanticCacheService>();
        cache.GetCachedResultAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                asked = call.ArgAt<float>(1);
                return Task.FromResult<CachedSearchResult?>(new CachedSearchResult { OriginalQuery = call.ArgAt<string>(0) });
            });

        using var context = (FluxIndexContext)FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .AddSQLiteStorage()
            .ConfigureServices(s => s.AddSingleton(cache))
            .WithSemanticCacheOptions(configure)
            .Build();

        await context.SearchAsync("what is fluxindex", cancellationToken: TestContext.Current.CancellationToken);

        asked.Should().NotBeNull("the search must consult the registered semantic cache");
        return asked!.Value;
    }

    [Fact]
    public async Task SearchAsync_AsksTheCacheForTheConfiguredThreshold()
        => (await ThresholdTheSearchAsksFor(o => o.SimilarityThreshold = 0.5f)).Should().Be(0.5f);

    [Fact]
    public async Task SearchAsync_WithTheThresholdUnset_KeepsAsking095()
        => (await ThresholdTheSearchAsksFor(_ => { })).Should().Be(0.95f);
}
