using System.Collections;
using System.Reflection;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// The extraction seam (<see cref="IAdvancedEntityExtractionService"/>) as the indexing pipeline
/// actually exercises it: what the pipeline hands the extractor, what it demands back, and what of
/// the extractor's output survives to the node. Each fact here was a gap found while a consumer
/// planned to put a second extractor behind the seam — a declared option that never arrived, a
/// short result that silently emptied the tail chunks, extractor metadata dropped before persistence.
/// </summary>
public class EntityGraphServiceExtractionSeamTests
{
    private readonly IAdvancedEntityExtractionService _extractor = Substitute.For<IAdvancedEntityExtractionService>();
    private readonly List<EntityExtractionOptions?> _optionsSeen = [];

    private EntityGraphService CreateService() =>
        new(_extractor, null, graphStore: null, NullLogger<EntityGraphService>.Instance);

    private static DocumentChunk Chunk(string id, string content) => new()
    {
        Id = id,
        DocumentId = "doc-a",
        Content = content,
        ChunkIndex = 0
    };

    private void ExtractorReturns(Func<IReadOnlyList<string>, IReadOnlyList<EntityGraph>> graphsFor)
    {
        _extractor.ExtractBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _optionsSeen.Add(ci.Arg<EntityExtractionOptions>());
                return Task.FromResult(graphsFor(ci.Arg<IEnumerable<string>>().ToList()));
            });
    }

    private static EntityGraph GraphWith(params ExtractedEntity[] entities) => new()
    {
        Entities = entities,
        Relations = []
    };

    // ---- the pipeline demands one graph per input, in order ----

    [Fact]
    public async Task BuildEntityGraphAsync_ExtractorReturnsFewerGraphsThanInputs_Throws()
    {
        ExtractorReturns(contents => [GraphWith(new ExtractedEntity { Text = "Acme", Type = NamedEntityType.Organization, Confidence = 0.9 })]);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BuildEntityGraphAsync([Chunk("c1", "Acme."), Chunk("c2", "Globex.")], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("1 graph(s) for 2 chunk(s)", ex.Message);
    }

    [Fact]
    public async Task BuildEntityGraphAsync_OneGraphPerInput_JoinsProvenanceByPosition()
    {
        ExtractorReturns(contents => contents
            .Select(c => GraphWith(new ExtractedEntity { Text = c.TrimEnd('.'), Type = NamedEntityType.Organization, Confidence = 0.9 }))
            .ToList());
        var service = CreateService();

        var result = await service.BuildEntityGraphAsync([Chunk("c1", "Acme."), Chunk("c2", "Globex.")], cancellationToken: TestContext.Current.CancellationToken);

        var acme = Assert.Single(result.Entities, e => e.Name == "Acme");
        var globex = Assert.Single(result.Entities, e => e.Name == "Globex");
        Assert.Equal("c1", Assert.Single(result.ChunkMappings, m => m.EntityId == acme.Id).ChunkId);
        Assert.Equal("c2", Assert.Single(result.ChunkMappings, m => m.EntityId == globex.Id).ChunkId);
    }

    // ---- what the extractor is handed ----

    [Fact]
    public async Task BuildEntityGraphAsync_ExtractionOptions_ReachTheExtractor_WithPipelineKnobsLaidOver()
    {
        ExtractorReturns(contents => contents.Select(_ => GraphWith()).ToList());
        var service = CreateService();
        var declared = new EntityExtractionOptions
        {
            Language = "ko",
            UseLlm = false,
            CustomPatterns = new Dictionary<string, string> { ["ticket"] = @"T-\d+" },
            IncludeContext = false,
            ContextWindowSize = 42,
            MinConfidence = 0.1,        // pipeline's MinEntityConfidence wins
            MaxEntities = 999,          // pipeline's MaxEntitiesPerChunk wins
            EntityTypes = [NamedEntityType.Person]  // kept: the pipeline did not set its own filter
        };

        await service.BuildEntityGraphAsync(
            [Chunk("c1", "x")],
            new EntityGraphBuildOptions { ExtractionOptions = declared, MinEntityConfidence = 0.6, MaxEntitiesPerChunk = 7 },
            TestContext.Current.CancellationToken);

        var seen = Assert.Single(_optionsSeen);
        Assert.NotNull(seen);
        Assert.Equal("ko", seen.Language);
        Assert.False(seen.UseLlm);
        Assert.Equal(@"T-\d+", seen.CustomPatterns?["ticket"]);
        Assert.False(seen.IncludeContext);
        Assert.Equal(42, seen.ContextWindowSize);
        Assert.Equal(0.6, seen.MinConfidence);
        Assert.Equal(7, seen.MaxEntities);
        Assert.Equal([NamedEntityType.Person], seen.EntityTypes);
        Assert.NotSame(declared, seen);   // the consumer's object is not mutated
        Assert.Equal(0.1, declared.MinConfidence);
    }

    [Fact]
    public async Task BuildEntityGraphAsync_PipelineEntityTypes_OverrideTheBaseFilter()
    {
        ExtractorReturns(contents => contents.Select(_ => GraphWith()).ToList());
        var service = CreateService();

        await service.BuildEntityGraphAsync(
            [Chunk("c1", "x")],
            new EntityGraphBuildOptions
            {
                ExtractionOptions = new EntityExtractionOptions { EntityTypes = [NamedEntityType.Person] },
                EntityTypes = [NamedEntityType.Organization, NamedEntityType.Location]
            },
            TestContext.Current.CancellationToken);

        Assert.Equal([NamedEntityType.Organization, NamedEntityType.Location], Assert.Single(_optionsSeen)!.EntityTypes);
    }

    [Fact]
    public async Task BuildEntityGraphAsync_NoExtractionOptions_ExtractorGetsDefaultsPlusPipelineKnobs()
    {
        ExtractorReturns(contents => contents.Select(_ => GraphWith()).ToList());
        var service = CreateService();

        await service.BuildEntityGraphAsync([Chunk("c1", "x")], new EntityGraphBuildOptions { ExtractRelations = false }, TestContext.Current.CancellationToken);

        var seen = Assert.Single(_optionsSeen)!;
        var defaults = new EntityExtractionOptions();
        Assert.Equal(defaults.UseLlm, seen.UseLlm);
        Assert.Equal(defaults.Language, seen.Language);
        Assert.False(seen.ExtractRelations);
    }

    // ---- what of the extractor's output survives to the node ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildEntityGraphAsync_ExtractorMetadataAndSubtype_LandInNodeProperties(bool linkAcrossChunks)
    {
        ExtractorReturns(contents => contents.Select(c => GraphWith(new ExtractedEntity
        {
            Text = "Acme",
            Type = NamedEntityType.Custom,
            Subtype = "desk",
            Confidence = 0.9,
            Metadata = new Dictionary<string, object> { ["origin"] = "declared", ["path"] = c }
        })).ToList());
        var service = CreateService();

        var result = await service.BuildEntityGraphAsync(
            [Chunk("c1", "first"), Chunk("c2", "second")],
            new EntityGraphBuildOptions { LinkEntitiesAcrossChunks = linkAcrossChunks },
            TestContext.Current.CancellationToken);

        Assert.All(result.Entities, node =>
        {
            Assert.Equal("desk", node.Properties["subtype"]);
            Assert.Equal("declared", node.Properties["origin"]);
            Assert.True(node.Properties.ContainsKey("path"));
        });
        if (linkAcrossChunks)
        {
            Assert.Single(result.Entities);
        }
    }

    // ---- GraphRAGBuildOptions.EntityOptions reaches the extractor through the index build ----

    [Fact]
    public async Task GraphRAGService_EntityOptions_ReachTheExtractor_WithAndWithoutEntityGraphOptions()
    {
        ExtractorReturns(contents => contents.Select(_ => GraphWith()).ToList());
        var graphRag = CreateGraphRagService();

        await graphRag.BuildIndexAsync(
            [Chunk("c1", "x")],
            new GraphRAGBuildOptions { EntityOptions = new EntityExtractionOptions { Language = "ko" } },
            TestContext.Current.CancellationToken);
        await graphRag.BuildIndexAsync(
            [Chunk("c2", "y")],
            new GraphRAGBuildOptions
            {
                EntityOptions = new EntityExtractionOptions { Language = "ja", UseLlm = false },
                EntityGraphOptions = new EntityGraphBuildOptions { BatchSize = 3, MinEntityConfidence = 0.7 }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, _optionsSeen.Count);
        Assert.Equal("ko", _optionsSeen[0]!.Language);
        Assert.Equal("ja", _optionsSeen[1]!.Language);
        Assert.False(_optionsSeen[1]!.UseLlm);
        Assert.Equal(0.7, _optionsSeen[1]!.MinConfidence);
    }

    [Fact]
    public void ResolveEntityGraphOptions_LeavesConsumerObjectsAlone()
    {
        var graphOptions = new EntityGraphBuildOptions { BatchSize = 5 };
        var options = new GraphRAGBuildOptions { EntityOptions = new EntityExtractionOptions { Language = "ko" }, EntityGraphOptions = graphOptions };

        var resolved = GraphRAGService.ResolveEntityGraphOptions(options)!;

        Assert.NotSame(graphOptions, resolved);
        Assert.Null(graphOptions.ExtractionOptions);
        Assert.Equal(5, resolved.BatchSize);
        Assert.Same(options.EntityOptions, resolved.ExtractionOptions);

        var already = new EntityGraphBuildOptions { ExtractionOptions = new EntityExtractionOptions { Language = "en" } };
        Assert.Same(already, GraphRAGService.ResolveEntityGraphOptions(new GraphRAGBuildOptions { EntityOptions = options.EntityOptions, EntityGraphOptions = already }));
        Assert.Null(GraphRAGService.ResolveEntityGraphOptions(new GraphRAGBuildOptions()));
    }

    private GraphRAGService CreateGraphRagService()
    {
        var leiden = Substitute.For<ILeidenCommunityService>();
        leiden.DetectHierarchicalCommunitiesAsync(Arg.Any<IEnumerable<LeidenChunk>>(), Arg.Any<LeidenOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new CommunityHierarchy());
        var summaries = Substitute.For<IHierarchicalSummarizationService>();
        summaries.GenerateHierarchicalSummariesAsync(Arg.Any<CommunityHierarchy>(), Arg.Any<IEnumerable<DocumentChunk>>(), Arg.Any<HierarchicalSummarizationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new HierarchicalSummaryResult());
        return new GraphRAGService(CreateService(), leiden, summaries, graphStore: null, logger: NullLogger<GraphRAGService>.Instance);
    }
}

/// <summary>
/// The two hand-written option copies are complete: every public settable property of the source
/// type is carried by the copy. A property added to either options class without being added to its
/// copy fails here rather than silently arriving at the extractor as the default.
/// </summary>
public class OptionsCopyCompletenessTests
{
    [Fact]
    public void EntityExtractionOptions_Copy_CarriesEverySettableProperty()
    {
        var source = new EntityExtractionOptions();
        var expected = FillWithNonDefaults(source);

        var copy = source.Copy();

        AssertAllPropertiesEqual(expected, copy);
    }

    [Fact]
    public void EntityGraphBuildOptions_WithExtractionOptions_CarriesEverySettableProperty()
    {
        var source = new EntityGraphBuildOptions();
        var expected = FillWithNonDefaults(source);
        var extraction = new EntityExtractionOptions { Language = "ko" };

        var copy = source.WithExtractionOptions(extraction);

        expected[nameof(EntityGraphBuildOptions.ExtractionOptions)] = extraction;
        AssertAllPropertiesEqual(expected, copy);
    }

    private static Dictionary<string, object?> FillWithNonDefaults(object target)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in SettableProperties(target.GetType()))
        {
            var value = NonDefaultFor(property.PropertyType, property.GetValue(target));
            property.SetValue(target, value);
            values[property.Name] = value;
        }
        return values;
    }

    private static void AssertAllPropertiesEqual(IReadOnlyDictionary<string, object?> expected, object copy)
    {
        foreach (var property in SettableProperties(copy.GetType()))
        {
            var actual = property.GetValue(copy);
            var want = expected[property.Name];
            if (want is IEnumerable wantSeq && want is not string)
            {
                Assert.True(ReferenceEquals(want, actual) || wantSeq.Cast<object?>().SequenceEqual(((IEnumerable)actual!).Cast<object?>()),
                    $"{copy.GetType().Name}.{property.Name} was not copied");
            }
            else
            {
                Assert.True(Equals(want, actual), $"{copy.GetType().Name}.{property.Name} was not copied (expected {want}, got {actual})");
            }
        }
    }

    private static IEnumerable<PropertyInfo> SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.SetMethod?.IsPublic == true);

    private static object NonDefaultFor(Type type, object? current) => type switch
    {
        _ when type == typeof(bool) => !(bool)current!,
        _ when type == typeof(int) => (int)current! + 17,
        _ when type == typeof(double) => (double)current! + 0.125,
        _ when type == typeof(string) => "non-default",
        _ when type == typeof(IReadOnlyList<NamedEntityType>) => new List<NamedEntityType> { NamedEntityType.Event },
        _ when type == typeof(Dictionary<string, string>) => new Dictionary<string, string> { ["k"] = "v" },
        _ when type == typeof(EntityExtractionOptions) => new EntityExtractionOptions { Language = "zz" },
        _ => throw new NotSupportedException($"Add a non-default generator for {type} — a new option type was introduced.")
    };
}
