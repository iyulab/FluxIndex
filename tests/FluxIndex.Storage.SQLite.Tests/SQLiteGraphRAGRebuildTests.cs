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
/// A GraphRAG build replaces what an earlier build of the same chunks persisted. Community ids are upserted, so a
/// re-build whose chunks clustered differently used to keep the old communities next to the new ones, and a later
/// <see cref="IGraphRAGService.LoadIndexAsync"/> handed both — the old summary included — to global search.
/// </summary>
public sealed class SQLiteGraphRAGRebuildTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SQLiteEntityGraphDbContext _context;
    private readonly SQLiteEntityGraphStore _store;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SQLiteGraphRAGRebuildTests()
    {
        _connection.Open();
        var options = Options.Create(new SQLiteEntityGraphOptions());
        _context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
            options);
        _context.Database.EnsureCreated();
        _store = new SQLiteEntityGraphStore(_context, options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static DocumentChunk Chunk(string id, string documentId) => new()
    {
        Id = id,
        DocumentId = documentId,
        Content = $"content of {id}",
        ChunkIndex = 0,
        Embedding = [0.1f, 0.2f, 0.3f]
    };

    // One build: every chunk handed in forms one level-0 community with the given id and summary.
    private Task<GraphRAGIndex> BuildAsync(string communityId, string summary, params DocumentChunk[] chunks)
    {
        var extractor = Substitute.For<IAdvancedEntityExtractionService>();
        extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<string>>().Select(_ => new EntityGraph { Entities = [], Relations = [] }).ToList());
        var leiden = Substitute.For<ILeidenCommunityService>();
        leiden.DetectHierarchicalCommunitiesAsync(Arg.Any<IEnumerable<LeidenChunk>>(), Arg.Any<LeidenOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new CommunityHierarchy
            {
                Levels =
                [
                    new CommunityLevel
                    {
                        LevelIndex = 0,
                        Communities = [new LeidenCommunity { Id = communityId, ChunkIds = ci.Arg<IEnumerable<LeidenChunk>>().Select(c => c.Id).ToList(), Cohesion = 0.8 }]
                    }
                ]
            });
        var summaries = Substitute.For<IHierarchicalSummarizationService>();
        summaries.GenerateHierarchicalSummariesAsync(Arg.Any<CommunityHierarchy>(), Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<HierarchicalSummarizationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new HierarchicalSummaryResult
            {
                SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>
                {
                    [0] = [new CommunitySummary { CommunityId = communityId, Level = 0, Title = communityId, Summary = summary }]
                },
                TotalCommunitiesSummarized = 1
            });
        var service = new GraphRAGService(
            new EntityGraphService(extractor, null, _store, NullLogger<EntityGraphService>.Instance),
            leiden, summaries, graphStore: _store, logger: NullLogger<GraphRAGService>.Instance);
        return service.BuildIndexAsync(chunks, cancellationToken: Ct);
    }

    private Task<GraphRAGIndex> LoadAsync(params DocumentChunk[] chunks)
    {
        var reader = new GraphRAGService(
            new EntityGraphService(null, null, _store, NullLogger<EntityGraphService>.Instance),
            Substitute.For<ILeidenCommunityService>(),
            Substitute.For<IHierarchicalSummarizationService>(),
            graphStore: _store,
            logger: NullLogger<GraphRAGService>.Instance);
        return reader.LoadIndexAsync(chunks, cancellationToken: Ct);
    }

    [Fact]
    public async Task AChangedDocument_LoadsOnlyItsNewCommunities_AndAnotherDocumentKeepsItsOwn()
    {
        await BuildAsync("doc-b", "B as it is.", Chunk("b1", "doc-b"), Chunk("b2", "doc-b"));
        await BuildAsync("doc-a-v1", "A before the edit.", Chunk("a1", "doc-a"), Chunk("a2", "doc-a"), Chunk("a3", "doc-a"));

        // The edit keeps a1 and a2, drops a3 and adds a4 — the old community still groups a1/a2.
        var newChunks = new[] { Chunk("a1", "doc-a"), Chunk("a2", "doc-a"), Chunk("a4", "doc-a") };
        await BuildAsync("doc-a-v2", "A after the edit.", newChunks);

        var loaded = await LoadAsync(newChunks);
        var community = Assert.Single(Assert.Single(loaded.CommunityHierarchy.Levels).Communities);
        Assert.Equal("doc-a-v2", community.Id);
        Assert.Equal("A after the edit.", Assert.Single(loaded.Summaries.SummariesByLevel[0]).Summary);
        Assert.Null(await _store.GetCommunityByIdAsync("doc-a-v1", Ct));

        // Positive control: the rule removes superseded communities, not everything in the partition.
        Assert.Equal("doc-b", Assert.Single(await _store.GetCommunitiesByChunkIdsAsync(["b1"], ct: Ct)).Id);
    }

    // The same chunk set re-built under a new id (0.56.0 changed every id above level 0 once): the old row goes.
    [Fact]
    public async Task ARebuildUnderANewId_RemovesTheOldRow()
    {
        var chunks = new[] { Chunk("c1", "doc-c"), Chunk("c2", "doc-c") };
        await BuildAsync("old-id", "Old.", chunks);

        await BuildAsync("new-id", "New.", chunks);

        Assert.Equal(["new-id"], (await _store.GetCommunitiesByChunkIdsAsync(["c1", "c2"], ct: Ct)).Select(c => c.Id));
    }

    // Same id means the same chunk set, so the build reuses the stored summary — the supersede step must not
    // delete the community it has just re-stored.
    [Fact]
    public async Task ARebuildOfTheSameCommunity_KeepsItAndItsSummary()
    {
        var chunks = new[] { Chunk("d1", "doc-d") };
        await BuildAsync("same-id", "First.", chunks);

        await BuildAsync("same-id", "Second.", chunks);

        var stored = Assert.Single(await _store.GetCommunitiesByChunkIdsAsync(["d1"], ct: Ct));
        Assert.Equal("same-id", stored.Id);
        Assert.Equal("First.", stored.Summary);
    }

    [Fact]
    public async Task DeleteCommunities_RemovesRowsAndMembers_AndIgnoresUnknownIds()
    {
        await _store.StoreEntityAsync(new GraphEntity { Id = "e1", Name = "E1", Type = NamedEntityType.Organization }, Ct);
        await _store.StoreCommunityAsync(new GraphCommunity { Id = "k1", Name = "k1", EntityIds = ["e1"], ChunkIds = ["x1"] }, Ct);
        await _store.StoreCommunityAsync(new GraphCommunity { Id = "k2", Name = "k2", EntityIds = ["e1"], ChunkIds = ["x2"] }, Ct);

        var deleted = await _store.DeleteCommunitiesAsync(["k1", "missing"], Ct);

        Assert.Equal(1, deleted);
        Assert.Null(await _store.GetCommunityByIdAsync("k1", Ct));
        Assert.Equal(["k2"], (await _store.GetCommunitiesForEntityAsync("e1", Ct)).Select(c => c.Id));
    }
}
