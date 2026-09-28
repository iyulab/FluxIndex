using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>UseMemoryCache(maxCacheSize)</c> stored the size in <c>CacheOptions.MaxCacheSize</c> and nothing read it: the
/// search cache grew without bound. The in-memory cache service now owns a <c>MemoryCache</c> limited to that many
/// entries.
/// </summary>
public class InMemoryCacheServiceSizeLimitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StoresValues_WithDefaultOptionsAndWithExpiry()
    {
        using var service = new InMemoryCacheService(10, NullLogger<InMemoryCacheService>.Instance);

        await service.SetAsync("a", "1", cancellationToken: Ct);
        await service.SetAsync("b", "2", TimeSpan.FromMinutes(5), Ct);

        (await service.GetAsync<string>("a", Ct)).Should().Be("1");
        (await service.GetAsync<string>("b", Ct)).Should().Be("2");
    }

    [Fact]
    public async Task HoldsNoMoreEntriesThanTheLimit()
    {
        using var service = new InMemoryCacheService(2, NullLogger<InMemoryCacheService>.Instance);

        foreach (var key in new[] { "a", "b", "c", "d" })
            await service.SetAsync(key, key, cancellationToken: Ct);

        var held = 0;
        foreach (var key in new[] { "a", "b", "c", "d" })
            held += await service.ExistsAsync(key, Ct) ? 1 : 0;
        held.Should().BeInRange(1, 2);
    }

    [Fact]
    public async Task UseMemoryCache_BoundsTheCacheTheBuilderRegisters()
    {
        var context = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages()
            .UseMemoryCache(maxCacheSize: 2)
            .Build();
        var cache = context.ServiceProvider.GetRequiredService<ICacheService>();

        foreach (var key in new[] { "a", "b", "c", "d" })
            await cache.SetAsync(key, key, cancellationToken: Ct);

        var held = 0;
        foreach (var key in new[] { "a", "b", "c", "d" })
            held += await cache.ExistsAsync(key, Ct) ? 1 : 0;
        held.Should().BeInRange(1, 2, "UseMemoryCache(2) promised a cache of two entries");
    }

    [Fact]
    public void UseMemoryCache_RejectsANonPositiveSize()
    {
        var act = () => FluxIndexContext.CreateBuilder().UseMemoryCache(maxCacheSize: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
