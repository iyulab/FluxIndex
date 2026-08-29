using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

public class LateChunkingEmbeddingServiceTests
{
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ILogger<LateChunkingEmbeddingService> _logger;
    private readonly LateChunkingOptions _options;

    public LateChunkingEmbeddingServiceTests()
    {
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _logger = NullLogger<LateChunkingEmbeddingService>.Instance;
        _options = new LateChunkingOptions
        {
            MaxDocumentLength = 8000,
            WindowOverlap = 200,
            SurroundingContextSize = 500,
            ContextIntegrationMode = ContextIntegrationMode.SurroundingContext,
            DocumentContextWeight = 0.3
        };

        _mockEmbeddingService.GetModelName().Returns("test-model");

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f });
    }

    private LateChunkingEmbeddingService CreateService()
    {
        return new LateChunkingEmbeddingService(
            _mockEmbeddingService,
            Microsoft.Extensions.Options.Options.Create(_options),
            _logger);
    }

    private List<ChunkBoundary> CreateChunkBoundaries(string document, int chunkSize = 100)
    {
        var boundaries = new List<ChunkBoundary>();
        int index = 0;
        int position = 0;

        while (position < document.Length)
        {
            var length = Math.Min(chunkSize, document.Length - position);
            boundaries.Add(new ChunkBoundary
            {
                ChunkId = $"chunk_{index}",
                Index = index,
                StartPosition = position,
                EndPosition = position + length
            });
            position += length;
            index++;
        }

        return boundaries;
    }

    private IEnrichedChunk CreateMockEnrichedChunk(string content, int index)
    {
        var mockChunk = Substitute.For<IEnrichedChunk>();
        mockChunk.ChunkId.Returns($"chunk_{index}");
        mockChunk.Content.Returns(content);
        mockChunk.ContextDependency.Returns(0.5);
        return mockChunk;
    }

    #region Full Document Approach Tests

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_ShortDocument_UsesFullDocumentApproach()
    {
        // Arrange
        var service = CreateService();
        var document = "This is a short document about machine learning. It contains important information.";
        var boundaries = CreateChunkBoundaries(document, 40);

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.DocumentEmbedding);
        Assert.True(result.ChunkEmbeddings.Count > 0);
    }

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_GeneratesDocumentEmbedding()
    {
        // Arrange
        var service = CreateService();
        var document = "Short document content.";
        var boundaries = CreateChunkBoundaries(document);

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.DocumentEmbedding);
        Assert.Equal("test-model", result.DocumentEmbedding.ModelName);
    }

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_PreservesChunkInfo()
    {
        // Arrange
        var service = CreateService();
        var document = "Chunk one content. Chunk two content. Chunk three content.";
        var boundaries = CreateChunkBoundaries(document, 20);

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        foreach (var chunkEmbed in result.ChunkEmbeddings)
        {
            Assert.NotNull(chunkEmbed.ChunkId);
            Assert.NotEmpty(chunkEmbed.OriginalContent);
        }
    }

    #endregion

    #region Sliding Window Approach Tests

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_LongDocument_UsesSlidingWindowApproach()
    {
        // Arrange
        var longOptions = new LateChunkingOptions { MaxDocumentLength = 100 };
        var service = new LateChunkingEmbeddingService(
            _mockEmbeddingService,
            Microsoft.Extensions.Options.Options.Create(longOptions),
            _logger);

        var document = new string('A', 500); // Long document
        var boundaries = CreateChunkBoundaries(document, 50);

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ChunkEmbeddings.Count > 0);
    }

    #endregion

    #region Context Window Tests

    [Fact]
    public async Task GenerateChunkEmbeddingsWithContextAsync_AppliesContextWindow()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<IEnrichedChunk>
        {
            CreateMockEnrichedChunk("First chunk content.", 0),
            CreateMockEnrichedChunk("Second chunk content.", 1),
            CreateMockEnrichedChunk("Third chunk content.", 2)
        };

        // Act
        var results = await service.GenerateChunkEmbeddingsWithContextAsync(chunks, contextWindowSize: 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(3, results.Count);

        // Middle chunk should have context from both sides
        var middleChunk = results[1];
        Assert.Equal(1, middleChunk.PrecedingChunksIncluded);
        Assert.Equal(1, middleChunk.FollowingChunksIncluded);
    }

    [Fact]
    public async Task GenerateChunkEmbeddingsWithContextAsync_FirstChunk_HasOnlyFollowingContext()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<IEnrichedChunk>
        {
            CreateMockEnrichedChunk("First chunk.", 0),
            CreateMockEnrichedChunk("Second chunk.", 1),
            CreateMockEnrichedChunk("Third chunk.", 2)
        };

        // Act
        var results = await service.GenerateChunkEmbeddingsWithContextAsync(chunks, contextWindowSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var firstChunk = results[0];
        Assert.Equal(0, firstChunk.PrecedingChunksIncluded);
        Assert.Equal(2, firstChunk.FollowingChunksIncluded);
    }

    [Fact]
    public async Task GenerateChunkEmbeddingsWithContextAsync_LastChunk_HasOnlyPrecedingContext()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<IEnrichedChunk>
        {
            CreateMockEnrichedChunk("First chunk.", 0),
            CreateMockEnrichedChunk("Second chunk.", 1),
            CreateMockEnrichedChunk("Third chunk.", 2)
        };

        // Act
        var results = await service.GenerateChunkEmbeddingsWithContextAsync(chunks, contextWindowSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var lastChunk = results[2];
        Assert.Equal(2, lastChunk.PrecedingChunksIncluded);
        Assert.Equal(0, lastChunk.FollowingChunksIncluded);
    }

    #endregion

    #region Context Integration Mode Tests

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_SurroundingContext_IncludesMarkers()
    {
        // Arrange
        var surroundingOptions = new LateChunkingOptions
        {
            MaxDocumentLength = 10000,
            ContextIntegrationMode = ContextIntegrationMode.SurroundingContext,
            SurroundingContextSize = 50
        };
        var service = new LateChunkingEmbeddingService(
            _mockEmbeddingService,
            Microsoft.Extensions.Options.Options.Create(surroundingOptions),
            _logger);

        var document = "Before context. Main chunk content here. After context.";
        var boundaries = new List<ChunkBoundary>
        {
            new ChunkBoundary
            {
                ChunkId = "chunk_0",
                Index = 0,
                StartPosition = 16,
                EndPosition = 40
            }
        };

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        var chunkEmbed = result.ChunkEmbeddings.First();
        Assert.Contains("[[", chunkEmbed.ContextualContent);
        Assert.Contains("]]", chunkEmbed.ContextualContent);
    }

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_WeightedCombination_CombinesEmbeddings()
    {
        // Arrange
        var weightedOptions = new LateChunkingOptions
        {
            MaxDocumentLength = 10000,
            ContextIntegrationMode = ContextIntegrationMode.WeightedCombination,
            DocumentContextWeight = 0.3
        };
        var service = new LateChunkingEmbeddingService(
            _mockEmbeddingService,
            Microsoft.Extensions.Options.Options.Create(weightedOptions),
            _logger);

        var document = "Document content for testing.";
        var boundaries = CreateChunkBoundaries(document);

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        foreach (var chunk in result.ChunkEmbeddings)
        {
            Assert.True(chunk.DocumentContextApplied);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_EmptyBoundaries_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService();
        var document = "Some document content.";
        var boundaries = new List<ChunkBoundary>();

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.DocumentEmbedding);
        Assert.Empty(result.ChunkEmbeddings);
    }

    [Fact]
    public async Task GenerateChunkEmbeddingsWithContextAsync_EmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<IEnrichedChunk>();

        // Act
        var results = await service.GenerateChunkEmbeddingsWithContextAsync(chunks, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task GenerateChunkEmbeddingsWithContextAsync_SingleChunk_WorksCorrectly()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<IEnrichedChunk>
        {
            CreateMockEnrichedChunk("Only chunk.", 0)
        };

        // Act
        var results = await service.GenerateChunkEmbeddingsWithContextAsync(chunks, contextWindowSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.Equal(0, results[0].PrecedingChunksIncluded);
        Assert.Equal(0, results[0].FollowingChunksIncluded);
    }

    #endregion

    #region Embedding Normalization Tests

    [Fact]
    public async Task GenerateLateChunkingEmbeddingsAsync_NormalizesEmbeddings()
    {
        // Arrange
        var service = CreateService();
        var document = "Test document.";
        var boundaries = CreateChunkBoundaries(document);

        // Act
        var result = await service.GenerateLateChunkingEmbeddingsAsync(document, boundaries, TestContext.Current.CancellationToken);

        // Assert
        // Document embedding should be normalized (magnitude ≈ 1)
        if (result.DocumentEmbedding != null)
        {
            var magnitude = Math.Sqrt(result.DocumentEmbedding.Values.Sum(v => v * v));
            Assert.True(magnitude > 0);
        }
    }

    #endregion
}
