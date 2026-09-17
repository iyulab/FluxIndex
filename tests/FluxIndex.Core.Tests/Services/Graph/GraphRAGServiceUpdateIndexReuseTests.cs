using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// <see cref="IGraphRAGService.UpdateIndexAsync"/> runs the entity graph build over the new chunks
/// with the index's options, so stored-extraction reuse applies there too: a chunk the index already
/// holds is not extracted again, and the merge that follows joins the reconstituted entity with the
/// index's own rather than producing a second one. Pins that the two merge steps (the build's join
/// to stored entities, then <c>MergeEntityGraphsAsync</c>) compose without duplicating.
/// </summary>
public class GraphRAGServiceUpdateIndexReuseTests
{
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();
    private readonly IGraphStore _store = Substitute.For<IGraphStore>();
    private readonly List<GraphEntity> _storedEntities = [];
    private readonly List<IReadOnlyList<string>> _extractedBatches = [];

    public GraphRAGServiceUpdateIndexReuseTests()
    {
        _store.StoreEntitiesBatchAsync(Arg.Any<IEnumerable<GraphEntity>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var batch = ci.Arg<IEnumerable<GraphEntity>>().ToList();
                foreach (var entity in batch)
                {
                    _storedEntities.RemoveAll(e => e.Id == entity.Id);
                    _storedEntities.Add(entity);
                }
                return Task.FromResult<IReadOnlyList<string>>(batch.Select(e => e.Id).ToList());
            });
        _store.StoreRelationshipsBatchAsync(Arg.Any<IEnumerable<GraphRelationship>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<string>>(ci.Arg<IEnumerable<GraphRelationship>>().Select(r => r.Id).ToList()));
        _store.GetEntitiesByChunkIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = ci.Arg<IEnumerable<string>>().ToHashSet();
                return Task.FromResult<IReadOnlyList<GraphEntity>>(_storedEntities.Where(e => e.ChunkIds.Any(ids.Contains)).ToList());
            });
        _store.GetRelationshipsAsync(Arg.Any<string>(), Arg.Any<TraversalDirection>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<GraphRelationship>>([]));
        _store.StoreCommunityAsync(Arg.Any<GraphCommunity>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<GraphCommunity>().Id));

        _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var contents = ci.Arg<IEnumerable<string>>().ToList();
                _extractedBatches.Add(contents);
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

    private GraphRAGService CreateService()
    {
        var leiden = Substitute.For<ILeidenCommunityService>();
        leiden.DetectHierarchicalCommunitiesAsync(Arg.Any<IEnumerable<LeidenChunk>>(), Arg.Any<LeidenOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new CommunityHierarchy());
        leiden.UpdateHierarchyAsync(Arg.Any<CommunityHierarchy>(), Arg.Any<IEnumerable<LeidenChunk>>(), Arg.Any<LeidenOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new CommunityHierarchy());
        var summaries = Substitute.For<IHierarchicalSummarizationService>();
        summaries.GenerateHierarchicalSummariesAsync(Arg.Any<CommunityHierarchy>(), Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<HierarchicalSummarizationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new HierarchicalSummaryResult());
        summaries.UpdateSummariesAsync(Arg.Any<HierarchicalSummaryResult>(), Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HierarchicalSummaryResult());

        return new GraphRAGService(
            new EntityGraphService(_extractor, null, _store, NullLogger<EntityGraphService>.Instance),
            leiden,
            summaries,
            graphStore: _store,
            logger: NullLogger<GraphRAGService>.Instance);
    }

    private static DocumentChunk Chunk(string id, string content) => new()
    {
        Id = id,
        DocumentId = "doc-a",
        Content = content,
        ChunkIndex = 0
    };

    [Fact]
    public async Task UpdateIndexAsync_OverAChunkTheIndexAlreadyHolds_ExtractsOnlyTheNewOne_AndKeepsOneEntityPerName()
    {
        var service = CreateService();
        var index = await service.BuildIndexAsync(
        [
            Chunk("c1", "Acme partners with Globex."),
            Chunk("c2", "Globex opened an office.")
        ], cancellationToken: TestContext.Current.CancellationToken);
        var globexId = Assert.Single(index.EntityGraph.Entities, e => e.Name == "Globex").Id;
        _extractedBatches.Clear();

        var updated = await service.UpdateIndexAsync(
            index,
            [
                Chunk("c2", "Globex opened an office."),
                Chunk("c3", "Globex hired Initech.")
            ],
            new GraphRAGUpdateOptions { MergeEntities = true, RebuildCommunities = false, UpdateSummaries = false },
            TestContext.Current.CancellationToken);

        var batch = Assert.Single(_extractedBatches);
        Assert.Equal(["Globex hired Initech."], batch);

        var globex = Assert.Single(updated.EntityGraph.Entities, e => e.Name == "Globex");
        Assert.Equal(globexId, globex.Id);
        Assert.Equal(["Acme", "Globex", "Initech"], updated.EntityGraph.Entities.Select(e => e.Name).Order());
        Assert.Contains(updated.EntityGraph.ChunkMappings, m => m.EntityId == globexId && m.ChunkId == "c3");
        Assert.Equal(3, _storedEntities.Count);
        Assert.Equal(new[] { "c1", "c2", "c3" }, _storedEntities.Single(e => e.Name == "Globex").ChunkIds.Order());
    }
}
