using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The entity graph build against the real SQLite store, across documents and partitions: an entity another document
/// already stored is joined by identity even though the two documents share no chunk, and never across a partition —
/// two tenants of one store that mention the same organization keep two entities.
/// </summary>
public sealed class SQLiteEntityGraphPartitionMergeTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SQLiteEntityGraphDbContext _context;
    private readonly SQLiteEntityGraphStore _store;
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();

    public SQLiteEntityGraphPartitionMergeTests()
    {
        _connection.Open();
        var options = Options.Create(new SQLiteEntityGraphOptions());
        _context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
            options);
        _context.Database.EnsureCreated();
        _store = new SQLiteEntityGraphStore(_context, options, NullLogger<SQLiteEntityGraphStore>.Instance);

        _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<EntityGraph>>(ci.Arg<IEnumerable<string>>().Select(content => new EntityGraph
            {
                SourceId = content,
                Entities = content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Trim('.', ','))
                    .Where(w => w.Length > 1 && char.IsUpper(w[0]))
                    .Distinct()
                    .Select(w => new ExtractedEntity { Id = Guid.NewGuid().ToString(), Text = w, Type = NamedEntityType.Organization, Confidence = 0.9 })
                    .ToList(),
                Relations = []
            }).ToList()));
    }

    private static DocumentChunk Chunk(string id, string documentId, string content) => new()
    {
        Id = id,
        DocumentId = documentId,
        Content = content,
        ChunkIndex = 0
    };

    private EntityGraphService Service() => new(_extractor, null, _store, NullLogger<EntityGraphService>.Instance);

    [Fact]
    public async Task TwoDocumentsWithNoSharedChunk_InOnePartition_LeaveOneEntity_CarryingBothDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new EntityGraphBuildOptions { Partition = "tenant-1" };

        await Service().BuildEntityGraphAsync([Chunk("a1", "doc-a", "Acme signed a contract.")], options, ct);
        await Service().BuildEntityGraphAsync([Chunk("b1", "doc-b", "Acme opened an office.")], options, ct);

        var acme = Assert.Single(await _store.GetEntitiesByNormalizedNamesAsync(["acme"], "tenant-1", ct));
        Assert.Equal(new[] { "a1", "b1" }, acme.ChunkIds.Order());
        Assert.Equal(new[] { "doc-a", "doc-b" }, acme.DocumentIds.Order());
    }

    [Fact]
    public async Task AStoredEntityJoinedByASecondDocument_IsWrittenBack_ThroughTheRegisteredStore()
    {
        // The store as a consumer registers it (AddSQLiteEntityGraphStore), not a hand-built context: the join writes back
        // an entity the store already holds, which is an update of an existing row, and that update is what the
        // registration's tracking behaviour decides.
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), $"fluxindex-entitygraph-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection()
            .AddLogging()
            .AddSQLiteEntityGraphStore(path);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SQLiteEntityGraphSchemaInitializer>().InitializeSync(provider);

        try
        {
            var options = new EntityGraphBuildOptions { Partition = "tenant-1" };
            async Task BuildInOwnScope(DocumentChunk chunk)
            {
                await using var scope = provider.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IGraphStore>();
                await new EntityGraphService(_extractor, null, store, NullLogger<EntityGraphService>.Instance).BuildEntityGraphAsync([chunk], options, ct);
            }

            await BuildInOwnScope(Chunk("a1", "doc-a", "Acme signed a contract."));
            await BuildInOwnScope(Chunk("b1", "doc-b", "Acme opened an office."));

            await using var read = provider.CreateAsyncScope();
            var reader = read.ServiceProvider.GetRequiredService<IGraphStore>();
            var acme = Assert.Single(await reader.GetEntitiesByNormalizedNamesAsync(["acme"], "tenant-1", ct));
            Assert.Equal(new[] { "a1", "b1" }, acme.ChunkIds.Order());
            Assert.Equal(2, acme.MentionCount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task TheSameEntity_InTwoPartitions_StaysTwoEntities_EachWithItsOwnDocuments()
    {
        var ct = TestContext.Current.CancellationToken;

        await Service().BuildEntityGraphAsync([Chunk("a1", "doc-a", "Acme signed a contract.")], new EntityGraphBuildOptions { Partition = "tenant-1" }, ct);
        await Service().BuildEntityGraphAsync([Chunk("a1", "doc-a", "Acme signed a contract.")], new EntityGraphBuildOptions { Partition = "tenant-2" }, ct);

        var inTenant1 = Assert.Single(await _store.GetEntitiesByChunkIdsAsync(["a1"], "tenant-1", ct));
        var inTenant2 = Assert.Single(await _store.GetEntitiesByChunkIdsAsync(["a1"], "tenant-2", ct));
        Assert.NotEqual(inTenant1.Id, inTenant2.Id);
        Assert.Equal("tenant-1", inTenant1.Partition);
        Assert.Equal("tenant-2", inTenant2.Partition);
        Assert.Empty(await _store.GetEntitiesByChunkIdsAsync(["a1"], ct: ct));
    }

    [Fact]
    public async Task ADocumentReindexedWithoutReuse_JoinsItsStoredEntities_InsteadOfDuplicatingThem()
    {
        // Reuse off means every chunk is extracted again — the chunk-scoped reconstitution never runs, so only the
        // identity join keeps the second build from writing a second copy of every entity.
        var ct = TestContext.Current.CancellationToken;
        var options = new EntityGraphBuildOptions { Partition = "tenant-1", ReuseStoredExtractions = false };
        var chunks = new List<DocumentChunk> { Chunk("a1", "doc-a", "Acme partners with Globex.") };

        await Service().BuildEntityGraphAsync(chunks, options, ct);
        await Service().BuildEntityGraphAsync(chunks, options, ct);

        Assert.Equal(2, (await _store.GetEntitiesByChunkIdsAsync(["a1"], "tenant-1", ct)).Count);
    }

    [Fact]
    public async Task MergingEntityGraphsOfTwoPartitions_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        var inTenant1 = await Service().BuildEntityGraphAsync([Chunk("a1", "doc-a", "Acme signed.")], new EntityGraphBuildOptions { Partition = "tenant-1", PersistToGraphStore = false }, ct);
        var inTenant2 = await Service().BuildEntityGraphAsync([Chunk("b1", "doc-b", "Globex signed.")], new EntityGraphBuildOptions { Partition = "tenant-2", PersistToGraphStore = false }, ct);

        await Assert.ThrowsAsync<ArgumentException>(() => Service().MergeEntityGraphsAsync([inTenant1, inTenant2], cancellationToken: ct));
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
