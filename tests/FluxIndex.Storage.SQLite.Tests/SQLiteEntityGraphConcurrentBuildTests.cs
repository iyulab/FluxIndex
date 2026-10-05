using System.Reflection;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// Two builds of one partition that run at the same time, each over a document that mentions the same entity (typed as a
/// product in one and a technology in the other — one type family), leave one entity holding both documents' chunks. The
/// window is forced, not raced: the store double lets each build finish its lookup of stored entities and then waits
/// until both have, so both miss each other's write — the case a consumer's concurrent memorize hit about once in four.
/// </summary>
public sealed class SQLiteEntityGraphConcurrentBuildTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fluxindex-graph-{Guid.NewGuid():N}.db");
    private readonly IOptions<SQLiteEntityGraphOptions> _options = Options.Create(new SQLiteEntityGraphOptions());
    private readonly SQLiteEntityGraphStore _store;

    public SQLiteEntityGraphConcurrentBuildTests()
    {
        using (var context = NewContext())
        {
            context.Database.EnsureCreated();
        }

        _store = new SQLiteEntityGraphStore(
            new DelegateDbContextFactory<SQLiteEntityGraphDbContext>(NewContext), _options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }

    private SQLiteEntityGraphDbContext NewContext() => new(
        new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options, _options);

    private static IAdvancedEntityExtractionService Extractor()
    {
        var extractor = Substitute.For<IAdvancedEntityExtractionService>();
        extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<EntityGraph>>(ci.Arg<IEnumerable<string>>().Select(content => new EntityGraph
            {
                SourceId = content,
                Entities =
                [
                    new ExtractedEntity
                    {
                        Id = Guid.NewGuid().ToString(),
                        Text = "Cloudgate",
                        Type = content.Contains("architecture", StringComparison.Ordinal) ? NamedEntityType.Technology : NamedEntityType.Product,
                        Confidence = 0.9
                    }
                ],
                Relations = []
            }).ToList()));
        return extractor;
    }

    private static DocumentChunk Chunk(string id, string documentId, string content) =>
        new() { Id = id, DocumentId = documentId, Content = content, ChunkIndex = 0 };

    private async Task BuildBothAtOnceAsync(string partitionA, string partitionB)
    {
        using var barrier = new Barrier(2);
        var gated = BarrierGraphStore.Wrap(_store, barrier);
        var service = new EntityGraphService(Extractor(), null, gated, NullLogger<EntityGraphService>.Instance);

        await Task.WhenAll(
            Task.Run(() => service.BuildEntityGraphAsync(
                [Chunk("a1", "doc-a", "Cloudgate is the product we ship.")], new EntityGraphBuildOptions { Partition = partitionA }, Ct), Ct),
            Task.Run(() => service.BuildEntityGraphAsync(
                [Chunk("b1", "doc-b", "Cloudgate is an architecture for gateways.")], new EntityGraphBuildOptions { Partition = partitionB }, Ct), Ct));

        Assert.Equal(2, BarrierGraphStore.Passed(gated));
    }

    [Fact]
    public async Task Two_builds_of_one_partition_at_once_leave_one_entity_with_both_documents_chunks()
    {
        await BuildBothAtOnceAsync("desk-1", "desk-1");

        var stored = await _store.GetEntitiesByNormalizedNamesAsync(["cloudgate"], "desk-1", Ct);
        var entity = Assert.Single(stored);
        Assert.Equal(new[] { "a1", "b1" }, entity.ChunkIds.Order());
        Assert.Equal(new[] { "doc-a", "doc-b" }, entity.DocumentIds.Order());
    }

    // The id is derived per partition: the same name in two partitions stays two entities.
    [Fact]
    public async Task The_same_entity_in_two_partitions_stays_two_entities()
    {
        await BuildBothAtOnceAsync("desk-1", "desk-2");

        Assert.Single(await _store.GetEntitiesByNormalizedNamesAsync(["cloudgate"], "desk-1", Ct));
        Assert.Single(await _store.GetEntitiesByNormalizedNamesAsync(["cloudgate"], "desk-2", Ct));
    }

    public void Dispose()
    {
        // Pooling=False: every connection is closed when its context is, so the file is free to delete.
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// An <see cref="IGraphStore"/> that forwards everything and, after a lookup of stored entities by name, waits until
    /// the other build has made its own lookup too.
    /// </summary>
    public class BarrierGraphStore : DispatchProxy
    {
        private IGraphStore _inner = null!;
        private Barrier _barrier = null!;
        private int _passed;

        public static IGraphStore Wrap(IGraphStore inner, Barrier barrier)
        {
            var proxy = Create<IGraphStore, BarrierGraphStore>();
            var self = (BarrierGraphStore)(object)proxy;
            self._inner = inner;
            self._barrier = barrier;
            return proxy;
        }

        public static int Passed(IGraphStore proxy) => ((BarrierGraphStore)(object)proxy)._passed;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(_inner, args);
            return targetMethod.Name == nameof(IGraphStore.GetEntitiesByNormalizedNamesAsync)
                ? AfterBothLookedUp((Task<IReadOnlyList<GraphEntity>>)result!)
                : result;
        }

        private async Task<IReadOnlyList<GraphEntity>> AfterBothLookedUp(Task<IReadOnlyList<GraphEntity>> lookup)
        {
            var found = await lookup;
            if (await Task.Run(() => _barrier.SignalAndWait(TimeSpan.FromSeconds(30))))
                Interlocked.Increment(ref _passed);
            return found;
        }
    }
}
