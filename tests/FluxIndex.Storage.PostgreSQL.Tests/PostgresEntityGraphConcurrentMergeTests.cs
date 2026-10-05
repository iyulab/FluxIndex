using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.PostgreSQL.EntityGraph;
using FluxIndex.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace FluxIndex.Storage.PostgreSQL.Tests;

/// <summary>
/// The PostgreSQL twin of the SQLite concurrent-merge facts: two writes into one stored entity, forced to both read the
/// row before either saves. Here the row version is a <c>timestamptz</c> (microseconds) written from a .NET timestamp
/// (100 ns ticks), so the token's round trip through the column is part of what the facts check.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PostgresEntityGraphConcurrentMergeTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly PostgreSqlContainer _container = PostgreSqlTestContainer.Create();
    private readonly SaveBarrierInterceptor _gate = new();
    private NpgsqlDataSource _dataSource = null!;
    private PostgresEntityGraphStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var graphOptions = Options.Create(new EntityGraphOptions
        {
            ConnectionString = _container.GetConnectionString(),
            EmbeddingDimension = 4
        });
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(graphOptions.Value.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
        var contextOptions = new DbContextOptionsBuilder<EntityGraphDbContext>()
            .UseNpgsql(_dataSource, npgsql => npgsql.UseVector())
            .AddInterceptors(_gate)
            .Options;
        await using (var cmd = _dataSource.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector"))
        {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var context = new EntityGraphDbContext(contextOptions, graphOptions))
        {
            await context.Database.EnsureCreatedAsync();
        }
        _store = new PostgresEntityGraphStore(
            new TestDbContextFactory<EntityGraphDbContext>(() => new EntityGraphDbContext(contextOptions, graphOptions)),
            graphOptions,
            NullLogger<PostgresEntityGraphStore>.Instance);
    }

    private static GraphEntity Entity(string chunkId, string documentId) => new()
    {
        Id = "entity-1",
        Name = "Cloudgate",
        NormalizedName = "cloudgate",
        Type = NamedEntityType.Product,
        SurfaceForms = ["Cloudgate"],
        Confidence = 0.9,
        MentionCount = 1,
        ChunkIds = [chunkId],
        DocumentIds = [documentId]
    };

    [Fact]
    public async Task Two_merges_into_a_stored_entity_at_once_keep_both_writers_chunks()
    {
        await _store.StoreEntityAsync(Entity("c0", "doc-0"), Ct);

        _gate.Arm(2);
        await Task.WhenAll(
            Task.Run(() => _store.StoreEntityAsync(Entity("c1", "doc-1"), Ct), Ct),
            Task.Run(() => _store.StoreEntityAsync(Entity("c2", "doc-2"), Ct), Ct));

        Assert.Equal(2, _gate.Passed);
        var stored = await _store.GetEntityByIdAsync("entity-1", Ct);
        Assert.NotNull(stored);
        Assert.Equal(new[] { "c0", "c1", "c2" }, stored.ChunkIds.Order());
        Assert.Equal(new[] { "doc-0", "doc-1", "doc-2" }, stored.DocumentIds.Order());
    }

    // A row written once and then updated by one writer must not read as a conflict: the token read back from the
    // timestamptz column has to equal the value the next UPDATE compares against.
    [Fact]
    public async Task Sequential_writes_never_conflict_on_the_round_tripped_token()
    {
        await _store.StoreEntityAsync(Entity("c0", "doc-0"), Ct);
        for (var i = 1; i <= 5; i++)
        {
            await _store.StoreEntityAsync(Entity($"c{i}", $"doc-{i}"), Ct);
        }

        Assert.True(await _store.UpdateEntityAsync(Entity("c9", "doc-9"), Ct));
        var stored = await _store.GetEntityByIdAsync("entity-1", Ct);
        Assert.NotNull(stored);
        Assert.Equal(new[] { "c9" }, stored.ChunkIds);
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();
        if (_dataSource is not null) await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
