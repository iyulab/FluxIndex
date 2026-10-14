using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Storage.PostgreSQL.Cache;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// The PostgreSQL semantic cache opens a context per operation, so what one call records must be in the database
/// for the next: the entry a set wrote, the hit count, and the hit/miss statistics (an atomic upsert on the
/// statistics row). Same contract as the SQLite cache's statistics tests, against a real server.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresSemanticCacheStatisticsTests : IAsyncLifetime
{
    private const int Dimensions = 4;

    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private ServiceProvider _provider = null!;
    private ISemanticCacheService _cache = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 1f, 0f, 0f, 0f }));
        // The cache embeds lookups in the query role (FluxIndex 0.68.0); this symmetric double answers both alike.
        embeddings.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 1f, 0f, 0f, 0f }));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(embeddings);
        services.AddPostgreSQLSemanticCache(o =>
        {
            o.ConnectionString = _container.GetConnectionString();
            o.EmbeddingDimensions = Dimensions;
            o.EnableAutoCleanup = false;
        });
        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<PostgresCacheSchemaInitializer>().InitializeSync(_provider);
        _cache = _provider.GetRequiredService<ISemanticCacheService>();
    }

    private static CacheDocumentChunk[] Answer(string content) =>
        [new CacheDocumentChunk { Id = "c1", DocumentId = "d1", Content = content }];

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task HitsMissesAndHitCounts_PersistAcrossOperations()
    {
        var ct = TestContext.Current.CancellationToken;

        (await _cache.GetCachedResultAsync("what is postgres", FilterScope.Unscoped, cancellationToken: ct)).Should().BeNull();
        await _cache.SetCachedResultAsync("what is postgres", FilterScope.Unscoped, Answer("a relational database"), cancellationToken: ct);

        var first = await _cache.GetCachedResultAsync("what is postgres", FilterScope.Unscoped, cancellationToken: ct);
        var second = await _cache.GetCachedResultAsync("what is postgres", FilterScope.Unscoped, cancellationToken: ct);

        first!.HitCount.Should().Be(1);
        second!.HitCount.Should().Be(2, "the first hit's count was written, not only incremented in memory");

        var stats = await _cache.GetCacheStatisticsAsync(ct);
        stats.CacheMisses.Should().Be(1);
        stats.CacheHits.Should().Be(2, "every hit counts, not only the one that created the statistics row");
    }

    [Fact]
    public async Task ATableCreatedWithTimestampWithoutTimeZone_IsConvertedAndHitsWork()
    {
        var ct = TestContext.Current.CancellationToken;

        // The shape earlier releases created: timestamp columns without a time zone, which read back as
        // DateTimeKind.Unspecified and made every hit fail when it saved its count.
        await using (var connection = new Npgsql.NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = new Npgsql.NpgsqlCommand(
                """
                ALTER TABLE semantic_cache ALTER COLUMN "CreatedAt" TYPE timestamp,
                                           ALTER COLUMN "ExpiresAt" TYPE timestamp,
                                           ALTER COLUMN "LastAccessedAt" TYPE timestamp;
                ALTER TABLE cache_stats ALTER COLUMN "LastUpdated" TYPE timestamp;
                """, connection);
            await command.ExecuteNonQueryAsync(ct);
        }

        _provider.GetRequiredService<PostgresCacheSchemaInitializer>().InitializeSync(_provider);
        await _cache.SetCachedResultAsync("legacy table", FilterScope.Unscoped, Answer("still served"), cancellationToken: ct);

        (await _cache.GetCachedResultAsync("legacy table", FilterScope.Unscoped, cancellationToken: ct))!.HitCount.Should().Be(1);
    }

    [Fact]
    public async Task TheSameQuestionInTwoScopes_IsAnsweredFromItsOwnScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantA = FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "a" });
        var tenantB = FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "b" });

        await _cache.SetCachedResultAsync("what is postgres", tenantA, Answer("tenant a"), cancellationToken: ct);
        await _cache.SetCachedResultAsync("what is postgres", tenantB, Answer("tenant b"), cancellationToken: ct);

        (await _cache.GetCachedResultAsync("what is postgres", tenantA, cancellationToken: ct))!.Results.Single().Content.Should().Be("tenant a");
        (await _cache.GetCachedResultAsync("what is postgres", tenantB, cancellationToken: ct))!.Results.Single().Content.Should().Be("tenant b");
        (await _cache.GetCachedResultAsync("what is postgres", FilterScope.Unscoped, cancellationToken: ct)).Should().BeNull(
            "an unscoped search is a scope of its own");
    }

    [Fact]
    public async Task ATableCreatedBeforeScopes_GainsTheColumn_AndItsRowsStayUnscopedHits()
    {
        var ct = TestContext.Current.CancellationToken;
        await _cache.SetCachedResultAsync("legacy question", FilterScope.Unscoped, Answer("legacy answer"), cancellationToken: ct);

        // Back to the shape earlier releases created: no Scope column.
        await using (var connection = new Npgsql.NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = new Npgsql.NpgsqlCommand(
                """
                DROP INDEX IF EXISTS idx_semantic_cache_scope;
                ALTER TABLE semantic_cache DROP COLUMN "Scope";
                """, connection);
            await command.ExecuteNonQueryAsync(ct);
        }

        _provider.GetRequiredService<PostgresCacheSchemaInitializer>().InitializeSync(_provider);

        (await _cache.GetCachedResultAsync("legacy question", FilterScope.Unscoped, cancellationToken: ct))!
            .Results.Single().Content.Should().Be("legacy answer", "a row written before scopes is an unscoped entry");
        (await _cache.GetCachedResultAsync("legacy question", FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "a" }), cancellationToken: ct))
            .Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentLookups_CountEveryHitAndMiss()
    {
        var ct = TestContext.Current.CancellationToken;
        await _cache.SetCachedResultAsync("cached question", FilterScope.Unscoped, Answer("cached answer"), cancellationToken: ct);

        // 16 overlapping callers on one cache instance, half hitting and half missing (the miss path embeds a
        // different vector so the similarity search finds nothing).
        await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(
            () => _cache.GetCachedResultAsync("cached question", FilterScope.Unscoped, cancellationToken: ct), ct)));

        (await _cache.GetCacheStatisticsAsync(ct)).CacheHits.Should().Be(16,
            "the statistics row is updated atomically, so no concurrent increment is lost");
    }
}
