using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for RetrievalVerificationService
/// </summary>
public class RetrievalVerificationServiceTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<ITextCompletionService> _mockCompletionService;
    private readonly ILogger<RetrievalVerificationService> _logger;

    public RetrievalVerificationServiceTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockCompletionService = new Mock<ITextCompletionService>();
        _logger = NullLogger<RetrievalVerificationService>.Instance;

        _mockEmbeddingService
            .Setup(x => x.GetModelName())
            .Returns("test-model");

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f });
    }

    private RetrievalVerificationService CreateService(bool withLlm = true)
    {
        var options = new RetrievalVerificationOptions
        {
            MinRelevanceThreshold = 0.5,
            HallucinationThreshold = 0.5,
            MinEvidenceChunks = 1
        };

        return new RetrievalVerificationService(
            _mockEmbeddingService.Object,
            withLlm ? _mockCompletionService.Object : null,
            Microsoft.Extensions.Options.Options.Create(options),
            _logger);
    }

    private List<RetrievedChunk> CreateTestChunks(int count, string topic = "machine learning")
    {
        var chunks = new List<RetrievedChunk>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var embedding = new float[5];
            for (int j = 0; j < 5; j++)
            {
                embedding[j] = (float)random.NextDouble();
            }

            chunks.Add(new RetrievedChunk
            {
                ChunkId = $"chunk_{i}",
                Content = $"This is content about {topic} for chunk {i}. It contains relevant information.",
                Score = 0.8f - (i * 0.1f),
                Embedding = new EmbeddingVector(embedding, "test-model"),
                Metadata = new Dictionary<string, object> { ["source"] = $"source_{i}" }
            });
        }

        return chunks;
    }

    #region Retrieval Verification Tests

    [Fact]
    public async Task VerifyRetrievalAsync_ValidChunks_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "What is machine learning?";
        var chunks = CreateTestChunks(5, "machine learning");

        // Act
        var result = await service.VerifyRetrievalAsync(query, chunks);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task VerifyRetrievalAsync_EmptyChunks_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var query = "Test query";
        var chunks = new List<RetrievedChunk>();

        // Act
        var result = await service.VerifyRetrievalAsync(query, chunks);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Hallucination Detection Tests

    [Fact]
    public async Task CheckHallucinationAsync_SupportedContent_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var sourceChunks = CreateTestChunks(3, "machine learning");
        var generatedContent = "Machine learning is used for pattern recognition.";

        // Act
        var result = await service.CheckHallucinationAsync(generatedContent, sourceChunks);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HallucinationScore >= 0 && result.HallucinationScore <= 1);
    }

    [Fact]
    public async Task CheckHallucinationAsync_WithLLM_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: true);
        var sourceChunks = CreateTestChunks(3);
        var generatedContent = "This is generated content to verify.";

        _mockCompletionService
            .Setup(x => x.GenerateJsonCompletionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 0.3, \"unsupported_claims\": []}");

        // Act
        var result = await service.CheckHallucinationAsync(generatedContent, sourceChunks);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Factual Consistency Tests

    [Fact]
    public async Task CheckFactualConsistencyAsync_ConsistentChunks_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var chunks = CreateTestChunks(3, "consistent topic");

        // Act
        var result = await service.CheckFactualConsistencyAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ConsistencyScore >= 0 && result.ConsistencyScore <= 1);
    }

    [Fact]
    public async Task CheckFactualConsistencyAsync_SingleChunk_ReturnsMaxConsistency()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var chunks = CreateTestChunks(1);

        // Act
        var result = await service.CheckFactualConsistencyAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1.0, result.ConsistencyScore);
        Assert.Empty(result.Contradictions);
    }

    [Fact]
    public async Task CheckFactualConsistencyAsync_EmptyChunks_ReturnsMaxConsistency()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var chunks = new List<RetrievedChunk>();

        // Act
        var result = await service.CheckFactualConsistencyAsync(chunks);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1.0, result.ConsistencyScore);
    }

    #endregion

    #region Source Attribution Tests

    [Fact]
    public async Task VerifySourceAttributionAsync_ValidClaim_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var claim = "Machine learning is a powerful tool.";
        var sources = CreateTestChunks(5, "machine learning");

        // Act
        var result = await service.VerifySourceAttributionAsync(claim, sources);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task VerifySourceAttributionAsync_NoSources_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var claim = "This is a claim without sources.";
        var sources = new List<RetrievedChunk>();

        // Act
        var result = await service.VerifySourceAttributionAsync(claim, sources);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CheckHallucinationAsync_EmptyContent_ReturnsResult()
    {
        // Arrange
        var service = CreateService(withLlm: false);
        var sources = CreateTestChunks(3);
        var emptyContent = "";

        // Act
        var result = await service.CheckHallucinationAsync(emptyContent, sources);

        // Assert
        Assert.NotNull(result);
    }

    #endregion
}
