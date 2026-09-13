using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// Three <see cref="GraphRAGQueryOptions"/> that were declared but never read by <see cref="GraphRAGService.QueryAsync"/>
/// (found by <c>OptionsReachabilityRosterTests</c>): <c>IncludeContext</c>, <c>IncludeRelationships</c>,
/// <c>IncludeCommunityContext</c>. Each fact is the observable difference the option now makes on the result.
/// </summary>
public class GraphRAGServiceQueryOptionsTests
{
    private readonly IEntityGraphService _entityGraph = Substitute.For<IEntityGraphService>();
    private readonly IHierarchicalSummarizationService _summaries = Substitute.For<IHierarchicalSummarizationService>();
    private readonly ITextCompletionService _llm = Substitute.For<ITextCompletionService>();
    private readonly List<string> _answerPrompts = [];
    private readonly GraphRAGService _service;

    public GraphRAGServiceQueryOptionsTests()
    {
        _entityGraph.SearchByEntitiesAsync(Arg.Any<string>(), Arg.Any<EntityGraphResult>(), Arg.Any<EntitySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new EntitySearchResult
            {
                Query = "q",
                QueryEntities = [new EntityNode { Id = "e1", Name = "Acme", Type = NamedEntityType.Organization }],
                Hits = [new EntitySearchHit { ChunkId = "chunk-0", Content = "Acme partners with Globex.", Score = 0.8, EntityMatchScore = 1.0 }]
            });
        _entityGraph.TraverseEntityRelationsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EntityGraphResult>(), Arg.Any<EntityTraversalOptions>(), Arg.Any<CancellationToken>())
            .Returns(new EntityTraversalResult
            {
                Paths =
                [
                    new EntityPath
                    {
                        Entities = [new EntityNode { Id = "e1" }, new EntityNode { Id = "e2" }],
                        Relations = [new EntityEdge { SourceEntityId = "e1", TargetEntityId = "e2", Label = "partners_with", Weight = 0.7 }]
                    }
                ]
            });
        _summaries.GlobalSearchAsync(Arg.Any<string>(), Arg.Any<HierarchicalSummaryResult>(), Arg.Any<GlobalSearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new GlobalSearchResult
            {
                Query = "q",
                Answer = new SynthesizedAnswer { Text = "global", Confidence = 0.8 },
                MatchedCommunities =
                [
                    new MatchedCommunity
                    {
                        CommunityId = "c1",
                        Summary = new CommunitySummary { CommunityId = "c1", Title = "Partnerships", Summary = "Acme and Globex partner.", SourceChunkIds = ["chunk-0"] },
                        Similarity = 0.7,
                        RelevanceScore = 0.75
                    }
                ]
            });
        _llm.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci => { _answerPrompts.Add(ci.Arg<string>()); return Task.FromResult("answer"); });

        _service = new GraphRAGService(
            _entityGraph,
            Substitute.For<ILeidenCommunityService>(),
            _summaries,
            embeddingService: null,
            textCompletionService: _llm,
            graphStore: null,
            NullLogger<GraphRAGService>.Instance);
    }

    private static GraphRAGIndex Index() => new()
    {
        EntityGraph = new EntityGraphResult
        {
            Entities = [new EntityNode { Id = "e1", Name = "Acme", Type = NamedEntityType.Organization }, new EntityNode { Id = "e2", Name = "Globex", Type = NamedEntityType.Organization }],
            Relations = [new EntityEdge { SourceEntityId = "e1", TargetEntityId = "e2", Label = "partners_with" }],
            ChunkMappings = []
        },
        CommunityHierarchy = new CommunityHierarchy(),
        Summaries = new HierarchicalSummaryResult(),
        Chunks = new Dictionary<string, DocumentChunk> { ["chunk-0"] = new() { Id = "chunk-0", DocumentId = "doc-a", Content = "Acme partners with Globex.", ChunkIndex = 0 } }
    };

    // ---- IncludeContext ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncludeContext_DecidesWhetherDocumentsCarryChunkText_AnswerAlwaysSeesIt(bool includeContext)
    {
        var result = await _service.QueryAsync("Who does Acme partner with?", Index(),
            new GraphRAGQueryOptions { ForceScope = QueryScope.Local, IncludeContext = includeContext }, TestContext.Current.CancellationToken);

        var doc = Assert.Single(result.Documents);
        Assert.Equal("chunk-0", doc.ChunkId);
        Assert.Equal(0.8, doc.Score);
        Assert.Equal(includeContext ? "Acme partners with Globex." : string.Empty, doc.Content);
        Assert.Equal("answer", result.Answer);
        Assert.Contains(_answerPrompts, p => p.Contains("Acme partners with Globex."));
    }

    // ---- IncludeRelationships ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncludeRelationships_DecidesWhetherTraversedRelationshipsAreReturned(bool includeRelationships)
    {
        var result = await _service.QueryAsync("Who does Acme partner with?", Index(),
            new GraphRAGQueryOptions { ForceScope = QueryScope.Local, IncludeRelationships = includeRelationships }, TestContext.Current.CancellationToken);

        if (includeRelationships)
        {
            var rel = Assert.Single(result.Relationships);
            Assert.Equal(("e1", "e2", "partners_with", 0.7), (rel.SourceEntityId, rel.TargetEntityId, rel.RelationType, rel.Strength));
        }
        else
        {
            Assert.Empty(result.Relationships);
        }
    }

    [Fact]
    public async Task IncludeRelationships_GlobalScope_HasNoneToReturn()
    {
        var result = await _service.QueryAsync("Summarize the partnerships", Index(),
            new GraphRAGQueryOptions { ForceScope = QueryScope.Global, IncludeRelationships = true }, TestContext.Current.CancellationToken);

        Assert.Empty(result.Relationships);
    }

    // ---- IncludeCommunityContext ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncludeCommunityContext_DecidesWhetherCommunitiesReachTheAnswerAndTheResult(bool includeCommunities)
    {
        var result = await _service.QueryAsync("Summarize the partnerships", Index(),
            new GraphRAGQueryOptions { ForceScope = QueryScope.Global, IncludeCommunityContext = includeCommunities }, TestContext.Current.CancellationToken);

        // Global scope retrieves community summaries as documents either way; the option governs the
        // "Community Context" section of the answer prompt and the RelatedCommunities block of the result.
        Assert.NotEmpty(result.Documents);
        if (includeCommunities)
        {
            Assert.Equal("c1", Assert.Single(result.RelatedCommunities).Id);
            Assert.Contains(_answerPrompts, p => p.Contains("Community Context:") && p.Contains("- Partnerships: Acme and Globex partner."));
        }
        else
        {
            Assert.Empty(result.RelatedCommunities);
            Assert.DoesNotContain(_answerPrompts, p => p.Contains("Community Context:"));
        }
    }
}
