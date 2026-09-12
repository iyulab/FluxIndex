using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The chunk-scoped entity lookup is what a loaded index (and every document- or tenant-scoped read)
/// is built on. It must be exact regardless of how many entities the graph holds: a lookup that only
/// inspects the first N rows returns a silently shorter scope once the graph outgrows N, and a lookup
/// that matches by substring returns entities of other chunks whose ids merely contain the queried id.
/// </summary>
public sealed class SQLiteEntityGraphStoreScopedLookupTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SQLiteEntityGraphOptions _options = new();
    private readonly SQLiteEntityGraphStore _store;

    public SQLiteEntityGraphStoreScopedLookupTests()
    {
        _connection.Open();
        var options = Options.Create(_options);
        var context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
            options);
        context.Database.EnsureCreated();
        _store = new SQLiteEntityGraphStore(context, options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }

    private static GraphEntity Entity(int index, params string[] chunkIds) => new()
    {
        Id = $"entity-{index:D5}",
        Name = $"Entity {index}",
        NormalizedName = $"entity {index}",
        ChunkIds = chunkIds
    };

    [Fact]
    public async Task ScopedLookup_FindsAnEntityBeyondAnyPageWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var beyondWindow = _options.DefaultPageSize * 10 + 1;

        // Every entity but the last belongs to some other chunk; only the last is in scope.
        var entities = Enumerable.Range(1, beyondWindow - 1).Select(i => Entity(i, $"other-{i}")).ToList();
        entities.Add(Entity(beyondWindow, "target"));
        await _store.StoreEntitiesBatchAsync(entities, ct);

        var found = await _store.GetEntitiesByChunkIdsAsync(["target"], ct);

        Assert.Equal([$"entity-{beyondWindow:D5}"], found.Select(e => e.Id));
    }

    [Fact]
    public async Task ScopedLookup_MatchesWholeChunkIds_NotSubstrings()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.StoreEntitiesBatchAsync([Entity(1, "c10"), Entity(2, "c1")], ct);

        var found = await _store.GetEntitiesByChunkIdsAsync(["c1"], ct);

        Assert.Equal(["entity-00002"], found.Select(e => e.Id));
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
