using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// After <see cref="IGraphRAGService.UpdateIndexAsync"/> the index's summaries describe the index's communities: every summarized community
/// of the updated hierarchy has one, and no summary names a community the hierarchy no longer has. A community's id follows its chunks, so a
/// community that gained a chunk is a new id — before, the summaries were matched against the old hierarchy, leaving the grown community
/// unsummarized and the old id's summary in place. Real Leiden detection and real (extractive) summarization; only extraction is faked.
/// </summary>
public class GraphRAGServiceUpdateSummariesFollowHierarchyTests
{
    private const int Dimensions = 32;

    private static DocumentChunk Chunk(string id, int group, int member)
    {
        var v = new float[Dimensions];
        v[group / 2] += 0.45f;
        v[2 + group] += 0.85f;
        v[6 + (group * 4 + member) % (Dimensions - 12)] += 0.15f;
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return new DocumentChunk
        {
            Id = id,
            DocumentId = "doc",
            Content = $"Group {group} note {member} about topic{group}.",
            ChunkIndex = member,
            Embedding = v.Select(x => x / norm).ToArray(),
        };
    }

    private static GraphRAGService CreateService()
    {
        var extractor = Substitute.For<IAdvancedEntityExtractionService>();
        extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyList<EntityGraph>>(
                ci.Arg<IEnumerable<string>>().Select(c => new EntityGraph { SourceId = c, Entities = [], Relations = [] }).ToList()));

        return new GraphRAGService(
            new EntityGraphService(extractor, null, null, NullLogger<EntityGraphService>.Instance),
            new LeidenCommunityService(NullLogger<LeidenCommunityService>.Instance),
            new HierarchicalSummarizationService(null, null, null, NullLogger<HierarchicalSummarizationService>.Instance),
            logger: NullLogger<GraphRAGService>.Instance);
    }

    private static readonly GraphRAGBuildOptions Build = new()
    {
        CommunityOptions = new LeidenOptions { RandomSeed = 7, MinCommunitySize = 2, MaxHierarchyLevels = 3, SimilarityThreshold = 0.1 },
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Summaries_describe_exactly_the_updated_communities(bool rebuildCommunities)
    {
        var service = CreateService();
        var corpus = Enumerable.Range(0, 4).SelectMany(g => Enumerable.Range(0, 4).Select(m => Chunk($"g{g}-m{m}", g, m))).ToList();
        var index = await service.BuildIndexAsync(corpus, Build, TestContext.Current.CancellationToken);
        Assert.NotEmpty(index.Summaries.SummariesByLevel.Values.SelectMany(s => s));

        var updated = await service.UpdateIndexAsync(
            index,
            [Chunk("new-g1", 1, 7)],
            new GraphRAGUpdateOptions { RebuildCommunities = rebuildCommunities, UpdateSummaries = true },
            TestContext.Current.CancellationToken);

        var communityIds = updated.CommunityHierarchy.Levels.SelectMany(l => l.Communities).Select(c => c.Id).Order().ToList();
        var summaryIds = updated.Summaries.SummariesByLevel.Values.SelectMany(s => s).Select(s => s.CommunityId).Order().ToList();
        Assert.Equal(communityIds, summaryIds);

        var holder = Assert.Single(updated.CommunityHierarchy.Levels[0].Communities, c => c.ChunkIds.Contains("new-g1"));
        var holderSummary = Assert.Single(updated.Summaries.SummariesByLevel[0], s => s.CommunityId == holder.Id);
        Assert.Contains("new-g1", holderSummary.SourceChunkIds);
    }
}
