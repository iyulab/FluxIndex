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
/// End-to-end provenance round trip through a real store: build an entity graph from chunks, let the
/// service persist it, then ask the store for entities by chunk id. This is the query every
/// document-scoped graph read stands on; it can only match when the persisted entities carry the
/// chunk ids they came from.
/// </summary>
public sealed class SQLiteEntityGraphStoreProvenanceRoundTripTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SQLiteEntityGraphDbContext _context;
    private readonly SQLiteEntityGraphStore _store;

    public SQLiteEntityGraphStoreProvenanceRoundTripTests()
    {
        _connection.Open();
        var options = Options.Create(new SQLiteEntityGraphOptions());
        _context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
            options);
        _context.Database.EnsureCreated();
        _store = new SQLiteEntityGraphStore(_context, options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }

    [Fact]
    public async Task EntitiesPersistedByTheService_AreFoundByTheChunkTheyCameFrom()
    {
        var ct = TestContext.Current.CancellationToken;
        var extractor = Substitute.For<IAdvancedEntityExtractionService>();
        extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityGraph>
            {
                new() { SourceId = "c1", Entities = [new ExtractedEntity { Id = "acme", Text = "Acme Corp", Type = NamedEntityType.Organization, Confidence = 0.9 }], Relations = [] },
                new() { SourceId = "c2", Entities = [new ExtractedEntity { Id = "globex", Text = "Globex", Type = NamedEntityType.Organization, Confidence = 0.9 }], Relations = [] }
            });
        var service = new EntityGraphService(extractor, null, _store, NullLogger<EntityGraphService>.Instance);
        var chunks = new List<DocumentChunk>
        {
            new() { Id = "c1", DocumentId = "doc-a", Content = "Acme Corp.", ChunkIndex = 0 },
            new() { Id = "c2", DocumentId = "doc-b", Content = "Globex.", ChunkIndex = 1 }
        };

        await service.BuildEntityGraphAsync(chunks, cancellationToken: ct);

        var fromC1 = await _store.GetEntitiesByChunkIdsAsync(["c1"], ct);
        var only = Assert.Single(fromC1);
        Assert.Equal("Acme Corp", only.Name);
        Assert.Equal(["doc-a"], only.DocumentIds);

        var fromUnknown = await _store.GetEntitiesByChunkIdsAsync(["no-such-chunk"], ct);
        Assert.Empty(fromUnknown);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
