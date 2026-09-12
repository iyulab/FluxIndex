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
/// The consumer-facing promise of <see cref="IGraphRAGService.LoadIndexAsync"/>: an index built and
/// persisted by one service instance can be loaded by a fresh one (a new process, after a restart)
/// and queried, scoped to the chunks the consumer hands in. Two documents are indexed; loading one
/// must yield only that document's entities, relationships and communities, and both local and
/// global search must work on what was loaded.
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

    private static DocumentChunk Chunk(string id, string documentId, string content) => new()
    {
        Id = id,
        DocumentId = documentId,
        Content = content,
        ChunkIndex = 0,
        Embedding = [0.1f, 0.2f, 0.3f]   // community detection only considers chunks with embeddings
    };

    private static ExtractedEntity Org(string id, string text) => new() { Id = id, Text = text, Type = NamedEntityType.Organization, Confidence = 0.9 };

    /// <summary>One community per document, with a summary, as a Leiden + summarisation pair would produce.</summary>
    private static (ILeidenCommunityService Leiden, IHierarchicalSummarizationService Summaries) CommunityServicesFor(string communityId, string summaryText)
    {
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
            .Returns(ci => new HierarchicalSummaryResult
            {
                SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>
                {
                    [0] = [new CommunitySummary { CommunityId = communityId, Level = 0, Title = communityId, Summary = summaryText, Themes = ["partners"] }]
                },
                TotalCommunitiesSummarized = 1
            });
        return (leiden, summaries);
    }

    [Fact]
    public async Task AnIndexPersistedByOneInstance_IsQueryableFromAFreshInstance_WithinTheRequestedScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var docA = new[] { Chunk("a1", "doc-a", "Acme Corp partners with Globex."), Chunk("a2", "doc-a", "Acme Corp is based in Springfield.") };
        var docB = new[] { Chunk("b1", "doc-b", "Initech ships widgets.") };

        // --- process 1: extract + detect + summarise (all substituted) and persist (real) ---
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
        var (leidenA, summariesA) = CommunityServicesFor("community-a", "Acme Corp and its partner Globex.");
        var writerA = new GraphRAGService(new EntityGraphService(extractor, null, _store, NullLogger<EntityGraphService>.Instance), leidenA, summariesA, graphStore: _store, logger: NullLogger<GraphRAGService>.Instance);
        await writerA.BuildIndexAsync(docA, cancellationToken: ct);
        var (leidenB, summariesB) = CommunityServicesFor("community-b", "Initech and its widgets.");
        var writerB = new GraphRAGService(new EntityGraphService(extractor, null, _store, NullLogger<EntityGraphService>.Instance), leidenB, summariesB, graphStore: _store, logger: NullLogger<GraphRAGService>.Instance);
        await writerB.BuildIndexAsync(docB, cancellationToken: ct);

        // --- process 2: a fresh service with no extraction, detection or summarisation state ---
        var globalSearch = Substitute.For<IHierarchicalSummarizationService>();
        HierarchicalSummaryResult? searched = null;
        globalSearch.GlobalSearchAsync(Arg.Any<string>(), Arg.Do<HierarchicalSummaryResult>(s => searched = s), Arg.Any<GlobalSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new GlobalSearchResult { Query = ci.Arg<string>() });
        var reader = new GraphRAGService(
            new EntityGraphService(null, null, _store, NullLogger<EntityGraphService>.Instance),
            Substitute.For<ILeidenCommunityService>(),
            globalSearch,
            graphStore: _store,
            logger: NullLogger<GraphRAGService>.Instance);

        var index = await reader.LoadIndexAsync(docA, cancellationToken: ct);

        // Entities and relationships: document A only.
        Assert.Equal(["a1", "a2"], index.Chunks.Keys.Order());
        Assert.Equal(["Acme Corp", "Globex"], index.EntityGraph.Entities.Select(e => e.Name).Order());
        Assert.DoesNotContain(index.EntityGraph.Entities, e => e.Name == "Initech");
        var edge = Assert.Single(index.EntityGraph.Relations);
        Assert.Equal(RelationType.RelatedTo, edge.RelationType);

        // Communities: only the one that groups A's chunks, with its summary.
        var level0 = Assert.Single(index.CommunityHierarchy.Levels);
        var community = Assert.Single(level0.Communities);
        Assert.Equal("community-a", community.Id);
        Assert.Equal(["a1", "a2"], community.ChunkIds.Order());

        // Entity membership is real: the store's member rows reference the community's entities.
        var acmeId = index.EntityGraph.Entities.Single(e => e.Name == "Acme Corp").Id;
        var acmeCommunities = await _store.GetCommunitiesForEntityAsync(acmeId, ct);
        Assert.Equal("community-a", Assert.Single(acmeCommunities).Id);
        var initechCommunities = await _store.GetCommunitiesByChunkIdsAsync(["b1"], ct);
        Assert.Equal("community-b", Assert.Single(initechCommunities).Id);
        var summary = Assert.Single(index.Summaries.SummariesByLevel[0]);
        Assert.Equal("Acme Corp and its partner Globex.", summary.Summary);

        // Local search answers from A's chunks, with their content.
        var local = await reader.LocalSearchAsync("what do we know about acme corp", index, cancellationToken: ct);
        Assert.NotEmpty(local.Documents);
        Assert.All(local.Documents, d => Assert.Contains(d.ChunkId, new[] { "a1", "a2" }));
        Assert.All(local.Documents, d => Assert.Equal("doc-a", d.DocumentId));
        Assert.All(local.Documents, d => Assert.False(string.IsNullOrEmpty(d.Content), $"document {d.ChunkId} came back without its content"));
        Assert.Contains(local.MatchedEntities, e => e.Text == "Acme Corp");

        // Global search receives the reloaded summaries, not an empty result.
        await reader.GlobalSearchAsync("overview of the partners", index, cancellationToken: ct);
        Assert.NotNull(searched);
        Assert.Equal(1, searched!.TotalCommunitiesSummarized);
        Assert.Equal("community-a", Assert.Single(searched.SummariesByLevel[0]).CommunityId);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
