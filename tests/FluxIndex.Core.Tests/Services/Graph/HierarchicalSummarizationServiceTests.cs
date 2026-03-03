using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Graph;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Graph;

/// <summary>
/// Tests for HierarchicalSummarizationService
/// </summary>
public class HierarchicalSummarizationServiceTests
{
    private readonly ITextCompletionService _mockLlmService;
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HierarchicalSummarizationService> _logger;

    public HierarchicalSummarizationServiceTests()
    {
        _mockLlmService = Substitute.For<ITextCompletionService>();
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = NullLogger<HierarchicalSummarizationService>.Instance;
    }

    private HierarchicalSummarizationService CreateService(
        bool withLlm = true,
        bool withEmbedding = true,
        bool withCache = true)
    {
        return new HierarchicalSummarizationService(
            withLlm ? _mockLlmService : null,
            withEmbedding ? _mockEmbeddingService : null,
            withCache ? _cache : null,
            _logger);
    }

    private CommunityHierarchy CreateMockHierarchy(int levelCount = 2, int communitiesPerLevel = 3)
    {
        var levels = new List<CommunityLevel>();

        for (int level = 0; level < levelCount; level++)
        {
            var communities = new List<LeidenCommunity>();
            for (int i = 0; i < communitiesPerLevel; i++)
            {
                communities.Add(new LeidenCommunity
                {
                    Id = $"community_L{level}_C{i}",
                    Index = i,
                    ChunkIds = new[] { $"chunk_{level}_{i}_0", $"chunk_{level}_{i}_1" },
                    Keywords = new[] { $"topic{i}", "common", "keyword" },
                    RepresentativeChunkIds = new[] { $"chunk_{level}_{i}_0" },
                    Centroid = new EmbeddingVector(new float[] { 0.1f * i, 0.2f, 0.3f }, "centroid")
                });
            }

            levels.Add(new CommunityLevel
            {
                LevelIndex = level,
                Communities = communities,
                Modularity = 0.8 - level * 0.1,
                Resolution = 1.0
            });
        }

        return new CommunityHierarchy
        {
            Id = "test_hierarchy",
            Levels = levels,
            TotalChunks = communitiesPerLevel * 2 * levelCount,
            Options = new LeidenOptions()
        };
    }

    private List<DocumentChunk> CreateMockChunks(CommunityHierarchy hierarchy)
    {
        var chunks = new List<DocumentChunk>();

        foreach (var level in hierarchy.Levels)
        {
            foreach (var community in level.Communities)
            {
                foreach (var chunkId in community.ChunkIds)
                {
                    chunks.Add(new DocumentChunk
                    {
                        Id = chunkId,
                        DocumentId = $"doc_{community.Id}",
                        Content = $"This is content for {chunkId} about {string.Join(" ", community.Keywords)}. It contains important information about the topic.",
                        ChunkIndex = 0
                    });
                }
            }
        }

        return chunks;
    }

    #region GenerateHierarchicalSummariesAsync Tests

    [Fact]
    public async Task GenerateHierarchicalSummariesAsync_WithoutLLM_GeneratesFallbackSummaries()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var hierarchy = CreateMockHierarchy(levelCount: 2);
        var chunks = CreateMockChunks(hierarchy);

