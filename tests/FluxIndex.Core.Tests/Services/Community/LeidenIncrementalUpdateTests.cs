using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Community;

/// <summary>
/// <see cref="LeidenCommunityService.UpdateHierarchyAsync"/> adds chunks to an existing hierarchy. It used to detect on the new chunks alone
/// and return that — so an incremental GraphRAG update replaced every existing community with communities of the new chunks.
/// </summary>
public class LeidenIncrementalUpdateTests
{
    private const int Dimensions = 32;
    private static readonly LeidenOptions Options = new() { RandomSeed = 7, MinCommunitySize = 2, MaxHierarchyLevels = 3, SimilarityThreshold = 0.1 };

    /// <summary>Four groups of four (group axis 2..5) in two themes (axis 0/1); <paramref name="extraAxis"/> ≥ 0 builds a chunk on a fresh axis.</summary>
    private static LeidenChunk Chunk(string id, int group, int member, int extraAxis = -1)
    {
        var v = new float[Dimensions];
        if (extraAxis >= 0)
        {
            v[extraAxis] = 1f;
            v[(extraAxis + member + 1) % Dimensions] += 0.1f;
        }
        else
        {
            v[group / 2] += 0.45f;
            v[2 + group] += 0.85f;
            v[6 + (group * 4 + member) % (Dimensions - 12)] += 0.15f;
        }
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return new LeidenChunk { Id = id, Content = id, Embedding = new EmbeddingVector(v.Select(x => x / norm).ToArray(), "test-model") };
    }

    private static List<LeidenChunk> Corpus() =>
        [.. Enumerable.Range(0, 4).SelectMany(g => Enumerable.Range(0, 4).Select(m => Chunk($"g{g}-m{m}", g, m)))];

    private static LeidenCommunityService Service() => new(NullLogger<LeidenCommunityService>.Instance);

    private static async Task<(CommunityHierarchy Before, CommunityHierarchy After)> AddAsync(params LeidenChunk[] added)
    {
        var service = Service();
        var before = await service.DetectHierarchicalCommunitiesAsync(Corpus(), Options, TestContext.Current.CancellationToken);
        var after = await service.UpdateHierarchyAsync(before, added, cancellationToken: TestContext.Current.CancellationToken);
        return (before, after);
    }

    [Fact]
    public async Task Existing_communities_survive_and_a_new_chunk_joins_its_nearest_group_and_its_ancestors()
    {
        var (before, after) = await AddAsync(Chunk("new-g1", 1, 7));

        Assert.Equal(before.LevelCount, after.LevelCount);
        Assert.Equal(before.TotalChunks + 1, after.TotalChunks);

        var group1 = Assert.Single(after.Levels[0].Communities, c => c.ChunkIds.Contains("g1-m0"));
        Assert.Contains("new-g1", group1.ChunkIds);

        // Untouched groups keep their identity.
        var untouchedBefore = before.Levels[0].Communities.Where(c => !c.ChunkIds.Contains("g1-m0")).Select(c => c.Id).Order();
        var untouchedAfter = after.Levels[0].Communities.Where(c => !c.ChunkIds.Contains("new-g1")).Select(c => c.Id).Order();
        Assert.Equal(untouchedBefore, untouchedAfter);

        // Every ancestor of the group holds the new chunk too, and the links still agree.
        for (var parentId = group1.ParentCommunityId; parentId != null;)
        {
            var parent = after.Levels.SelectMany(l => l.Communities).Single(c => c.Id == parentId);
            Assert.Contains("new-g1", parent.ChunkIds);
            parentId = parent.ParentCommunityId;
        }
        AssertLinksAgree(after);
    }

    [Fact]
    public async Task Chunks_that_fit_no_community_form_their_own()
    {
        var (before, after) = await AddAsync(Chunk("far-a", 0, 0, extraAxis: 20), Chunk("far-b", 0, 1, extraAxis: 20));

        Assert.Equal(before.Levels[0].CommunityCount + 1, after.Levels[0].CommunityCount);
        var fresh = Assert.Single(after.Levels[0].Communities, c => c.ChunkIds.Contains("far-a"));
        Assert.Equal(["far-a", "far-b"], fresh.ChunkIds.Order());
        Assert.Null(fresh.ParentCommunityId);
        Assert.Equal(before.TotalChunks + 2, after.TotalChunks);
        AssertLinksAgree(after);
    }

    [Fact]
    public async Task A_chunk_already_in_the_hierarchy_is_not_added_twice()
    {
        var (before, after) = await AddAsync(Chunk("g2-m1", 2, 1));

        Assert.Equal(before.TotalChunks, after.TotalChunks);
        Assert.Equal(
            before.Levels.Select(l => l.Communities.Sum(c => c.Size)),
            after.Levels.Select(l => l.Communities.Sum(c => c.Size)));
    }

    private static void AssertLinksAgree(CommunityHierarchy hierarchy)
    {
        for (var i = 1; i < hierarchy.Levels.Count; i++)
        {
            foreach (var coarser in hierarchy.Levels[i].Communities)
            {
                var children = hierarchy.Levels[i - 1].Communities.Where(f => f.ParentCommunityId == coarser.Id).ToList();
                Assert.Equal(children.Select(c => c.Id).Order(), coarser.ChildCommunityIds.Order());
                Assert.Equal(coarser.ChunkIds.Order(), children.SelectMany(c => c.ChunkIds).Order());
            }
        }
    }
}
