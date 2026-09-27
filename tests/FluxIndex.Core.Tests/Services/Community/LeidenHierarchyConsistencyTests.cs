using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Community;

/// <summary>
/// What a hierarchy must mean, whatever partition the algorithm finds: a coarser community is made of finer ones, so its
/// chunks are exactly the chunks of the finer communities under it, every finer community names its parent, and every
/// coarser community names its children. The corpus has two clear levels — four tight groups of four chunks, paired into
/// two themes — so the detection yields at least two levels to check.
/// </summary>
public class LeidenHierarchyConsistencyTests
{
    private const int Dimensions = 32;

    private static float[] Direction(int axis)
    {
        var v = new float[Dimensions];
        v[axis] = 1f;
        return v;
    }

    /// <summary>theme axis 0/1, group axis 2..5, a small per-chunk offset on axis 6+.</summary>
    private static List<LeidenChunk> Corpus()
    {
        var chunks = new List<LeidenChunk>();
        for (var group = 0; group < 4; group++)
        {
            var theme = Direction(group / 2);
            var groupAxis = Direction(2 + group);
            for (var member = 0; member < 4; member++)
            {
                var v = new float[Dimensions];
                for (var d = 0; d < Dimensions; d++)
                    v[d] = 0.45f * theme[d] + 0.85f * groupAxis[d];
                v[6 + (group * 4 + member) % (Dimensions - 6)] += 0.15f;
                var norm = MathF.Sqrt(v.Sum(x => x * x));
                chunks.Add(new LeidenChunk
                {
                    Id = $"g{group}-m{member}",
                    Content = $"group {group} member {member}",
                    Embedding = new EmbeddingVector(v.Select(x => x / norm).ToArray(), "test-model"),
                });
            }
        }
        return chunks;
    }

    private static async Task<CommunityHierarchy> DetectAsync() =>
        await new LeidenCommunityService(NullLogger<LeidenCommunityService>.Instance).DetectHierarchicalCommunitiesAsync(
            Corpus(),
            new LeidenOptions { RandomSeed = 7, MinCommunitySize = 2, MaxHierarchyLevels = 3, SimilarityThreshold = 0.1 },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_corpus_yields_more_than_one_level()
    {
        var hierarchy = await DetectAsync();

        Assert.True(hierarchy.Levels.Count >= 2, $"expected a hierarchy, got {hierarchy.Levels.Count} level(s): " + string.Join(" | ", hierarchy.Levels.Select(l => string.Join(";", l.Communities.Select(c => string.Join(",", c.ChunkIds))))));
    }

    [Fact]
    public async Task The_coarser_level_groups_the_themes_and_no_level_repeats_the_one_below()
    {
        var hierarchy = await DetectAsync();

        static string Shape(CommunityLevel level) => string.Join(" | ", level.Communities
            .Select(c => string.Join(",", c.ChunkIds.OrderBy(x => x, StringComparer.Ordinal)))
            .OrderBy(x => x, StringComparer.Ordinal));

        // Two themes of two groups each: level 1 holds groups 0+1 and 2+3.
        Assert.Equal(2, hierarchy.Levels[1].CommunityCount);
        Assert.All(hierarchy.Levels[1].Communities, c => Assert.Equal(8, c.Size));
        Assert.Contains(hierarchy.Levels[1].Communities, c => c.ChunkIds.Contains("g0-m0") && c.ChunkIds.Contains("g1-m3"));

        for (var i = 1; i < hierarchy.Levels.Count; i++)
            Assert.NotEqual(Shape(hierarchy.Levels[i - 1]), Shape(hierarchy.Levels[i]));
    }

    [Fact]
    public async Task A_chunk_is_in_at_most_one_community_per_level_and_only_input_chunks_appear()
    {
        var input = Corpus().Select(c => c.Id).ToHashSet();
        var hierarchy = await DetectAsync();

        foreach (var level in hierarchy.Levels)
        {
            var ids = level.Communities.SelectMany(c => c.ChunkIds).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.All(ids, id => Assert.Contains(id, input));
        }
    }

    [Fact]
    public async Task A_coarser_community_holds_exactly_the_chunks_of_the_finer_communities_under_it()
    {
        var hierarchy = await DetectAsync();

        for (var i = 1; i < hierarchy.Levels.Count; i++)
        {
            var finer = hierarchy.Levels[i - 1].Communities;
            foreach (var coarser in hierarchy.Levels[i].Communities)
            {
                var children = finer.Where(f => f.ParentCommunityId == coarser.Id).ToList();
                Assert.NotEmpty(children);
                Assert.Equal(
                    coarser.ChunkIds.OrderBy(x => x, StringComparer.Ordinal),
                    children.SelectMany(c => c.ChunkIds).OrderBy(x => x, StringComparer.Ordinal));
                Assert.Equal(
                    children.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal),
                    coarser.ChildCommunityIds.OrderBy(x => x, StringComparer.Ordinal));
            }
        }
    }

    [Fact]
    public async Task Every_finer_community_whose_chunks_reach_the_coarser_level_names_its_parent()
    {
        var hierarchy = await DetectAsync();

        for (var i = 0; i < hierarchy.Levels.Count - 1; i++)
        {
            var coarser = hierarchy.Levels[i + 1].Communities;
            foreach (var finer in hierarchy.Levels[i].Communities)
            {
                var holder = coarser.SingleOrDefault(c => c.ChunkIds.Contains(finer.ChunkIds[0]));
                Assert.Equal(holder?.Id, finer.ParentCommunityId);
            }
        }

        Assert.All(hierarchy.Levels[^1].Communities, top => Assert.Null(top.ParentCommunityId));
    }
}
