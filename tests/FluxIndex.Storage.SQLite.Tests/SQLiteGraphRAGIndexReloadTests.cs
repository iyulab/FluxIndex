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
/// The consumer-facing promise of <see cref="IGraphRAGService.LoadIndexAsync"/>: an entity graph
/// persisted by one service instance can be loaded by a fresh one (a new process, after a restart)
/// and queried, scoped to the chunks the consumer hands in. Persist two documents, load one, and
/// local search must only ever answer from that one.
/// </summary>
public sealed class SQLiteGraphRAGIndexReloadTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SQLiteEntityGraphDbContext _context;
    private readonly SQLiteEntityGraphStore _store;

    public SQLiteGraphRAGIndexReloadTests()
    {
        _connection.Open();
        var options = Options.Create(new SQLiteEntityGraphOptions());
        _context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
            options);
        _context.Database.EnsureCreated();
        _store = new SQLiteEntityGraphStore(_context, options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }

    private static DocumentChunk Chunk(string id, string documentId, string content) => new() { Id = id, DocumentId = documentId, Content = content, ChunkIndex = 0 };

    private static ExtractedEntity Org(string id, string text) => new() { Id = id, Text = text, Type = NamedEntityType.Organization, Confidence = 0.9 };

    [Fact]
    public async Task AnIndexPersistedByOneInstance_IsQueryableFromAFreshInstance_WithinTheRequestedScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var docA = new[] { Chunk("a1", "doc-a", "Acme Corp partners with Globex."), Chunk("a2", "doc-a", "Acme Corp is based in Springfield.") };
        var docB = new[] { Chunk("b1", "doc-b", "Initech ships widgets.") };

        // --- process 1: extract + persist (extraction substituted, persistence real) ---
        var extractor = Substitute.For<IAdvancedEntityExtractionService>();
        extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(
                new List<EntityGraph>
                {
                    new() { SourceId = "a1", Entities = [Org("acme", "Acme Corp"), Org("globex", "Globex")], Relations = [new EntityRelation { SourceEntityId = "acme", TargetEntityId = "globex", Type = RelationType.RelatedTo, Label = "partners with", Confidence = 0.8 }] },
                    new() { SourceId = "a2", Entities = [Org("acme", "Acme Corp")], Relations = [] }
                },
                new List<EntityGraph>
                {
                    new() { SourceId = "b1", Entities = [Org("initech", "Initech")], Relations = [] }
                });
        var writer = new EntityGraphService(extractor, null, _store, NullLogger<EntityGraphService>.Instance);
        await writer.BuildEntityGraphAsync(docA, cancellationToken: ct);
        await writer.BuildEntityGraphAsync(docB, cancellationToken: ct);

        // --- process 2: a fresh service with no extraction service and no in-memory state ---
        var reader = new GraphRAGService(
            new EntityGraphService(null, null, _store, NullLogger<EntityGraphService>.Instance),
            Substitute.For<ILeidenCommunityService>(),
            Substitute.For<IHierarchicalSummarizationService>(),
            graphStore: _store,
            logger: NullLogger<GraphRAGService>.Instance);

        var index = await reader.LoadIndexAsync(docA, cancellationToken: ct);

        Assert.Equal(["a1", "a2"], index.Chunks.Keys.Order());
        Assert.Equal(["Acme Corp", "Globex"], index.EntityGraph.Entities.Select(e => e.Name).Order());
        Assert.DoesNotContain(index.EntityGraph.Entities, e => e.Name == "Initech");
        var edge = Assert.Single(index.EntityGraph.Relations);
        Assert.Equal(RelationType.RelatedTo, edge.RelationType);

        var result = await reader.LocalSearchAsync("what do we know about acme corp", index, cancellationToken: ct);

        Assert.NotEmpty(result.Documents);
        Assert.All(result.Documents, d => Assert.Contains(d.ChunkId, new[] { "a1", "a2" }));
        Assert.All(result.Documents, d => Assert.Equal("doc-a", d.DocumentId));
        Assert.All(result.Documents, d => Assert.False(string.IsNullOrEmpty(d.Content), $"document {d.ChunkId} came back without its content"));
        Assert.Contains(result.MatchedEntities, e => e.Text == "Acme Corp");
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
