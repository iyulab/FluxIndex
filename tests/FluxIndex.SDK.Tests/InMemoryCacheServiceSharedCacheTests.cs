using AwesomeAssertions;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// The injected <see cref="IMemoryCache"/> is the host's shared instance, and a host is entitled to put a
/// <see cref="MemoryCacheOptions.SizeLimit"/> on it. A size-limited cache throws on <c>Set</c> for an entry
/// that declares no <c>Size</c>, so every entry this service writes has to declare one - both the default
/// options and the per-call expiry path.
/// </summary>
public class InMemoryCacheServiceSharedCacheTests
{
    private static InMemoryCacheService Create(IMemoryCache cache) =>
        new(cache, NullLogger<InMemoryCacheService>.Instance);

    [Fact]
    public async Task SetAsync_OnASizeLimitedHostCache_StoresTheValue_DefaultOptions()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = Create(cache);

        await service.SetAsync("k", "v", cancellationToken: TestContext.Current.CancellationToken);

        (await service.GetAsync<string>("k", TestContext.Current.CancellationToken)).Should().Be("v");
    }

    [Fact]
    public async Task SetAsync_OnASizeLimitedHostCache_StoresTheValue_WithExpiry()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var service = Create(cache);

        await service.SetAsync("k", "v", TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        (await service.GetAsync<string>("k", TestContext.Current.CancellationToken)).Should().Be("v");
    }

    [Fact]
    public void FixturePremise_ASizeLimitedCache_RejectsAnEntryWithoutSize()
    {
        // Without this the two facts above could pass on a cache that never enforced anything.
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });

        var set = () => cache.Set("k", "v", new MemoryCacheEntryOptions());

        set.Should().Throw<InvalidOperationException>();
    }
}