        // Act
        var result = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(hierarchy.Id, result.HierarchyId);
        Assert.Equal(2, result.SummariesByLevel.Count);
        Assert.True(result.TotalCommunitiesSummarized > 0);
    }

    [Fact]
    public async Task GenerateHierarchicalSummariesAsync_WithLLM_GeneratesLLMSummaries()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var hierarchy = CreateMockHierarchy(levelCount: 1, communitiesPerLevel: 2);
        var chunks = CreateMockChunks(hierarchy);

        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("This is a generated summary about the community topic.");

        // Act
        var result = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCommunitiesSummarized);
        Assert.All(result.SummariesByLevel[0], s => Assert.NotEmpty(s.Summary));
    }

    [Fact]
    public async Task GenerateHierarchicalSummariesAsync_WithOptions_RespectsOptions()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var hierarchy = CreateMockHierarchy(levelCount: 3);
        var chunks = CreateMockChunks(hierarchy);
        var options = new HierarchicalSummarizationOptions
        {
            LevelsToSummarize = new[] { 0, 1 }, // Only summarize first two levels
            MaxChunksPerCommunity = 1
        };

        // Act
        var result = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.SummariesByLevel.Count); // Only 2 levels requested
        Assert.DoesNotContain(2, result.SummariesByLevel.Keys);
    }

    [Fact]
    public async Task GenerateHierarchicalSummariesAsync_WithEmbedding_GeneratesEmbeddings()
    {
        // Arrange
        var service = CreateService(withLlm: false, withEmbedding: true);
        var hierarchy = CreateMockHierarchy(levelCount: 1, communitiesPerLevel: 2);
        var chunks = CreateMockChunks(hierarchy);

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });

        // Act
        var result = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.All(result.SummariesByLevel[0], s => Assert.NotNull(s.Embedding));
    }

    [Fact]
    public async Task GenerateHierarchicalSummariesAsync_EmptyHierarchy_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var hierarchy = new CommunityHierarchy
        {
            Levels = Array.Empty<CommunityLevel>()
        };
        var chunks = new List<DocumentChunk>();

        // Act
        var result = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.SummariesByLevel);
        Assert.Equal(0, result.TotalCommunitiesSummarized);
    }

    #endregion

    #region GlobalSearchAsync Tests

    [Fact]
    public async Task GlobalSearchAsync_WithMatchingCommunities_ReturnsResults()
    {
        // Arrange
        var service = CreateService(withLlm: false, withEmbedding: true);
        var hierarchy = CreateMockHierarchy(levelCount: 2, communitiesPerLevel: 3);
        var chunks = CreateMockChunks(hierarchy);

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });

        // Generate summaries first
        var summaryResult = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);

        // Act
        var searchResult = await service.GlobalSearchAsync("topic0 information", summaryResult);

        // Assert
        Assert.NotNull(searchResult);
        Assert.NotEmpty(searchResult.MatchedCommunities);
        Assert.NotNull(searchResult.Answer);
    }

    [Fact]
    public async Task GlobalSearchAsync_WithLLM_SynthesizesAnswer()
    {
        // Arrange
        var service = CreateService(withLlm: true, withEmbedding: true);
        var hierarchy = CreateMockHierarchy(levelCount: 1, communitiesPerLevel: 2);
        var chunks = CreateMockChunks(hierarchy);

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f });

        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Based on the community information, here is the synthesized answer about the topic.");

        var summaryResult = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);

        // Act
        var searchResult = await service.GlobalSearchAsync("What is topic0?", summaryResult);

        // Assert
        Assert.NotNull(searchResult.Answer);
        Assert.True(searchResult.Answer.Text.Length > 0);
    }

    [Fact]
    public async Task GlobalSearchAsync_RespectsSearchLevel()
    {
        // Arrange
        var service = CreateService(withLlm: false, withEmbedding: false);
        var hierarchy = CreateMockHierarchy(levelCount: 3, communitiesPerLevel: 2);
        var chunks = CreateMockChunks(hierarchy);

        var summaryResult = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);
        var options = new GlobalSearchOptions { SearchLevel = 1 };

        // Act
        var searchResult = await service.GlobalSearchAsync("query", summaryResult, options);

        // Assert
        Assert.Equal(1, searchResult.SearchLevel);
    }

    [Fact]
    public async Task GlobalSearchAsync_NoMatchingSummaries_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService(withLlm: false, withEmbedding: false);
        var hierarchy = new CommunityHierarchy
        {
            Levels = Array.Empty<CommunityLevel>()
        };

        var summaryResult = new HierarchicalSummaryResult
        {
            SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>()
        };

        // Act
        var searchResult = await service.GlobalSearchAsync("query", summaryResult);

        // Assert
        Assert.NotNull(searchResult);
        Assert.Empty(searchResult.MatchedCommunities);
        Assert.False(searchResult.Answer.IsComplete);
    }

    #endregion

    #region SynthesizeAnswerAsync Tests

    [Fact]
    public async Task SynthesizeAnswerAsync_WithoutLLM_ReturnsCombinedSummaries()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var summaries = new List<CommunitySummary>
        {
            new CommunitySummary
            {
                CommunityId = "c1",
                Summary = "Summary about topic A",
                Confidence = 0.8
            },
            new CommunitySummary
            {
                CommunityId = "c2",
                Summary = "Summary about topic B",
                Confidence = 0.7
            }
        };

        // Act
        var answer = await service.SynthesizeAnswerAsync("query", summaries);

        // Assert
        Assert.NotNull(answer);
        Assert.Contains("topic A", answer.Text);
        Assert.Contains("topic B", answer.Text);
        Assert.Equal(2, answer.SourceCommunityCount);
    }

    [Fact]
    public async Task SynthesizeAnswerAsync_WithLLM_GeneratesSynthesis()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var summaries = new List<CommunitySummary>
        {
            new CommunitySummary
            {
                CommunityId = "c1",
                Title = "Topic A",
                Summary = "Summary about topic A",
                Confidence = 0.8
            }
        };

        _mockLlmService.CompleteAsync(Arg.Any<string>(), Arg.Any<Flux.Abstractions.TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("Synthesized answer about topic A based on the community.");

        // Act
        var answer = await service.SynthesizeAnswerAsync("query", summaries);

        // Assert
        Assert.NotNull(answer);
        Assert.Contains("topic A", answer.Text);
    }

    [Fact]
    public async Task SynthesizeAnswerAsync_EmptySummaries_ReturnsNoInfo()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var summaries = new List<CommunitySummary>();

        // Act
        var answer = await service.SynthesizeAnswerAsync("query", summaries);

        // Assert
        Assert.NotNull(answer);
        Assert.Equal(0, answer.SourceCommunityCount);
        Assert.False(answer.IsComplete);
    }

    [Fact]
    public async Task SynthesizeAnswerAsync_FiltersByConfidence()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var summaries = new List<CommunitySummary>
        {
            new CommunitySummary { CommunityId = "c1", Summary = "High confidence", Confidence = 0.9 },
            new CommunitySummary { CommunityId = "c2", Summary = "Low confidence", Confidence = 0.3 }
        };
        var options = new AnswerSynthesisOptions { MinSummaryConfidence = 0.5 };

        // Act
        var answer = await service.SynthesizeAnswerAsync("query", summaries, options);

        // Assert
        Assert.Contains("High confidence", answer.Text);
        Assert.DoesNotContain("Low confidence", answer.Text);
    }

    #endregion

    #region UpdateSummariesAsync Tests

    [Fact]
    public async Task UpdateSummariesAsync_NoAffectedCommunities_ReturnsExisting()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var existingResult = new HierarchicalSummaryResult
        {
            HierarchyId = "test",
            SummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>
            {
                [0] = new List<CommunitySummary>
                {
                    new CommunitySummary { CommunityId = "c1", Summary = "Original" }
                }
            },
            ChunkLookup = new Dictionary<string, DocumentChunk>()
        };

        // Act
        var result = await service.UpdateSummariesAsync(
            existingResult,
            Enumerable.Empty<DocumentChunk>(),
            Enumerable.Empty<string>());

        // Assert
        Assert.Same(existingResult, result);
    }

    #endregion

    #region InvalidateSummariesAsync Tests

    [Fact]
    public async Task InvalidateSummariesAsync_WithCache_RemovesFromCache()
    {
        // Arrange
        var service = CreateService(withCache: true);
        var cacheKey = "HierarchicalSummary_test_community";

        // Add to cache
        _cache.Set(cacheKey, new CommunitySummary { CommunityId = "test_community" });
        Assert.True(_cache.TryGetValue(cacheKey, out _));

        // Act
        await service.InvalidateSummariesAsync(new[] { "test_community" });

        // Assert
        Assert.False(_cache.TryGetValue(cacheKey, out _));
    }

    [Fact]
    public async Task InvalidateSummariesAsync_WithoutCache_DoesNotThrow()
    {
        // Arrange
        var service = CreateService(withCache: false);

        // Act & Assert - should not throw
        await service.InvalidateSummariesAsync(new[] { "test_community" });
    }

    #endregion

    #region GetCachedSummaryAsync Tests

    [Fact]
    public async Task GetCachedSummaryAsync_CacheHit_ReturnsCached()
    {
        // Arrange
        var service = CreateService(withCache: true);
        var summary = new CommunitySummary
        {
            CommunityId = "test_id",
            Summary = "Cached summary"
        };
        _cache.Set("HierarchicalSummary_test_id", summary);

        // Act
        var result = await service.GetCachedSummaryAsync("test_id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Cached summary", result.Summary);
    }

    [Fact]
    public async Task GetCachedSummaryAsync_CacheMiss_ReturnsNull()
    {
        // Arrange
        var service = CreateService(withCache: true);

        // Act
        var result = await service.GetCachedSummaryAsync("nonexistent_id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCachedSummaryAsync_NoCache_ReturnsNull()
    {
        // Arrange
        var service = CreateService(withCache: false);

        // Act
        var result = await service.GetCachedSummaryAsync("any_id");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region DI Registration Tests

    [Fact]
    public void Service_CanBeCreatedWithMinimalDependencies()
    {
        // Arrange & Act
        var service = new HierarchicalSummarizationService(
            llmService: null,
            embeddingService: null,
            cache: null,
            logger: null);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Service_CanBeCreatedWithAllDependencies()
    {
        // Arrange & Act
        var service = CreateService(withLlm: true, withEmbedding: true, withCache: true);

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task GenerateHierarchicalSummariesAsync_TracksStatistics()
    {
        // Arrange
        var service = CreateService(withLlm: false, withEmbedding: false);
        var hierarchy = CreateMockHierarchy(levelCount: 2, communitiesPerLevel: 2);
        var chunks = CreateMockChunks(hierarchy);

        // Act
        var result = await service.GenerateHierarchicalSummariesAsync(hierarchy, chunks);

        // Assert
        Assert.NotNull(result.Statistics);
        Assert.True(result.Statistics.TotalProcessingTimeMs >= 0);
        Assert.Equal(2, result.Statistics.SummariesByLevel.Count);
    }

    #endregion
}
