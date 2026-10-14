using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Storage.SQLite.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The SQLite cache is the <see cref="ISemanticCacheService"/> the search path consults: results round-trip as
/// <see cref="CacheDocumentChunk"/> with plain metadata values, a row it cannot read is a miss (never an exception),
/// a keyword-only context neither looks up nor stores, and the caller's cancellation is not swallowed.
/// </summary>
[Collection("SQLite Tests")]
public sealed class SQLiteSemanticCacheContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider CacheProvider(IEmbeddingService? embeddings = null)
    {
        if (embeddings is null)
        {
            var constant = Substitute.For<IEmbeddingService>();
            constant.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new[] { 1f, 0f, 0f, 0f }));
            embeddings = constant;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(embeddings);
        services.AddSQLiteSemanticCache(o =>
        {
            o.CacheDatabasePath = "semantic-cache-contract.db";
            o.UseInMemory = true;
            o.EnableAutoCleanup = false;
        });
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SQLiteCacheSchemaInitializer>().InitializeSync(provider);
        return provider;
    }

    [Fact]
    public async Task StoredResults_RoundTripAsChunksWithPlainMetadata()
    {
        await using var provider = CacheProvider();
        var cache = provider.GetRequiredService<ISemanticCacheService>();
        var chunk = new CacheDocumentChunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            Content = "an embedded database",
            ChunkIndex = 2,
            Score = 0.75f,
            Metadata = new Dictionary<string, object> { ["source"] = "guide.md", ["page"] = 3, ["draft"] = false }
        };
        var metadata = new SearchMetadata
        {
            SearchAlgorithm = "vector",
            TotalDocuments = 1,
            AdditionalProperties = new Dictionary<string, object> { ["limit"] = 10 }
        };

        await cache.SetCachedResultAsync("what is sqlite", FilterScope.Unscoped, [chunk], metadata, cancellationToken: Ct);
        var hit = await cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: Ct);

        hit.Should().NotBeNull();
        hit!.CachedQuery.Should().Be("what is sqlite");
        var read = hit.Results.Should().ContainSingle().Subject;
        (read.Id, read.DocumentId, read.Content, read.ChunkIndex, read.Score)
            .Should().Be(("chunk-1", "doc-1", "an embedded database", 2, 0.75f));
        read.Metadata.Should().BeEquivalentTo(new Dictionary<string, object> { ["source"] = "guide.md", ["page"] = 3L, ["draft"] = false });
        hit.Metadata!.SearchAlgorithm.Should().Be("vector");
        hit.Metadata.AdditionalProperties["limit"].Should().Be(10L);
    }

    [Fact]
    public async Task ARowItCannotRead_IsAMissAndIsRemoved()
    {
        await using var provider = CacheProvider();
        var cache = provider.GetRequiredService<ISemanticCacheService>();

        // The shape an earlier release wrote for the retired ISemanticCache: a list of arbitrary objects.
        var factory = provider.GetRequiredService<IDbContextFactory<SQLiteCacheDbContext>>();
        await using (var context = await factory.CreateDbContextAsync(Ct))
        {
            var legacy = new SemanticCacheEntity
            {
                Id = "legacy",
                QueryHash = "legacy-hash",
                Query = "what is sqlite",
                ResultsJson = "[\"an embedded database\"]",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            legacy.SetEmbedding([1f, 0f, 0f, 0f]);
            context.SemanticCache.Add(legacy);
            await context.SaveChangesAsync(Ct);
        }

        (await cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: Ct)).Should().BeNull();
        (await cache.GetCacheStatisticsAsync(Ct)).TotalEntries.Should().Be(0, "an unreadable row is removed, not served or kept");

        await cache.SetCachedResultAsync("what is sqlite", FilterScope.Unscoped,
            [new CacheDocumentChunk { Id = "c", DocumentId = "d", Content = "an embedded database" }], cancellationToken: Ct);
        (await cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: Ct)).Should().NotBeNull(
            "positive control: the same query hits once a readable entry is stored");
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("{\"Id\":\"c\"}")]
    [InlineData("[{\"Id\":\"c\",\"Unknown\":1}]")]
    public void ResultsJsonThatIsNotTheStoredForm_IsUnreadable(string json)
        => FluxIndex.Core.Application.Utilities.SemanticCacheJson.TryDeserializeResults(json, out _).Should().BeFalse();

    [Fact]
    public async Task AKeywordOnlyContext_NeitherLooksUpNorStores()
    {
        await using var provider = CacheProvider(NoEmbeddingService.Instance);
        var cache = provider.GetRequiredService<ISemanticCacheService>();

        await cache.SetCachedResultAsync("what is sqlite", FilterScope.Unscoped,
            [new CacheDocumentChunk { Id = "c", DocumentId = "d", Content = "x" }], cancellationToken: Ct);

        (await cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: Ct)).Should().BeNull();
        (await cache.GetCacheStatisticsAsync(Ct)).TotalEntries.Should().Be(0);
    }

    [Fact]
    public async Task ClearCache_RemovesEveryEntry()
    {
        await using var provider = CacheProvider();
        var cache = provider.GetRequiredService<ISemanticCacheService>();
        await cache.SetCachedResultAsync("what is sqlite", FilterScope.Unscoped,
            [new CacheDocumentChunk { Id = "c", DocumentId = "d", Content = "x" }], cancellationToken: Ct);
        (await cache.GetCacheStatisticsAsync(Ct)).TotalEntries.Should().Be(1);

        await cache.ClearCacheAsync(Ct);

        (await cache.GetCacheStatisticsAsync(Ct)).TotalEntries.Should().Be(0);
        (await cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ACancelledLookup_ThrowsRatherThanAnsweringAMiss()
    {
        await using var provider = CacheProvider();
        var cache = provider.GetRequiredService<ISemanticCacheService>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var lookup = () => cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: cancelled.Token);

        await lookup.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TheSameQuestionInTwoScopes_IsAnsweredFromItsOwnScope()
    {
        // A scoped search used to bypass the cache entirely; now the scope is part of the entry, and a lookup never
        // crosses scopes — two tenants asking the same question must not share an answer.
        await using var provider = CacheProvider();
        var cache = provider.GetRequiredService<ISemanticCacheService>();
        var tenantA = FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "a" });
        var tenantB = FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "b" });

        await cache.SetCachedResultAsync("what is sqlite", tenantA, [new CacheDocumentChunk { Id = "a", DocumentId = "doc-a", Content = "tenant a" }], cancellationToken: Ct);
        await cache.SetCachedResultAsync("what is sqlite", tenantB, [new CacheDocumentChunk { Id = "b", DocumentId = "doc-b", Content = "tenant b" }], cancellationToken: Ct);

        (await cache.GetCachedResultAsync("what is sqlite", tenantA, cancellationToken: Ct))!.Results.Single().DocumentId.Should().Be("doc-a");
        (await cache.GetCachedResultAsync("what is sqlite", tenantB, cancellationToken: Ct))!.Results.Single().DocumentId.Should().Be("doc-b");
        (await cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: Ct)).Should().BeNull(
            "an unscoped search is a scope of its own");
        (await cache.GetCacheStatisticsAsync(Ct)).TotalEntries.Should().Be(2);
    }

    [Fact]
    public async Task ACacheTableWrittenBeforeScopes_GainsTheColumn_AndItsRowsStayUnscopedHits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-semcache-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await using (var provider = FileCacheProvider(path))
            {
                var cache = provider.GetRequiredService<ISemanticCacheService>();
                await cache.SetCachedResultAsync("what is sqlite", FilterScope.Unscoped,
                    [new CacheDocumentChunk { Id = "c", DocumentId = "legacy", Content = "x" }], cancellationToken: Ct);

                // Back to the shape an earlier release created: no Scope column (and no index on it).
                await using var context = await provider.GetRequiredService<IDbContextFactory<SQLiteCacheDbContext>>().CreateDbContextAsync(Ct);
                var scopeIndex = await context.Database.SqlQueryRaw<string>(
                    "SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'index' AND tbl_name = 'semantic_cache' AND sql LIKE '%Scope%'").ToListAsync(Ct);
                foreach (var index in scopeIndex)
                    await context.Database.ExecuteSqlRawAsync($"DROP INDEX \"{index}\"", Ct);
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE semantic_cache DROP COLUMN Scope", Ct);
            }

            await using (var provider = FileCacheProvider(path))
            {
                var cache = provider.GetRequiredService<ISemanticCacheService>();
                (await cache.GetCachedResultAsync("what is sqlite", FilterScope.Unscoped, cancellationToken: Ct))!
                    .Results.Single().DocumentId.Should().Be("legacy", "a row written before scopes is an unscoped entry");
                (await cache.GetCachedResultAsync("what is sqlite", FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "a" }), cancellationToken: Ct))
                    .Should().BeNull();
            }
        }
        finally
        {
            // This file's pool only — ClearAllPools would close connections other tests are using in parallel.
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"));
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static ServiceProvider FileCacheProvider(string path)
    {
        var constant = Substitute.For<IEmbeddingService>();
        constant.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 1f, 0f, 0f, 0f }));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(constant);
        services.AddSQLiteSemanticCache(o =>
        {
            o.CacheDatabasePath = path;
            o.UseInMemory = false;
            o.EnableAutoCleanup = false;
        });
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SQLiteCacheSchemaInitializer>().InitializeSync(provider);
        return provider;
    }

    [Fact]
    public void Registration_IsOneSingletonServingBothTypes_AndIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IEmbeddingService>());
        services.AddSQLiteInMemorySemanticCache();
        services.AddSQLiteInMemorySemanticCache();

        services.Count(d => d.ServiceType == typeof(ISemanticCacheService)).Should().Be(1);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISemanticCacheService>().Should()
            .BeSameAs(provider.GetRequiredService<SQLiteSemanticCache>());
    }
}
