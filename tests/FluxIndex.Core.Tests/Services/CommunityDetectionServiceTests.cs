using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for CommunityDetectionService
/// </summary>
public class CommunityDetectionServiceTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly ILogger<CommunityDetectionService> _logger;

    public CommunityDetectionServiceTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _logger = NullLogger<CommunityDetectionService>.Instance;

        _mockEmbeddingService
            .Setup(x => x.GetModelName())
            .Returns("test-model");
    }

    private CommunityDetectionService CreateService()
    {
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.KMeans,
            NumClusters = 3,
            MaxIterations = 100
        };

        return new CommunityDetectionService(
            _mockEmbeddingService.Object,
            null, // IGraphTraversalService
            null, // ITextCompletionService
            Microsoft.Extensions.Options.Options.Create(options),
            _logger);
    }

    private List<ChunkWithEmbedding> CreateTestChunks(int count)
    {
        var chunks = new List<ChunkWithEmbedding>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var embedding = new float[10];
            for (int j = 0; j < 10; j++)
            {
                embedding[j] = (float)random.NextDouble();
            }

            chunks.Add(new ChunkWithEmbedding
            {
                ChunkId = $"chunk_{i}",
                Content = $"Test content for chunk {i}",
                Embedding = new EmbeddingVector(embedding, "test-model")
            });
        }

        return chunks;
    }

    #region K-Means Clustering Tests

    [Fact]
    public async Task DetectCommunitiesAsync_WithKMeans_DetectsCommunities()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(10);
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.KMeans,
            NumClusters = 3
        };

        // Act
        var result = await service.DetectCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Communities.Count > 0);
        Assert.True(result.Communities.Count <= 3);
        Assert.Equal(ClusteringAlgorithm.KMeans, result.Algorithm);
    }

    [Fact]
    public async Task DetectCommunitiesAsync_KMeans_AssignsAllChunks()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(15);
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.KMeans,
            NumClusters = 4
        };

        // Act
        var result = await service.DetectCommunitiesAsync(chunks, options);

        // Assert
        var totalAssigned = result.Communities.Sum(c => c.ChunkIds.Count);
        Assert.Equal(chunks.Count, totalAssigned);
    }

    #endregion

    #region DBSCAN Clustering Tests

    [Fact]
    public async Task DetectCommunitiesAsync_WithDBSCAN_DetectsCommunities()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(12);
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.DBSCAN,
            Epsilon = 0.5,
            MinPoints = 2
        };

        // Act
        var result = await service.DetectCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ClusteringAlgorithm.DBSCAN, result.Algorithm);
    }

    #endregion

    #region Hierarchical Clustering Tests

    [Fact]
    public async Task DetectCommunitiesAsync_WithHierarchical_DetectsCommunities()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(8);
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.Hierarchical,
            NumClusters = 2
        };

        // Act
        var result = await service.DetectCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ClusteringAlgorithm.Hierarchical, result.Algorithm);
        Assert.True(result.Communities.Count <= 2);
    }

    #endregion

    #region Label Propagation Tests

    [Fact]
    public async Task DetectCommunitiesAsync_WithLabelPropagation_DetectsCommunities()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(10);
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.LabelPropagation
        };

        // Act
        var result = await service.DetectCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ClusteringAlgorithm.LabelPropagation, result.Algorithm);
        Assert.True(result.Communities.Count >= 1);
    }

    #endregion

    #region Community Merging Tests

    [Fact]
    public async Task MergeCommunitiesAsync_MultipleCommunities_CanMerge()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(12);
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.KMeans,
            NumClusters = 4
        };

        var detection = await service.DetectCommunitiesAsync(chunks, options);

        // Act
        var merged = await service.MergeCommunitiesAsync(
            detection.Communities,
            similarityThreshold: 0.3);

        // Assert
        Assert.NotNull(merged);
        Assert.True(merged.Count <= detection.Communities.Count);
    }

    #endregion

    #region Find Best Community Tests

    [Fact]
    public async Task FindBestCommunityAsync_WithQuery_FindsRelevantCommunity()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(10);
        var queryEmbedding = new EmbeddingVector(
            new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f },
            "test-model");

        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.KMeans,
            NumClusters = 3
        };

        var detection = await service.DetectCommunitiesAsync(chunks, options);

        // Act
        var bestCommunity = await service.FindBestCommunityAsync(
            queryEmbedding,
            detection.Communities);

        // Assert
        Assert.NotNull(bestCommunity);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task DetectCommunitiesAsync_EmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<ChunkWithEmbedding>();

        // Act
        var result = await service.DetectCommunitiesAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Communities);
    }

    [Fact]
    public async Task DetectCommunitiesAsync_SingleChunk_CreatesSingleCommunity()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(1);
        var options = new CommunityDetectionOptions
        {
            Algorithm = ClusteringAlgorithm.KMeans,
            NumClusters = 3
        };

        // Act
        var result = await service.DetectCommunitiesAsync(chunks, options);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Communities);
    }

    #endregion
}
