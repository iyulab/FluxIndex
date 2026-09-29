using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite.Cache;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The SQLite semantic cache opens a context per operation, so what one call records must be in the
/// database for the next: the schema the initializer created, the entry a set wrote, the hit count and
/// the hit/miss statistics. Runs on the in-memory database a cache with its own path uses.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteSemanticCacheStatisticsTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ISemanticCache _cache;

    public SQLiteSemanticCacheStatisticsTests()
    {
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 1f, 0f, 0f, 0f }));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(embeddings);
        services.AddSQLiteSemanticCache(o =>
        {
            o.CacheDatabasePath = "semantic-cache.db";
            o.UseInMemory = true;
            o.EnableAutoCleanup = false;
        });
        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<SQLiteCacheSchemaInitializer>().InitializeSync(_provider);
        _cache = _provider.GetRequiredService<ISemanticCache>();
    }

    [Fact]
    public async Task HitsMissesAndHitCounts_PersistAcrossOperations()
    {
        var ct = TestContext.Current.CancellationToken;

        (await _cache.GetAsync("what is sqlite", cancellationToken: ct)).Should().BeNull();
        await _cache.SetAsync("what is sqlite", ["an embedded database"], cancellationToken: ct);

        var first = await _cache.GetAsync("what is sqlite", cancellationToken: ct);
        var second = await _cache.GetAsync("what is sqlite", cancellationToken: ct);

        first!.HitCount.Should().Be(1);
        second!.HitCount.Should().Be(2, "the first hit's count was written, not only incremented in memory");

        var stats = await _cache.GetStatisticsAsync(ct);
        stats.CacheMisses.Should().Be(1);
        stats.CacheHits.Should().Be(2, "every hit counts, not only the one that created the statistics row");
        stats.TotalQueries.Should().Be(1);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
