using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
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
/// Re-memorizing an unchanged document is routine (a refresh, a retried job, a bulk re-index). The vector and keyword
/// legs write the same rows again because chunk ids are deterministic; the graph leg must do the same for communities,
/// and must not pay for the same community summary twice. Real Leiden detection, real SQLite graph store.
/// </summary>
public sealed class SQLiteGraphRAGCommunityIdempotencyTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SQLiteEntityGraphDbContext _context;
    private readonly SQLiteEntityGraphStore _store;

    public SQLiteGraphRAGCommunityIdempotencyTests()
    {
        _connection.Open();
        var options = Options.Create(new SQLiteEntityGraphOptions());
        _context = new SQLiteEntityGraphDbContext(
            new DbContextOptionsBuilder<SQLiteEntityGraphDbContext>().UseSqlite(_connection).Options,
            options);
        _context.Database.EnsureCreated();
        _store = new SQLiteEntityGraphStore(_context, options, NullLogger<SQLiteEntityGraphStore>.Instance);
    }

    private static DocumentChunk Chunk(string id, int index, string content, float[] embedding) => new()
    {
        Id = id,
        DocumentId = "doc-profile",
        Content = content,
        ChunkIndex = index,
        Embedding = embedding
    };

    private static ExtractedEntity Org(string id, string text) => new() { Id = id, Text = text, Type = NamedEntityType.Organization, Confidence = 0.9 };

    [Fact]
    public async Task RebuildingAnUnchangedDocument_KeepsTheCommunityCount_AndSummarizesOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        DocumentChunk[] chunks =
        [
            Chunk("profile-1", 0, "Acme Corp partners with Globex.", [0.9f, 0.1f, 0.0f]),
            Chunk("profile-2", 1, "Acme Corp is based in Springfield.", [0.85f, 0.15f, 0.05f]),
            Chunk("profile-3", 2, "Initech ships widgets to Acme Corp.", [0.1f, 0.9f, 0.1f]),
            Chunk("profile-4", 3, "Initech was founded in Shelbyville.", [0.05f, 0.95f, 0.1f]),
        ];

        var extractor = Substitute.For<IAdvancedEntityExtractionService>();
        extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<string>>().Select((_, i) => new EntityGraph
            {
                SourceId = chunks[i].Id,
                Entities = i < 2 ? [Org("acme", "Acme Corp")] : [Org("initech", "Initech")],
                Relations = []
            }).ToList());

        var summarizer = Substitute.For<IHierarchicalSummarizationService>();
        summarizer.GenerateHierarchicalSummariesAsync(Arg.Any<CommunityHierarchy>(), Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<HierarchicalSummarizationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var hierarchy = ci.Arg<CommunityHierarchy>();
                return new HierarchicalSummaryResult
                {
                    SummariesByLevel = hierarchy.Levels.ToDictionary(
                        l => l.LevelIndex,
                        l => (IReadOnlyList<CommunitySummary>)[.. l.Communities.Select(c => new CommunitySummary { CommunityId = c.Id, Level = l.LevelIndex, Title = c.Id, Summary = $"Summary of {c.Size} chunks." })]),
                    TotalCommunitiesSummarized = hierarchy.Levels.Sum(l => l.CommunityCount)
                };
            });

        async Task<int> BuildAndCountAsync()
        {
            var service = new GraphRAGService(
                new EntityGraphService(extractor, null, _store, NullLogger<EntityGraphService>.Instance),
                new LeidenCommunityService(NullLogger<LeidenCommunityService>.Instance),
                summarizer,
                graphStore: _store,
                logger: NullLogger<GraphRAGService>.Instance);
            var index = await service.BuildIndexAsync(chunks, new GraphRAGBuildOptions { CommunityOptions = new LeidenOptions { MinCommunitySize = 1 } }, ct);
            Assert.NotEmpty(index.CommunityHierarchy.Levels);
            return (await _store.GetCommunitiesByChunkIdsAsync(chunks.Select(c => c.Id), ct)).Count;
        }

        var afterFirst = await BuildAndCountAsync();
        var afterSecond = await BuildAndCountAsync();
        var afterThird = await BuildAndCountAsync();

        Assert.True(afterFirst > 0, "the first build must persist communities for this fact to mean anything");
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(afterFirst, afterThird);
        await summarizer.Received(1).GenerateHierarchicalSummariesAsync(
            Arg.Any<CommunityHierarchy>(), Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<HierarchicalSummarizationOptions?>(), Arg.Any<CancellationToken>());
        var stored = await _store.GetCommunitiesByChunkIdsAsync(chunks.Select(c => c.Id), ct);
        Assert.All(stored, c => Assert.False(string.IsNullOrEmpty(c.Summary), $"community {c.Id} lost its summary on a rebuild"));
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
