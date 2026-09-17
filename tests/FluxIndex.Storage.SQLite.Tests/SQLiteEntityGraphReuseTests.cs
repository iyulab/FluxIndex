using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Storage.SQLite.Graph;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// <see cref="EntityGraphBuildOptions.ReuseStoredExtractions"/> against the real SQLite entity graph
/// store: the second build over the same chunks reaches the store's chunk-scoped query, reconstitutes
/// what it persisted, and calls the extractor for nothing — and the store ends up with one entity per
/// name, not one per build.
/// </summary>
public sealed class SQLiteEntityGraphReuseTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SQLiteEntityGraphDbContext _context;
    private readonly SQLiteEntityGraphStore _store;
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();
    private int _extractionCalls;

    public SQLiteEntityGraphReuseTests()
    {
        _connection.Open();
        var options = Options.Create(new SQLiteEntityGraphOptions());
        _context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
            options);
        _context.Database.EnsureCreated();
        _store = new SQLiteEntityGraphStore(_context, options, NullLogger<SQLiteEntityGraphStore>.Instance);

        _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _extractionCalls++;
                var contents = ci.Arg<IEnumerable<string>>().ToList();
                return Task.FromResult<IReadOnlyList<EntityGraph>>(contents.Select(content => new EntityGraph
                {
                    SourceId = content,
                    Entities = content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(w => w.Trim('.', ','))
                        .Where(w => w.Length > 1 && char.IsUpper(w[0]))
                        .Distinct()
                        .Select(w => new ExtractedEntity { Id = Guid.NewGuid().ToString(), Text = w, Type = NamedEntityType.Organization, Confidence = 0.9 })
                        .ToList(),
                    Relations = []
                }).ToList());
            });
    }

    private static DocumentChunk Chunk(string id, string documentId, string content) => new()
    {
        Id = id,
        DocumentId = documentId,
        Content = content,
        ChunkIndex = 0
    };

    [Fact]
    public async Task SecondBuildOverTheSameChunks_ExtractsNothing_AndLeavesOneEntityPerName()
    {
        var chunks = new List<DocumentChunk>
        {
            Chunk("c1", "doc-a", "Acme partners with Globex."),
            Chunk("c2", "doc-a", "Globex opened an office.")
        };
        var service = new EntityGraphService(_extractor, null, _store, NullLogger<EntityGraphService>.Instance);

        var first = await service.BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, _extractionCalls);

        var second = await service.BuildEntityGraphAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, _extractionCalls);
        Assert.Equal(2, second.Stats.ChunksReused);
        Assert.Equal(0, second.Stats.ChunksExtracted);
        Assert.Equal(first.Entities.Select(e => e.Id).Order(), second.Entities.Select(e => e.Id).Order());

        var stored = await _store.GetEntitiesByChunkIdsAsync(["c1", "c2"], ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, stored.Count);
        Assert.Equal(new[] { "c1", "c2" }, stored.Single(e => e.Name == "Globex").ChunkIds.Order());
    }

    [Fact]
    public async Task ARewrittenChunk_IsExtractedAlone_AndItsEntityJoinsTheStoredOne()
    {
        var service = new EntityGraphService(_extractor, null, _store, NullLogger<EntityGraphService>.Instance);
        await service.BuildEntityGraphAsync(
        [
            Chunk("c1", "doc-a", "Acme partners with Globex."),
            Chunk("c2", "doc-a", "Globex opened an office.")
        ], cancellationToken: TestContext.Current.CancellationToken);

        var result = await service.BuildEntityGraphAsync(
        [
            Chunk("c1", "doc-a", "Acme partners with Globex."),
            Chunk("c3", "doc-a", "Globex hired Initech.")
        ], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, _extractionCalls);
        Assert.Equal(1, result.Stats.ChunksReused);
        Assert.Equal(1, result.Stats.ChunksExtracted);

        var globex = await _store.GetEntitiesByChunkIdsAsync(["c3"], ct: TestContext.Current.CancellationToken);
        var joined = Assert.Single(globex, e => e.Name == "Globex");
        Assert.Equal(new[] { "c1", "c2", "c3" }, joined.ChunkIds.Order());
        var all = await _store.GetEntitiesByChunkIdsAsync(["c1", "c2", "c3"], ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, all.Count); // Acme, Globex, Initech
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
