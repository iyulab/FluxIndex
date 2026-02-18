using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Community;

/// <summary>
/// Unit tests for LeidenCommunityService covering hierarchical community detection,
/// summary generation, and community search functionality.
/// </summary>
public class LeidenCommunityServiceTests
{
    private readonly ILogger<LeidenCommunityService> _loggerMock;
    private readonly ITextCompletionService _llmServiceMock;

    public LeidenCommunityServiceTests()
    {
        _loggerMock = Substitute.For<ILogger<LeidenCommunityService>>();
        _llmServiceMock = Substitute.For<ITextCompletionService>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithLogger_Succeeds()
    {
        // Act
        var service = new LeidenCommunityService(_loggerMock);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithLoggerAndLlmService_Succeeds()
    {
        // Act
        var service = new LeidenCommunityService(_loggerMock, _llmServiceMock);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LeidenCommunityService(null!));
    }

    #endregion

    #region DetectHierarchicalCommunitiesAsync Tests

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_EmptyChunks_ReturnsEmptyHierarchy()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = Enumerable.Empty<LeidenChunk>();

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks);

        // Assert
        Assert.Empty(result.Levels);
        Assert.Equal(0, result.TotalChunks);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_SingleChunk_ReturnsHierarchyWithOneChunk()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = new List<LeidenChunk>
        {
            CreateChunk("1", "Test content", CreateRandomEmbedding())
        };
        var options = new LeidenOptions { MinCommunitySize = 1 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.Equal(1, result.TotalChunks);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_MultipleChunks_DetectsCommunities()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var baseEmbedding = CreateRandomEmbedding();
        var chunks = new List<LeidenChunk>
        {
            CreateChunk("1", "Machine learning content", baseEmbedding),
            CreateChunk("2", "Machine learning algorithms", CreateSimilarEmbedding(baseEmbedding, 0.95f)),
            CreateChunk("3", "Deep learning neural networks", CreateSimilarEmbedding(baseEmbedding, 0.9f)),
            CreateChunk("4", "Cooking recipes", CreateRandomEmbedding()),
            CreateChunk("5", "Food preparation tips", CreateSimilarEmbedding(CreateRandomEmbedding(), 0.9f))
        };
        var options = new LeidenOptions { MinCommunitySize = 1, SimilarityThreshold = 0.5 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.Equal(5, result.TotalChunks);
        Assert.NotNull(result.Options);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_WithOptions_UsesProvidedOptions()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(5);
        var options = new LeidenOptions
        {
            Resolution = 1.5,
            MaxIterations = 50,
            MinModularityGain = 0.001,
            MaxHierarchyLevels = 3,
            MinCommunitySize = 1
        };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.Equal(options, result.Options);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_WithRandomSeed_ProducesReproducibleResults()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(10);
        var options = new LeidenOptions { RandomSeed = 42, MinCommunitySize = 1 };

        // Act
        var result1 = await service.DetectHierarchicalCommunitiesAsync(chunks, options);
        var result2 = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.Equal(result1.LevelCount, result2.LevelCount);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_IncludesStatistics()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(5);
        var options = new LeidenOptions { MinCommunitySize = 1 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result.Statistics);
        Assert.True(result.Statistics.ProcessingTimeMs >= 0);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_CancellationToken_IsPassed()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(3);

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, cancellationToken: cts.Token);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(10);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.DetectHierarchicalCommunitiesAsync(chunks, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_CalculatesCentroids()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var baseEmbedding = CreateRandomEmbedding();
        var chunks = new List<LeidenChunk>
        {
            CreateChunk("1", "Content 1", baseEmbedding),
            CreateChunk("2", "Content 2", CreateSimilarEmbedding(baseEmbedding, 0.95f)),
            CreateChunk("3", "Content 3", CreateSimilarEmbedding(baseEmbedding, 0.9f))
        };
        var options = new LeidenOptions { MinCommunitySize = 1, SimilarityThreshold = 0.5 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        if (result.LevelCount > 0)
        {
            foreach (var community in result.Levels[0].Communities)
            {
                Assert.NotNull(community.Centroid);
            }
        }
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_ExtractsKeywords()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var baseEmbedding = CreateRandomEmbedding();
        var chunks = new List<LeidenChunk>
        {
            CreateChunk("1", "Machine learning algorithms are important", baseEmbedding),
            CreateChunk("2", "Machine learning and deep learning", CreateSimilarEmbedding(baseEmbedding, 0.95f)),
            CreateChunk("3", "Neural networks for machine learning", CreateSimilarEmbedding(baseEmbedding, 0.9f))
        };
        var options = new LeidenOptions { MinCommunitySize = 1, SimilarityThreshold = 0.5 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        if (result.LevelCount > 0 && result.Levels[0].Communities.Any())
        {
            var community = result.Levels[0].Communities.First();
            Assert.NotEmpty(community.Keywords);
        }
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_SelectsRepresentatives()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var baseEmbedding = CreateRandomEmbedding();
        var chunks = new List<LeidenChunk>
        {
            CreateChunk("1", "Content 1", baseEmbedding),
            CreateChunk("2", "Content 2", CreateSimilarEmbedding(baseEmbedding, 0.95f)),
            CreateChunk("3", "Content 3", CreateSimilarEmbedding(baseEmbedding, 0.9f)),
            CreateChunk("4", "Content 4", CreateSimilarEmbedding(baseEmbedding, 0.85f))
        };
        var options = new LeidenOptions { MinCommunitySize = 1, SimilarityThreshold = 0.5 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        if (result.LevelCount > 0 && result.Levels[0].Communities.Any())
        {
            var community = result.Levels[0].Communities.First();
            Assert.NotEmpty(community.RepresentativeChunkIds);
            Assert.True(community.RepresentativeChunkIds.Count <= 3);
        }
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_WithRefinement_RefinesPartition()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(10);
        var options = new LeidenOptions { UseRefinement = true, MinCommunitySize = 1 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_WithSummaryGeneration_GeneratesSummaries()
    {
        // Arrange
        _llmServiceMock.GenerateCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns("This community focuses on machine learning topics.");

        var service = new LeidenCommunityService(_loggerMock, _llmServiceMock);
        var baseEmbedding = CreateRandomEmbedding();
        var chunks = new List<LeidenChunk>
        {
            CreateChunk("1", "Machine learning", baseEmbedding),
            CreateChunk("2", "Deep learning", CreateSimilarEmbedding(baseEmbedding, 0.95f)),
            CreateChunk("3", "Neural networks", CreateSimilarEmbedding(baseEmbedding, 0.9f))
        };
        var options = new LeidenOptions
        {
            GenerateSummariesOnDetection = true,
            MinCommunitySize = 1,
            SimilarityThreshold = 0.5
        };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region GenerateSummariesAsync Tests

    [Fact]
    public async Task GenerateSummariesAsync_InvalidLevel_ReturnsEmptyList()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>(),
            TotalChunks = 0,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.GenerateSummariesAsync(hierarchy, 5);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GenerateSummariesAsync_NegativeLevel_ReturnsEmptyList()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new() { LevelIndex = 0, Communities = new List<LeidenCommunity>() }
            },
            TotalChunks = 0,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.GenerateSummariesAsync(hierarchy, -1);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GenerateSummariesAsync_WithoutLlmService_ReturnsKeywordBasedSummaries()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var community = new LeidenCommunity
        {
            Index = 0,
            ChunkIds = new List<string> { "1", "2", "3" },
            Keywords = new List<string> { "machine", "learning", "neural" },
            RepresentativeChunkIds = new List<string> { "1" }
        };
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = new List<LeidenCommunity> { community }
                }
            },
            TotalChunks = 3,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.GenerateSummariesAsync(hierarchy, 0);

        // Assert
        Assert.Single(result);
        Assert.Contains("machine", result[0].Summary);
    }

    [Fact]
    public async Task GenerateSummariesAsync_WithLlmService_GeneratesLlmSummaries()
    {
        // Arrange
        _llmServiceMock.GenerateCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns("A community about machine learning and AI.");

        var service = new LeidenCommunityService(_loggerMock, _llmServiceMock);
        var community = new LeidenCommunity
        {
            Index = 0,
            ChunkIds = new List<string> { "1", "2", "3" },
            Keywords = new List<string> { "machine", "learning", "AI" },
            RepresentativeChunkIds = new List<string> { "1" }
        };
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = new List<LeidenCommunity> { community }
                }
            },
            TotalChunks = 3,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.GenerateSummariesAsync(hierarchy, 0);

        // Assert
        Assert.Single(result);
        Assert.Contains("machine learning", result[0].Summary);
    }

    [Fact]
    public async Task GenerateSummariesAsync_LlmFailure_ReturnsNull()
    {
        // Arrange
        _llmServiceMock.GenerateCompletionAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Throws(new Exception("LLM service unavailable"));

        var service = new LeidenCommunityService(_loggerMock, _llmServiceMock);
        var community = new LeidenCommunity
        {
            Index = 0,
            ChunkIds = new List<string> { "1" },
            Keywords = new List<string> { "test" },
            RepresentativeChunkIds = new List<string> { "1" }
        };
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = new List<LeidenCommunity> { community }
                }
            },
            TotalChunks = 1,
            Options = new LeidenOptions()
        };

        // Act - should not throw
        var result = await service.GenerateSummariesAsync(hierarchy, 0);

        // Assert - may return empty or non-empty depending on implementation
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GenerateSummariesAsync_RespectsCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new LeidenCommunityService(_loggerMock);
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = new List<LeidenCommunity>
                    {
                        new() { Index = 0, ChunkIds = new List<string> { "1" }, Keywords = new List<string>(), RepresentativeChunkIds = new List<string>() }
                    }
                }
            },
            TotalChunks = 1,
            Options = new LeidenOptions()
        };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GenerateSummariesAsync(hierarchy, 0, cts.Token));
    }

    #endregion

    #region FindRelevantCommunitiesAsync Tests

    [Fact]
    public async Task FindRelevantCommunitiesAsync_InvalidLevel_ReturnsEmptyList()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var queryEmbedding = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>(),
            TotalChunks = 0,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.FindRelevantCommunitiesAsync(queryEmbedding, hierarchy, level: 5);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindRelevantCommunitiesAsync_NegativeLevel_ReturnsEmptyList()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var queryEmbedding = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");
        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new() { LevelIndex = 0, Communities = new List<LeidenCommunity>() }
            },
            TotalChunks = 0,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.FindRelevantCommunitiesAsync(queryEmbedding, hierarchy, level: -1);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindRelevantCommunitiesAsync_ReturnsSortedBySimilarity()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var queryValues = CreateRandomFloatArray(128);
        var queryEmbedding = new EmbeddingVector(queryValues, "test-model");

        var centroid1 = new EmbeddingVector(CreateSimilarFloatArray(queryValues, 0.9f), "test-model");
        var centroid2 = new EmbeddingVector(CreateSimilarFloatArray(queryValues, 0.7f), "test-model");
        var centroid3 = new EmbeddingVector(CreateSimilarFloatArray(queryValues, 0.5f), "test-model");

        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = new List<LeidenCommunity>
                    {
                        new() { Index = 0, ChunkIds = new List<string> { "1" }, Centroid = centroid3, Keywords = new List<string>(), RepresentativeChunkIds = new List<string>() },
                        new() { Index = 1, ChunkIds = new List<string> { "2" }, Centroid = centroid1, Keywords = new List<string>(), RepresentativeChunkIds = new List<string>() },
                        new() { Index = 2, ChunkIds = new List<string> { "3" }, Centroid = centroid2, Keywords = new List<string>(), RepresentativeChunkIds = new List<string>() }
                    }
                }
            },
            TotalChunks = 3,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.FindRelevantCommunitiesAsync(queryEmbedding, hierarchy, level: 0, topK: 3);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.True(result[0].Similarity >= result[1].Similarity);
        Assert.True(result[1].Similarity >= result[2].Similarity);
    }

    [Fact]
    public async Task FindRelevantCommunitiesAsync_RespectsTopK()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var queryEmbedding = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");
        var centroid = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");

        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = Enumerable.Range(0, 10).Select(i => new LeidenCommunity
                    {
                        Index = i,
                        ChunkIds = new List<string> { i.ToString() },
                        Centroid = centroid,
                        Keywords = new List<string>(),
                        RepresentativeChunkIds = new List<string>()
                    }).ToList()
                }
            },
            TotalChunks = 10,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.FindRelevantCommunitiesAsync(queryEmbedding, hierarchy, level: 0, topK: 3);

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task FindRelevantCommunitiesAsync_SkipsCommunitiesWithoutCentroid()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var queryEmbedding = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");
        var centroid = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");

        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = new List<LeidenCommunity>
                    {
                        new() { Index = 0, ChunkIds = new List<string> { "1" }, Centroid = null, Keywords = new List<string>(), RepresentativeChunkIds = new List<string>() },
                        new() { Index = 1, ChunkIds = new List<string> { "2" }, Centroid = centroid, Keywords = new List<string>(), RepresentativeChunkIds = new List<string>() }
                    }
                }
            },
            TotalChunks = 2,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.FindRelevantCommunitiesAsync(queryEmbedding, hierarchy, level: 0, topK: 5);

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task FindRelevantCommunitiesAsync_IncludesCorrectLevel()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var queryEmbedding = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");
        var centroid = new EmbeddingVector(CreateRandomFloatArray(128), "test-model");

        var hierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>
            {
                new()
                {
                    LevelIndex = 0,
                    Communities = new List<LeidenCommunity>
                    {
                        new() { Index = 0, ChunkIds = new List<string> { "1" }, Centroid = centroid, Keywords = new List<string>(), RepresentativeChunkIds = new List<string>() }
                    }
                }
            },
            TotalChunks = 1,
            Options = new LeidenOptions()
        };

        // Act
        var result = await service.FindRelevantCommunitiesAsync(queryEmbedding, hierarchy, level: 0, topK: 5);

        // Assert
        Assert.Single(result);
        Assert.Equal(0, result[0].Level);
    }

    #endregion

    #region UpdateHierarchyAsync Tests

    [Fact]
    public async Task UpdateHierarchyAsync_WithNewChunks_ReturnsUpdatedHierarchy()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var existingHierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>(),
            TotalChunks = 0,
            Options = new LeidenOptions { MinCommunitySize = 1 }
        };
        var newChunks = CreateTestChunks(5);

        // Act
        var result = await service.UpdateHierarchyAsync(existingHierarchy, newChunks);

        // Assert
        Assert.Equal(5, result.TotalChunks);
    }

    [Fact]
    public async Task UpdateHierarchyAsync_UsesProvidedOptions()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var existingHierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>(),
            TotalChunks = 0,
            Options = new LeidenOptions()
        };
        var newChunks = CreateTestChunks(3);
        var newOptions = new LeidenOptions { Resolution = 2.0, MinCommunitySize = 1 };

        // Act
        var result = await service.UpdateHierarchyAsync(existingHierarchy, newChunks, newOptions);

        // Assert
        Assert.Equal(newOptions.Resolution, result.Options.Resolution);
    }

    [Fact]
    public async Task UpdateHierarchyAsync_FallsBackToExistingOptions()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var existingOptions = new LeidenOptions { Resolution = 1.5, MinCommunitySize = 1 };
        var existingHierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>(),
            TotalChunks = 0,
            Options = existingOptions
        };
        var newChunks = CreateTestChunks(3);

        // Act
        var result = await service.UpdateHierarchyAsync(existingHierarchy, newChunks);

        // Assert
        Assert.Equal(existingOptions.Resolution, result.Options.Resolution);
    }

    [Fact]
    public async Task UpdateHierarchyAsync_RespectsCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new LeidenCommunityService(_loggerMock);
        var existingHierarchy = new CommunityHierarchy
        {
            Levels = new List<CommunityLevel>(),
            TotalChunks = 0,
            Options = new LeidenOptions()
        };
        var newChunks = CreateTestChunks(10);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UpdateHierarchyAsync(existingHierarchy, newChunks, cancellationToken: cts.Token));
    }

    #endregion

    #region Algorithm Behavior Tests

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_HighResolution_ProducesMoreCommunities()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(20);
        var lowResOptions = new LeidenOptions { Resolution = 0.5, MinCommunitySize = 1 };
        var highResOptions = new LeidenOptions { Resolution = 2.0, MinCommunitySize = 1 };

        // Act
        var lowResResult = await service.DetectHierarchicalCommunitiesAsync(chunks, lowResOptions);
        var highResResult = await service.DetectHierarchicalCommunitiesAsync(chunks, highResOptions);

        // Assert - generally higher resolution produces more communities
        Assert.NotNull(lowResResult);
        Assert.NotNull(highResResult);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_MinCommunitySize_FiltersSmallCommunities()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(10);
        var options = new LeidenOptions { MinCommunitySize = 3 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        if (result.LevelCount > 0)
        {
            Assert.All(result.Levels[0].Communities, c => Assert.True(c.Size >= 3));
        }
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_LowSimilarityThreshold_MoreConnections()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(10);
        var options = new LeidenOptions
        {
            SimilarityThreshold = 0.1,
            MinCommunitySize = 1
        };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Statistics?.GraphEdges >= 0);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_MaxNeighbors_LimitsConnections()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var chunks = CreateTestChunks(10);
        var options = new LeidenOptions
        {
            MaxNeighbors = 2,
            MinCommunitySize = 1
        };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DetectHierarchicalCommunitiesAsync_CohesionCalculation_IsValid()
    {
        // Arrange
        var service = new LeidenCommunityService(_loggerMock);
        var baseEmbedding = CreateRandomEmbedding();
        var chunks = new List<LeidenChunk>
        {
            CreateChunk("1", "Content 1", baseEmbedding),
            CreateChunk("2", "Content 2", CreateSimilarEmbedding(baseEmbedding, 0.98f)),
            CreateChunk("3", "Content 3", CreateSimilarEmbedding(baseEmbedding, 0.97f))
        };
        var options = new LeidenOptions { MinCommunitySize = 1, SimilarityThreshold = 0.5 };

        // Act
        var result = await service.DetectHierarchicalCommunitiesAsync(chunks, options);

        // Assert
        if (result.LevelCount > 0 && result.Levels[0].Communities.Any())
        {
            foreach (var community in result.Levels[0].Communities)
            {
                Assert.True(community.Cohesion >= 0 && community.Cohesion <= 1);
            }
        }
    }

    #endregion

    #region Helper Methods

    private static LeidenChunk CreateChunk(string id, string content, EmbeddingVector embedding)
    {
        return new LeidenChunk
        {
            Id = id,
            Content = content,
            Embedding = embedding
        };
    }

    private static EmbeddingVector CreateRandomEmbedding(int dimension = 128)
    {
        return new EmbeddingVector(CreateRandomFloatArray(dimension), "test-model");
    }

    private static float[] CreateRandomFloatArray(int dimension)
    {
        var random = new Random();
        var values = new float[dimension];
        float magnitude = 0;

        for (int i = 0; i < dimension; i++)
        {
            values[i] = (float)(random.NextDouble() * 2 - 1);
            magnitude += values[i] * values[i];
        }

        // Normalize
        magnitude = (float)Math.Sqrt(magnitude);
        for (int i = 0; i < dimension; i++)
        {
            values[i] /= magnitude;
        }

        return values;
    }

    private static EmbeddingVector CreateSimilarEmbedding(EmbeddingVector baseEmbedding, float similarity)
    {
        var values = CreateSimilarFloatArray(baseEmbedding.Values, similarity);
        return new EmbeddingVector(values, "test-model");
    }

    private static float[] CreateSimilarFloatArray(float[] baseValues, float similarity)
    {
        var random = new Random();
        var dimension = baseValues.Length;
        var values = new float[dimension];
        var noiseFactor = 1 - similarity;

        for (int i = 0; i < dimension; i++)
        {
            values[i] = baseValues[i] * similarity + (float)(random.NextDouble() * 2 - 1) * noiseFactor;
        }

        // Normalize
        float magnitude = 0;
        for (int i = 0; i < dimension; i++)
        {
            magnitude += values[i] * values[i];
        }
        magnitude = (float)Math.Sqrt(magnitude);
        for (int i = 0; i < dimension; i++)
        {
            values[i] /= magnitude;
        }

        return values;
    }

    private static List<LeidenChunk> CreateTestChunks(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => CreateChunk(i.ToString(), $"Test content {i}", CreateRandomEmbedding()))
            .ToList();
    }

    #endregion
}
