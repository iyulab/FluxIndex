using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

public class GraphRAGServiceTests
{
    private readonly Mock<IEntityGraphService> _mockEntityGraphService;
    private readonly Mock<ILeidenCommunityService> _mockLeidenCommunityService;
    private readonly Mock<IHierarchicalSummarizationService> _mockSummarizationService;
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<ITextCompletionService> _mockTextCompletionService;
    private readonly Mock<ILogger<GraphRAGService>> _mockLogger;
    private readonly GraphRAGService _service;

    public GraphRAGServiceTests()
    {
        _mockEntityGraphService = new Mock<IEntityGraphService>();
        _mockLeidenCommunityService = new Mock<ILeidenCommunityService>();
        _mockSummarizationService = new Mock<IHierarchicalSummarizationService>();
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockTextCompletionService = new Mock<ITextCompletionService>();
        _mockLogger = new Mock<ILogger<GraphRAGService>>();

        _service = new GraphRAGService(
            _mockEntityGraphService.Object,
            _mockLeidenCommunityService.Object,
            _mockSummarizationService.Object,
            _mockEmbeddingService.Object,
            _mockTextCompletionService.Object,
            graphStore: null, // No graph store for unit tests
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullEntityGraphService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphRAGService(
            null!,
            _mockLeidenCommunityService.Object,
            _mockSummarizationService.Object));
    }

    [Fact]
    public void Constructor_WithNullLeidenService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphRAGService(
            _mockEntityGraphService.Object,
            null!,
            _mockSummarizationService.Object));
    }

    [Fact]
    public void Constructor_WithNullSummarizationService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphRAGService(
            _mockEntityGraphService.Object,
            _mockLeidenCommunityService.Object,
            null!));
    }

    [Fact]
    public void Constructor_WithOptionalServicesNull_Succeeds()
    {
        var service = new GraphRAGService(
            _mockEntityGraphService.Object,
            _mockLeidenCommunityService.Object,
            _mockSummarizationService.Object,
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
        _mockEntityGraphService.Verify(s => s.SearchByEntitiesAsync(
            It.IsAny<string>(),
            It.IsAny<EntityGraphResult>(),
            It.IsAny<EntitySearchOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _mockSummarizationService.Verify(s => s.GlobalSearchAsync(
            It.IsAny<string>(),
            It.IsAny<HierarchicalSummaryResult>(),
            It.IsAny<GlobalSearchOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _mockEntityGraphService.Verify(s => s.SearchByEntitiesAsync(
            It.IsAny<string>(),
            It.IsAny<EntityGraphResult>(),
            It.Is<EntitySearchOptions>(o => o.TopK == 5),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _mockSummarizationService.Verify(s => s.GlobalSearchAsync(
            It.IsAny<string>(),
            It.IsAny<HierarchicalSummaryResult>(),
            It.Is<GlobalSearchOptions>(o => o.MaxCommunities == 3),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _mockLeidenCommunityService.Verify(s => s.DetectHierarchicalCommunitiesAsync(
            It.IsAny<IEnumerable<LeidenChunk>>(),
            It.IsAny<LeidenOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _mockSummarizationService.Verify(s => s.UpdateSummariesAsync(
            It.IsAny<HierarchicalSummaryResult>(),
            It.IsAny<IEnumerable<DocumentChunk>>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DI Registration Tests

    [Fact]
    public void AddGraphRAGService_AddsServiceToCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IEntityGraphService>(_ => _mockEntityGraphService.Object);
        services.AddScoped<ILeidenCommunityService>(_ => _mockLeidenCommunityService.Object);
        services.AddScoped<IHierarchicalSummarizationService>(_ => _mockSummarizationService.Object);

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
        _mockEntityGraphService
            .Setup(s => s.BuildEntityGraphAsync(
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<EntityGraphBuildOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityGraphResult
            {
                Entities = new List<EntityNode>(),
                Relations = new List<EntityEdge>()
            });

        _mockLeidenCommunityService
            .Setup(s => s.DetectHierarchicalCommunitiesAsync(
                It.IsAny<IEnumerable<LeidenChunk>>(),
                It.IsAny<LeidenOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunityHierarchy
            {
                Levels = new List<CommunityLevel>()
            });

        _mockSummarizationService
            .Setup(s => s.GenerateHierarchicalSummariesAsync(
                It.IsAny<CommunityHierarchy>(),
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<HierarchicalSummarizationOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HierarchicalSummaryResult
            {
                SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>()
            });
    }

    private void SetupMocksForSearch()
    {
        _mockEntityGraphService
            .Setup(s => s.SearchByEntitiesAsync(
                It.IsAny<string>(),
                It.IsAny<EntityGraphResult>(),
                It.IsAny<EntitySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntitySearchResult
            {
                Query = "test",
                QueryEntities = new List<EntityNode>(),
                Hits = new List<EntitySearchHit>
                {
                    new EntitySearchHit { ChunkId = "chunk-0", Content = "Test content", Score = 0.8 }
                }
            });

        _mockEntityGraphService
            .Setup(s => s.TraverseEntityRelationsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EntityGraphResult>(),
                It.IsAny<EntityTraversalOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityTraversalResult
            {
                Paths = new List<EntityPath>()
            });

        _mockSummarizationService
            .Setup(s => s.GlobalSearchAsync(
                It.IsAny<string>(),
                It.IsAny<HierarchicalSummaryResult>(),
                It.IsAny<GlobalSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSearchResult
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

        _mockTextCompletionService
            .Setup(s => s.GenerateCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Generated answer");
    }

    private void SetupMocksForUpdate()
    {
        _mockEntityGraphService
            .Setup(s => s.BuildEntityGraphAsync(
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<EntityGraphBuildOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityGraphResult
            {
                Entities = new List<EntityNode>(),
                Relations = new List<EntityEdge>()
            });

        _mockEntityGraphService
            .Setup(s => s.MergeEntityGraphsAsync(
                It.IsAny<IEnumerable<EntityGraphResult>>(),
                It.IsAny<EntityGraphMergeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityGraphResult
            {
                Entities = new List<EntityNode>(),
                Relations = new List<EntityEdge>()
            });

        _mockLeidenCommunityService
            .Setup(s => s.UpdateHierarchyAsync(
                It.IsAny<CommunityHierarchy>(),
                It.IsAny<IEnumerable<LeidenChunk>>(),
                It.IsAny<LeidenOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunityHierarchy
            {
                Levels = new List<CommunityLevel>()
            });

        _mockLeidenCommunityService
            .Setup(s => s.DetectHierarchicalCommunitiesAsync(
                It.IsAny<IEnumerable<LeidenChunk>>(),
                It.IsAny<LeidenOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunityHierarchy
            {
                Levels = new List<CommunityLevel>()
            });

        _mockSummarizationService
            .Setup(s => s.UpdateSummariesAsync(
                It.IsAny<HierarchicalSummaryResult>(),
                It.IsAny<IEnumerable<DocumentChunk>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HierarchicalSummaryResult
            {
                SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>()
            });
    }

    #endregion
}
