using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Graph;
using FluxIndex.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Two writes into one entity that is already stored, made at the same time: each reads the stored row, then both save.
/// The window is forced, not raced — the interceptor holds each write's first save until both have read. Without a
/// concurrency check the later save writes back the row it read and drops the earlier one's new chunk; with it the later
/// save matches no row, reads again and merges.
/// </summary>
public sealed class SQLiteEntityGraphConcurrentMergeTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluxindex-graph-{Guid.NewGuid():N}.db");
    private readonly IOptions<SQLiteEntityGraphOptions> _options = Options.Create(new SQLiteEntityGraphOptions());
    private readonly SaveBarrierInterceptor _gate = new();
    private readonly SQLiteEntityGraphStore _store;

    public SQLiteEntityGraphConcurrentMergeTests()
    {
        using (var context = NewContext())
        {
            context.Database.EnsureCreated();
        }

        _store = new SQLiteEntityGraphStore(
            new DelegateDbContextFactory<SQLiteEntityGraphDbContext>(NewContext), _options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }

    private SQLiteEntityGraphDbContext NewContext() => new(
        new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False")
            .AddInterceptors(_gate)
            .Options,
        _options);

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

    // UpdateEntityAsync replaces every field; losing the race to a merge must retry it, not surface the conflict.
    [Fact]
    public async Task An_update_racing_a_merge_is_retried_and_lands()
    {
        await _store.StoreEntityAsync(Entity("c0", "doc-0"), Ct);

        _gate.Arm(2);
        var results = await Task.WhenAll(
            Task.Run(async () => { await _store.StoreEntityAsync(Entity("c1", "doc-1"), Ct); return true; }, Ct),
            Task.Run(() => _store.UpdateEntityAsync(Entity("c9", "doc-9"), Ct), Ct));

        Assert.Equal(2, _gate.Passed);
        Assert.All(results, Assert.True);
        var stored = await _store.GetEntityByIdAsync("entity-1", Ct);
        Assert.NotNull(stored);
        Assert.Contains("c9", stored.ChunkIds);
    }

    public void Dispose()
    {
        _gate.Dispose();
        // Pooling=False: every connection is closed when its context is, so the file is free to delete.
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}
