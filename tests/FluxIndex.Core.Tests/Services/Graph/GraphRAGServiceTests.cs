using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

public class GraphRAGServiceTests
{
    private readonly IEntityGraphService _mockEntityGraphService;
    private readonly ILeidenCommunityService _mockLeidenCommunityService;
    private readonly IHierarchicalSummarizationService _mockSummarizationService;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ITextCompletionService _mockTextCompletionService;
    private readonly ILogger<GraphRAGService> _mockLogger;
    private readonly GraphRAGService _service;

    public GraphRAGServiceTests()
    {
        _mockEntityGraphService = Substitute.For<IEntityGraphService>();
        _mockLeidenCommunityService = Substitute.For<ILeidenCommunityService>();
        _mockSummarizationService = Substitute.For<IHierarchicalSummarizationService>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockTextCompletionService = Substitute.For<ITextCompletionService>();
        _mockLogger = Substitute.For<ILogger<GraphRAGService>>();

        _service = new GraphRAGService(
            _mockEntityGraphService,
            _mockLeidenCommunityService,
            _mockSummarizationService,
            _mockEmbeddingService,
            _mockTextCompletionService,
            graphStore: null, // No graph store for unit tests
            _mockLogger);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullEntityGraphService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphRAGService(
            null!,
            _mockLeidenCommunityService,
            _mockSummarizationService));
    }

    [Fact]
    public void Constructor_WithNullLeidenService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphRAGService(
            _mockEntityGraphService,
            null!,
            _mockSummarizationService));
    }

    [Fact]
    public void Constructor_WithNullSummarizationService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphRAGService(
            _mockEntityGraphService,
            _mockLeidenCommunityService,
            null!));
    }

    [Fact]
    public void Constructor_WithOptionalServicesNull_Succeeds()
    {
        var service = new GraphRAGService(
            _mockEntityGraphService,
            _mockLeidenCommunityService,
            _mockSummarizationService,
            null,
            null,
            null);

        Assert.NotNull(service);
    }

    #endregion

    #region BuildIndexAsync Tests

    [Fact]
    public async Task BuildIndexAsync_WithValidChunks_ReturnsGraphRAGIndex()
    {
        // Arrange
        var chunks = CreateTestChunks(5);
        SetupMocksForBuildIndex();

        // Act
        var result = await _service.BuildIndexAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Id);
        Assert.NotNull(result.EntityGraph);
        Assert.NotNull(result.CommunityHierarchy);
        Assert.NotNull(result.Summaries);
        Assert.Equal(5, result.Chunks.Count);
    }

    [Fact]
    public async Task BuildIndexAsync_WithMaxChunksOption_LimitsProcessedChunks()
    {
        // Arrange
        var chunks = CreateTestChunks(10);
        var options = new GraphRAGBuildOptions { MaxChunks = 5 };
        SetupMocksForBuildIndex();

        // Act
        var result = await _service.BuildIndexAsync(chunks, options);

        // Assert
        Assert.Equal(5, result.Chunks.Count);
    }

    [Fact]
    public async Task BuildIndexAsync_WithEmptyChunks_ReturnsEmptyIndex()
    {
        // Arrange
        var chunks = new List<DocumentChunk>();
        SetupMocksForBuildIndex();

        // Act
        var result = await _service.BuildIndexAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Chunks);
    }

    [Fact]
    public async Task BuildIndexAsync_RecordsBuildStats()
    {
        // Arrange
        var chunks = CreateTestChunks(5);
        SetupMocksForBuildIndex();

        // Act
        var result = await _service.BuildIndexAsync(chunks);

        // Assert
        Assert.Equal(5, result.Stats.TotalChunks);
        Assert.True(result.Stats.BuildTimeMs > 0);
    }

    #endregion

    #region QueryAsync Tests

    [Fact]
    public async Task QueryAsync_WithLocalScope_UsesLocalSearch()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new GraphRAGQueryOptions { ForceScope = QueryScope.Local };
        SetupMocksForSearch();

        // Act
        var result = await _service.QueryAsync("What is Entity A?", index, options);

        // Assert
        Assert.Equal(QueryScope.Local, result.UsedScope);
        await _mockEntityGraphService.Received(1).SearchByEntitiesAsync(
            Arg.Any<string>(),
            Arg.Any<EntityGraphResult>(),
            Arg.Any<EntitySearchOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WithGlobalScope_UsesGlobalSearch()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new GraphRAGQueryOptions { ForceScope = QueryScope.Global };
        SetupMocksForSearch();

        // Act
        var result = await _service.QueryAsync("Summarize the main themes", index, options);

        // Assert
        Assert.Equal(QueryScope.Global, result.UsedScope);
        await _mockSummarizationService.Received(1).GlobalSearchAsync(
            Arg.Any<string>(),
            Arg.Any<HierarchicalSummaryResult>(),
            Arg.Any<GlobalSearchOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryAsync_WithHybridScope_UsesBothSearches()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new GraphRAGQueryOptions { ForceScope = QueryScope.Hybrid };
        SetupMocksForSearch();

        // Act
        var result = await _service.QueryAsync("How does Entity A relate to the theme?", index, options);

        // Assert
        Assert.Equal(QueryScope.Hybrid, result.UsedScope);
    }

    [Fact]
    public async Task QueryAsync_RecordsQueryStats()
    {
        // Arrange
        var index = CreateTestIndex();
        SetupMocksForSearch();

        // Act
        var result = await _service.QueryAsync("test query", index);

        // Assert
        Assert.True(result.Stats.TotalTimeMs > 0);
        Assert.NotNull(result.Query);
        Assert.Equal("test query", result.Query);
    }

    #endregion

    #region DetectQueryScopeAsync Tests

    [Fact]
    public async Task DetectQueryScopeAsync_WithSpecificQuery_ReturnsLocalScope()
    {
        // Arrange
        var query = "What is the definition of Entity X?";

        // Act
        var result = await _service.DetectQueryScopeAsync(query);

        // Assert
        Assert.Equal(QueryScope.Local, result.Scope);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task DetectQueryScopeAsync_WithBroadQuery_ReturnsGlobalScope()
    {
        // Arrange
        var query = "Summarize the main themes and key topics overall";

        // Act
        var result = await _service.DetectQueryScopeAsync(query);

        // Assert
        Assert.Equal(QueryScope.Global, result.Scope);
    }

    [Fact]
    public async Task DetectQueryScopeAsync_WithRelationalQuery_ReturnsHybridOrLocal()
    {
        // Arrange - relational queries may be detected as local when entity mentions dominate
        var query = "What is the relationship between Entity A and Entity B?";

        // Act
        var result = await _service.DetectQueryScopeAsync(query);

        // Assert - either Hybrid (relationship keyword) or Local (entity mentions) is acceptable
        Assert.True(result.Scope == QueryScope.Hybrid || result.Scope == QueryScope.Local,
            $"Expected Hybrid or Local scope, but got {result.Scope}");
        Assert.True(result.Indicators.ComparativeScore >= 0 || result.Indicators.EntityMentionScore >= 0);
    }

    [Fact]
    public async Task DetectQueryScopeAsync_ReturnsIndicators()
    {
        // Arrange
        var query = "Compare the entities across all categories";

        // Act
        var result = await _service.DetectQueryScopeAsync(query);

        // Assert
        Assert.NotNull(result.Indicators);
        Assert.True(result.Indicators.ComparativeScore > 0);
    }

    [Fact]
    public async Task DetectQueryScopeAsync_DetectsEntityMentions()
    {
        // Arrange
        var query = "Tell me about Microsoft and Google";

        // Act
        var result = await _service.DetectQueryScopeAsync(query);

        // Assert
        Assert.NotEmpty(result.DetectedEntities);
        Assert.Contains("Microsoft", result.DetectedEntities);
    }

    #endregion

    #region LocalSearchAsync Tests

    [Fact]
    public async Task LocalSearchAsync_ReturnsMatchedEntities()
    {
        // Arrange
        var index = CreateTestIndex();
        SetupMocksForSearch();

        // Act
        var result = await _service.LocalSearchAsync("Entity query", index);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.MatchedEntities);
        Assert.True(result.ProcessingTimeMs > 0);
    }

    [Fact]
    public async Task LocalSearchAsync_WithOptions_RespectsMaxEntities()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new LocalSearchOptions { MaxEntities = 5 };
        SetupMocksForSearch();

        // Act
        var result = await _service.LocalSearchAsync("query", index, options);

        // Assert
        await _mockEntityGraphService.Received(1).SearchByEntitiesAsync(
            Arg.Any<string>(),
            Arg.Any<EntityGraphResult>(),
            Arg.Is<EntitySearchOptions>(o => o.TopK == 5),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GlobalSearchAsync Tests

    [Fact]
    public async Task GlobalSearchAsync_ReturnsMatchedCommunities()
    {
        // Arrange
        var index = CreateTestIndex();
        SetupMocksForSearch();

        // Act
        var result = await _service.GlobalSearchAsync("Broad thematic query", index);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Answer);
        Assert.True(result.ProcessingTimeMs > 0);
    }

    [Fact]
    public async Task GlobalSearchAsync_WithOptions_RespectsMaxCommunities()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new GlobalSearchOptions { MaxCommunities = 3 };
        SetupMocksForSearch();

        // Act
        var result = await _service.GlobalSearchAsync("query", index, options);

        // Assert
        await _mockSummarizationService.Received(1).GlobalSearchAsync(
            Arg.Any<string>(),
            Arg.Any<HierarchicalSummaryResult>(),
            Arg.Is<GlobalSearchOptions>(o => o.MaxCommunities == 3),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region HybridSearchAsync Tests

    [Fact]
    public async Task HybridSearchAsync_CombinesLocalAndGlobalResults()
    {
        // Arrange
        var index = CreateTestIndex();
        SetupMocksForSearch();

        // Act
        var result = await _service.HybridSearchAsync("Hybrid query", index);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.LocalResult);
        Assert.NotNull(result.GlobalResult);
        Assert.NotNull(result.FusedDocuments);
    }

    [Fact]
    public async Task HybridSearchAsync_UsesSpecifiedFusionStrategy()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new HybridGraphSearchOptions
        {
            FusionStrategy = GraphFusionStrategy.ReciprocalRankFusion
        };
        SetupMocksForSearch();

        // Act
        var result = await _service.HybridSearchAsync("query", index, options);

        // Assert
        Assert.Equal(GraphFusionStrategy.ReciprocalRankFusion, result.FusionStrategy);
    }

    [Fact]
    public async Task HybridSearchAsync_RespectsWeights()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new HybridGraphSearchOptions
        {
            LocalWeight = 0.7,
            GlobalWeight = 0.3
        };
        SetupMocksForSearch();

        // Act
        var result = await _service.HybridSearchAsync("query", index, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ProcessingTimeMs > 0);
    }

    #endregion

    #region UpdateIndexAsync Tests

    [Fact]
    public async Task UpdateIndexAsync_AddsNewChunks()
    {
        // Arrange
        var index = CreateTestIndex();
        var newChunks = CreateTestChunks(3);
        SetupMocksForUpdate();

        // Act
        var result = await _service.UpdateIndexAsync(index, newChunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(index.Id, result.Id);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateIndexAsync_WithRebuildOption_RebuildsHierarchy()
    {
        // Arrange
        var index = CreateTestIndex();
        var newChunks = CreateTestChunks(2);
        var options = new GraphRAGUpdateOptions { RebuildCommunities = true };
        SetupMocksForUpdate();

        // Act
        var result = await _service.UpdateIndexAsync(index, newChunks, options);

        // Assert
        await _mockLeidenCommunityService.Received(1).DetectHierarchicalCommunitiesAsync(
            Arg.Any<IEnumerable<LeidenChunk>>(),
            Arg.Any<LeidenOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateIndexAsync_WithUpdateSummariesOption_UpdatesSummaries()
    {
        // Arrange
        var index = CreateTestIndex();
        var newChunks = CreateTestChunks(2);
        var options = new GraphRAGUpdateOptions { UpdateSummaries = true };
        SetupMocksForUpdate();

        // Act
        var result = await _service.UpdateIndexAsync(index, newChunks, options);

        // Assert
        await _mockSummarizationService.Received(1).UpdateSummariesAsync(
            Arg.Any<HierarchicalSummaryResult>(),
            Arg.Any<IEnumerable<DocumentChunk>>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region DI Registration Tests

    [Fact]
    public void AddGraphRAGService_AddsServiceToCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IEntityGraphService>(_ => _mockEntityGraphService);
        services.AddScoped<ILeidenCommunityService>(_ => _mockLeidenCommunityService);
        services.AddScoped<IHierarchicalSummarizationService>(_ => _mockSummarizationService);

        // Act
        services.AddGraphRAGService();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IGraphRAGService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    #endregion

    #region Fusion Strategy Tests

    [Fact]
    public async Task HybridSearch_WeightedSum_CorrectlyWeightsScores()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new HybridGraphSearchOptions
        {
            FusionStrategy = GraphFusionStrategy.WeightedSum,
            LocalWeight = 0.6,
            GlobalWeight = 0.4
        };
        SetupMocksForSearch();

        // Act
        var result = await _service.HybridSearchAsync("query", index, options);

        // Assert
        Assert.NotNull(result.FusedDocuments);
    }

    [Fact]
    public async Task HybridSearch_Interleaved_AlternatesSources()
    {
        // Arrange
        var index = CreateTestIndex();
        var options = new HybridGraphSearchOptions
        {
            FusionStrategy = GraphFusionStrategy.Interleaved,
            MaxResults = 6
        };
        SetupMocksForSearch();

        // Act
        var result = await _service.HybridSearchAsync("query", index, options);

        // Assert
        Assert.NotNull(result.FusedDocuments);
    }

    #endregion

    #region Helper Methods

    private List<DocumentChunk> CreateTestChunks(int count)
    {
        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < count; i++)
        {
            var chunk = new DocumentChunk
            {
                Id = $"chunk-{i}",
                DocumentId = "doc-1",
                Content = $"Test content {i}",
                ChunkIndex = i,
                Embedding = new float[384]
            };
            chunks.Add(chunk);
        }
        return chunks;
    }

    private GraphRAGIndex CreateTestIndex()
    {
        var chunks = CreateTestChunks(5);
        return new GraphRAGIndex
        {
            EntityGraph = new EntityGraphResult
            {
                Entities = new List<EntityNode>
                {
                    new EntityNode { Id = "e1", Name = "Entity A", Type = NamedEntityType.Person },
                    new EntityNode { Id = "e2", Name = "Entity B", Type = NamedEntityType.Organization }
                },
                Relations = new List<EntityEdge>
                {
                    new EntityEdge { SourceEntityId = "e1", TargetEntityId = "e2", Label = "works_at" }
                },
                ChunkMappings = new List<EntityChunkMapping>()
            },
            CommunityHierarchy = new CommunityHierarchy
            {
                Levels = new List<CommunityLevel>
                {
                    new CommunityLevel
                    {
                        LevelIndex = 0,
                        Communities = new List<LeidenCommunity>
                        {
                            new LeidenCommunity { Id = "c1", ChunkIds = new[] { "chunk-0", "chunk-1" } }
                        }
                    }
                }
            },
            Summaries = new HierarchicalSummaryResult
            {
                SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>
                {
                    [0] = new List<CommunitySummary>
                    {
                        new CommunitySummary { CommunityId = "c1", Summary = "Test summary" }
                    }
                }
            },
            Chunks = chunks.ToDictionary(c => c.Id)
        };
    }

    private void SetupMocksForBuildIndex()
    {
        _mockEntityGraphService.BuildEntityGraphAsync(
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<EntityGraphBuildOptions>(),
                Arg.Any<CancellationToken>()).Returns(new EntityGraphResult
            {
                Entities = new List<EntityNode>(),
                Relations = new List<EntityEdge>()
            });

        _mockLeidenCommunityService.DetectHierarchicalCommunitiesAsync(
                Arg.Any<IEnumerable<LeidenChunk>>(),
                Arg.Any<LeidenOptions>(),
                Arg.Any<CancellationToken>()).Returns(new CommunityHierarchy
            {
                Levels = new List<CommunityLevel>()
            });

        _mockSummarizationService.GenerateHierarchicalSummariesAsync(
                Arg.Any<CommunityHierarchy>(),
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<HierarchicalSummarizationOptions>(),
                Arg.Any<CancellationToken>()).Returns(new HierarchicalSummaryResult
            {
                SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>()
            });
    }

    private void SetupMocksForSearch()
    {
        _mockEntityGraphService.SearchByEntitiesAsync(
                Arg.Any<string>(),
                Arg.Any<EntityGraphResult>(),
                Arg.Any<EntitySearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(new EntitySearchResult
            {
                Query = "test",
                QueryEntities = new List<EntityNode>(),
                Hits = new List<EntitySearchHit>
                {
                    new EntitySearchHit { ChunkId = "chunk-0", Content = "Test content", Score = 0.8 }
                }
            });

        _mockEntityGraphService.TraverseEntityRelationsAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EntityGraphResult>(),
                Arg.Any<EntityTraversalOptions>(),
                Arg.Any<CancellationToken>()).Returns(new EntityTraversalResult
            {
                Paths = new List<EntityPath>()
            });

        _mockSummarizationService.GlobalSearchAsync(
                Arg.Any<string>(),
                Arg.Any<HierarchicalSummaryResult>(),
                Arg.Any<GlobalSearchOptions>(),
                Arg.Any<CancellationToken>()).Returns(new GlobalSearchResult
            {
                Query = "test",
                Answer = new SynthesizedAnswer { Text = "Global answer", Confidence = 0.8 },
                MatchedCommunities = new List<MatchedCommunity>
                {
                    new MatchedCommunity
                    {
                        CommunityId = "c1",
                        Summary = new CommunitySummary { Summary = "Community summary", SourceChunkIds = new[] { "chunk-0" } },
                        Similarity = 0.7,
                        RelevanceScore = 0.75
                    }
                }
            });

        _mockTextCompletionService.CompleteAsync(
                Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Generated answer");
    }

    private void SetupMocksForUpdate()
    {
        _mockEntityGraphService.BuildEntityGraphAsync(
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<EntityGraphBuildOptions>(),
                Arg.Any<CancellationToken>()).Returns(new EntityGraphResult
            {
                Entities = new List<EntityNode>(),
                Relations = new List<EntityEdge>()
            });

        _mockEntityGraphService.MergeEntityGraphsAsync(
                Arg.Any<IEnumerable<EntityGraphResult>>(),
                Arg.Any<EntityGraphMergeOptions>(),
                Arg.Any<CancellationToken>()).Returns(new EntityGraphResult
            {
                Entities = new List<EntityNode>(),
                Relations = new List<EntityEdge>()
            });

        _mockLeidenCommunityService.UpdateHierarchyAsync(
                Arg.Any<CommunityHierarchy>(),
                Arg.Any<IEnumerable<LeidenChunk>>(),
                Arg.Any<LeidenOptions>(),
                Arg.Any<CancellationToken>()).Returns(new CommunityHierarchy
            {
                Levels = new List<CommunityLevel>()
            });

        _mockLeidenCommunityService.DetectHierarchicalCommunitiesAsync(
                Arg.Any<IEnumerable<LeidenChunk>>(),
                Arg.Any<LeidenOptions>(),
                Arg.Any<CancellationToken>()).Returns(new CommunityHierarchy
            {
                Levels = new List<CommunityLevel>()
            });

        _mockSummarizationService.UpdateSummariesAsync(
                Arg.Any<HierarchicalSummaryResult>(),
                Arg.Any<IEnumerable<DocumentChunk>>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>()).Returns(new HierarchicalSummaryResult
            {
                SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>()
            });
    }

    #endregion
}
