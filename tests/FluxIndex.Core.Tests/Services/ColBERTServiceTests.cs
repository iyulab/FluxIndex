using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Tests for ColBERTService - ColBERT-style late interaction scoring.
/// </summary>
public class ColBERTServiceTests
{
    private readonly ILogger<ColBERTService> _loggerMock;
    private readonly IEmbeddingService _embeddingServiceMock;
    private readonly ColBERTService _service;

    public ColBERTServiceTests()
    {
        _loggerMock = Substitute.For<ILogger<ColBERTService>>();
        _embeddingServiceMock = Substitute.For<IEmbeddingService>();
        _service = new ColBERTService(_loggerMock, _embeddingServiceMock);
    }

    #region ComputeMaxSimScore Tests

    [Fact]
    public void ComputeMaxSimScore_ShouldReturnZero_WhenEmptyEmbeddings()
    {
        // Arrange
        var queryEmbeddings = Array.Empty<float[]>();
        var docEmbeddings = new float[][] { new float[] { 1, 0, 0 } };

        // Act
        var score = _service.ComputeMaxSimScore(queryEmbeddings, docEmbeddings);

        // Assert
        score.Should().Be(0f);
    }

    [Fact]
    public void ComputeMaxSimScore_ShouldComputeMaxSim_ForSingleQueryToken()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var docEmbeddings = new float[][]
        {
            new float[] { 1, 0, 0 },  // Exact match, sim = 1
            new float[] { 0, 1, 0 },  // Orthogonal, sim = 0
            new float[] { 0, 0, 1 }   // Orthogonal, sim = 0
        };

        // Act
        var score = _service.ComputeMaxSimScore(queryEmbeddings, docEmbeddings);

        // Assert
        // MaxSim for query token = max(1, 0, 0) = 1
        // Total score = 1
        score.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void ComputeMaxSimScore_ShouldSumMaxSimsAcrossQueryTokens()
    {
        // Arrange
        var queryEmbeddings = new float[][]
        {
            new float[] { 1, 0, 0 },  // Query token 1
            new float[] { 0, 1, 0 }   // Query token 2
        };
        var docEmbeddings = new float[][]
        {
            new float[] { 1, 0, 0 },  // Matches Q1 perfectly
            new float[] { 0, 1, 0 },  // Matches Q2 perfectly
        };

        // Act
        var score = _service.ComputeMaxSimScore(queryEmbeddings, docEmbeddings);

        // Assert
        // MaxSim for Q1 = 1, MaxSim for Q2 = 1
        // Total = 2
        score.Should().BeApproximately(2f, 0.001f);
    }

    [Fact]
    public void ComputeMaxSimScore_ShouldHandlePartialMatches()
    {
        // Arrange
        var queryEmbeddings = new float[][]
        {
            new float[] { 1, 0, 0 },
            new float[] { 0, 1, 0 },
            new float[] { 0, 0, 1 }
        };
        var docEmbeddings = new float[][]
        {
            new float[] { 0.707f, 0.707f, 0 },  // Partially matches Q1 and Q2
        };

        // Act
        var score = _service.ComputeMaxSimScore(queryEmbeddings, docEmbeddings);

        // Assert
        // Q1 max sim ≈ 0.707, Q2 max sim ≈ 0.707, Q3 max sim = 0
        score.Should().BeApproximately(0.707f * 2, 0.01f);
    }

    [Fact]
    public void ComputeMaxSimScore_ShouldHandleNormalizedEmbeddings()
    {
        // Arrange - normalized unit vectors
        var queryEmbeddings = new float[][]
        {
            new float[] { 0.6f, 0.8f, 0 }  // |v| = 1
        };
        var docEmbeddings = new float[][]
        {
            new float[] { 0.6f, 0.8f, 0 },  // Same vector, sim = 1
            new float[] { 0.8f, 0.6f, 0 },  // Different but similar
        };

        // Act
        var score = _service.ComputeMaxSimScore(queryEmbeddings, docEmbeddings);

        // Assert
        score.Should().BeApproximately(1f, 0.001f);
    }

    #endregion

    #region ComputeBatchScoresAsync Tests

    [Fact]
    public async Task ComputeBatchScoresAsync_ShouldReturnEmpty_WhenNoDocuments()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var documents = Array.Empty<ColBERTDocument>();

        // Act
        var results = await _service.ComputeBatchScoresAsync(queryEmbeddings, documents, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeBatchScoresAsync_ShouldScoreMultipleDocuments()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var documents = new ColBERTDocument[]
        {
            new() { Id = "doc1", TokenEmbeddings = new float[][] { new float[] { 1, 0, 0 } } },
            new() { Id = "doc2", TokenEmbeddings = new float[][] { new float[] { 0, 1, 0 } } },
            new() { Id = "doc3", TokenEmbeddings = new float[][] { new float[] { 0.707f, 0.707f, 0 } } },
        };

        // Act
        var results = await _service.ComputeBatchScoresAsync(queryEmbeddings, documents, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(3);
        results[0].DocumentId.Should().Be("doc1");
        results[0].Score.Should().BeApproximately(1f, 0.001f);
        results[1].DocumentId.Should().Be("doc2");
        results[1].Score.Should().BeApproximately(0f, 0.001f);
        results[2].DocumentId.Should().Be("doc3");
        results[2].Score.Should().BeApproximately(0.707f, 0.01f);
    }

    [Fact]
    public async Task ComputeBatchScoresAsync_ShouldNormalizeByQueryLength()
    {
        // Arrange
        var queryEmbeddings = new float[][]
        {
            new float[] { 1, 0, 0 },
            new float[] { 0, 1, 0 }
        };
        var documents = new ColBERTDocument[]
        {
            new() { Id = "doc1", TokenEmbeddings = new float[][] { new float[] { 1, 0, 0 }, new float[] { 0, 1, 0 } } }
        };

        var options = new ColBERTOptions { NormalizeByQueryLength = true };

        // Act
        var results = await _service.ComputeBatchScoresAsync(queryEmbeddings, documents, options, TestContext.Current.CancellationToken);

        // Assert
        results[0].Score.Should().BeApproximately(2f, 0.001f);
        results[0].NormalizedScore.Should().BeApproximately(1f, 0.001f);
        results[0].QueryTokenCount.Should().Be(2);
    }

    [Fact]
    public async Task ComputeBatchScoresAsync_ShouldTruncateEmbeddings()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0 } };
        var documents = new ColBERTDocument[]
        {
            new()
            {
                Id = "doc1",
                TokenEmbeddings = Enumerable.Range(0, 1000)
                    .Select(_ => new float[] { 1, 0 })
                    .ToArray()
            }
        };

        var options = new ColBERTOptions { MaxDocumentTokens = 100 };

        // Act
        var results = await _service.ComputeBatchScoresAsync(queryEmbeddings, documents, options, TestContext.Current.CancellationToken);

        // Assert
        results[0].DocumentTokenCount.Should().Be(100);
    }

    #endregion

    #region RankAsync Tests

    [Fact]
    public async Task RankAsync_ShouldRankByColBERTScore()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var candidates = new ColBERTCandidate[]
        {
            new() { Id = "c1", TokenEmbeddings = new float[][] { new float[] { 0, 1, 0 } } },
            new() { Id = "c2", TokenEmbeddings = new float[][] { new float[] { 1, 0, 0 } } },
            new() { Id = "c3", TokenEmbeddings = new float[][] { new float[] { 0.5f, 0.5f, 0 } } },
        };

        // Act
        var results = await _service.RankAsync(queryEmbeddings, candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(3);
        results[0].Id.Should().Be("c2"); // Best match
        results[0].NewRank.Should().Be(0);
        results[2].Id.Should().Be("c1"); // Worst match
    }

    [Fact]
    public async Task RankAsync_ShouldCombineWithInitialScore()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var candidates = new ColBERTCandidate[]
        {
            new() { Id = "c1", TokenEmbeddings = new float[][] { new float[] { 0, 1, 0 } }, InitialScore = 0.9 },
            new() { Id = "c2", TokenEmbeddings = new float[][] { new float[] { 1, 0, 0 } }, InitialScore = 0.1 },
        };

        var options = new ColBERTOptions { ColBERTWeight = 0.5f };

        // Act
        var results = await _service.RankAsync(queryEmbeddings, candidates, options, TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        // c1: (0.5 * 0.9) + (0.5 * 0) = 0.45
        // c2: (0.5 * 0.1) + (0.5 * 1) = 0.55
        results[0].Id.Should().Be("c2");
        results[0].CombinedScore.Should().BeApproximately(0.55, 0.01);
    }

    [Fact]
    public async Task RankAsync_ShouldTrackOriginalRanks()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var candidates = new ColBERTCandidate[]
        {
            new() { Id = "c1", TokenEmbeddings = new float[][] { new float[] { 0, 1, 0 } } },
            new() { Id = "c2", TokenEmbeddings = new float[][] { new float[] { 1, 0, 0 } } },
        };

        // Act
        var results = await _service.RankAsync(queryEmbeddings, candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var c2Result = results.First(r => r.Id == "c2");
        c2Result.OriginalRank.Should().Be(1);
        c2Result.NewRank.Should().Be(0);
    }

    #endregion

    #region Compression Tests

    [Fact]
    public async Task CompressAndDecompress_None_ShouldPreserveData()
    {
        // Arrange
        var embeddings = new float[][]
        {
            new float[] { 1, 2, 3 },
            new float[] { 4, 5, 6 }
        };

        var options = new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.None };

        // Act
        var compressed = await _service.CompressEmbeddingsAsync(embeddings, options, TestContext.Current.CancellationToken);
        var decompressed = await _service.DecompressEmbeddingsAsync(compressed, TestContext.Current.CancellationToken);

        // Assert
        decompressed.Should().HaveCount(2);
        decompressed[0].Should().BeEquivalentTo(embeddings[0]);
        decompressed[1].Should().BeEquivalentTo(embeddings[1]);
    }

    [Fact]
    public async Task CompressAndDecompress_Float16_ShouldApproximateData()
    {
        // Arrange
        var embeddings = new float[][]
        {
            new float[] { 1.5f, 2.5f, 3.5f },
            new float[] { 4.5f, 5.5f, 6.5f }
        };

        var options = new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.Float16 };

        // Act
        var compressed = await _service.CompressEmbeddingsAsync(embeddings, options, TestContext.Current.CancellationToken);
        var decompressed = await _service.DecompressEmbeddingsAsync(compressed, TestContext.Current.CancellationToken);

        // Assert
        compressed.Data.Length.Should().Be(embeddings.Length * embeddings[0].Length * 2);
        decompressed.Should().HaveCount(2);
        for (int i = 0; i < embeddings.Length; i++)
        {
            for (int j = 0; j < embeddings[i].Length; j++)
            {
                decompressed[i][j].Should().BeApproximately(embeddings[i][j], 0.01f);
            }
        }
    }

    [Fact]
    public async Task CompressAndDecompress_Int8_ShouldApproximateData()
    {
        // Arrange
        var embeddings = new float[][]
        {
            new float[] { -1f, 0f, 1f },
            new float[] { 0.5f, -0.5f, 0f }
        };

        var options = new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.Scalar8Bit };

        // Act
        var compressed = await _service.CompressEmbeddingsAsync(embeddings, options, TestContext.Current.CancellationToken);
        var decompressed = await _service.DecompressEmbeddingsAsync(compressed, TestContext.Current.CancellationToken);

        // Assert
        compressed.Data.Length.Should().Be(embeddings.Length * embeddings[0].Length);
        compressed.QuantizationScale.Should().NotBeNull();
        compressed.QuantizationOffset.Should().NotBeNull();

        decompressed.Should().HaveCount(2);
        for (int i = 0; i < embeddings.Length; i++)
        {
            for (int j = 0; j < embeddings[i].Length; j++)
            {
                decompressed[i][j].Should().BeApproximately(embeddings[i][j], 0.02f);
            }
        }
    }

    [Fact]
    public async Task CompressAndDecompress_Binary_ShouldPreserveSign()
    {
        // Arrange
        var embeddings = new float[][]
        {
            new float[] { 1, -1, 0.5f, -0.5f, 0.1f, -0.1f, 0f, 0f },
        };

        var options = new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.Binary };

        // Act
        var compressed = await _service.CompressEmbeddingsAsync(embeddings, options, TestContext.Current.CancellationToken);
        var decompressed = await _service.DecompressEmbeddingsAsync(compressed, TestContext.Current.CancellationToken);

        // Assert
        compressed.Data.Length.Should().Be(1); // 8 bits = 1 byte

        decompressed[0][0].Should().Be(1f);   // positive -> 1
        decompressed[0][1].Should().Be(-1f);  // negative -> -1
        decompressed[0][2].Should().Be(1f);   // positive -> 1
        decompressed[0][3].Should().Be(-1f);  // negative -> -1
    }

    [Fact]
    public async Task Compress_ShouldAchieveExpectedCompressionRatios()
    {
        // Arrange
        var embeddings = new float[][]
        {
            Enumerable.Range(0, 768).Select(i => (float)i / 768).ToArray(),
            Enumerable.Range(0, 768).Select(i => (float)(768 - i) / 768).ToArray()
        };

        // Act & Assert
        var none = await _service.CompressEmbeddingsAsync(embeddings, new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.None }, TestContext.Current.CancellationToken);
        var float16 = await _service.CompressEmbeddingsAsync(embeddings, new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.Float16 }, TestContext.Current.CancellationToken);
        var int8 = await _service.CompressEmbeddingsAsync(embeddings, new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.Scalar8Bit }, TestContext.Current.CancellationToken);
        var binary = await _service.CompressEmbeddingsAsync(embeddings, new ColBERTCompressionOptions { CompressionType = ColBERTCompressionType.Binary }, TestContext.Current.CancellationToken);

        // None: 4 bytes per float
        none.Data.Length.Should().Be(2 * 768 * 4);

        // Float16: 2 bytes per float (2x compression)
        float16.Data.Length.Should().Be(2 * 768 * 2);

        // Int8: 1 byte per float (4x compression)
        int8.Data.Length.Should().Be(2 * 768 * 1);

        // Binary: 1 bit per float (32x compression)
        binary.Data.Length.Should().Be(2 * 768 / 8);
    }

    #endregion

    #region GenerateTokenEmbeddingsAsync Tests

    [Fact]
    public async Task GenerateTokenEmbeddingsAsync_ShouldThrow_WhenNoEmbeddingService()
    {
        // Arrange
        var serviceWithoutEmbedding = new ColBERTService(_loggerMock, null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => serviceWithoutEmbedding.GenerateTokenEmbeddingsAsync("test", isQuery: true, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateTokenEmbeddingsAsync_ShouldGeneratePerTokenEmbeddings()
    {
        // Arrange
        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(callInfo => { var text = callInfo.ArgAt<string>(0); return new float[] { text.Length, 0, 0 }; });

        // Act
        var embeddings = await _service.GenerateTokenEmbeddingsAsync("hello world test", isQuery: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        embeddings.Length.Should().BeGreaterThanOrEqualTo(3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ComputeMaxSimScore_ShouldHandleHighDimensionalEmbeddings()
    {
        // Arrange
        var random = new Random(42);
        var dimension = 768;

        var queryEmbeddings = new float[][] { Enumerable.Range(0, dimension).Select(_ => (float)random.NextDouble()).ToArray() };
        var docEmbeddings = new float[][] { Enumerable.Range(0, dimension).Select(_ => (float)random.NextDouble()).ToArray() };

        // Act
        var score = _service.ComputeMaxSimScore(queryEmbeddings, docEmbeddings);

        // Assert
        score.Should().BeInRange(0f, 1f);
    }

    [Fact]
    public async Task ComputeBatchScoresAsync_ShouldHandleLargeDocumentCount()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var documents = Enumerable.Range(0, 1000)
            .Select(i => new ColBERTDocument
            {
                Id = $"doc{i}",
                TokenEmbeddings = new float[][] { new float[] { i % 2, (i + 1) % 2, 0 } }
            })
            .ToList();

        // Act
        var results = await _service.ComputeBatchScoresAsync(queryEmbeddings, documents, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(1000);
    }

    [Fact]
    public async Task RankAsync_ShouldHandleEmptyCandidates()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var candidates = Array.Empty<ColBERTCandidate>();

        // Act
        var results = await _service.RankAsync(queryEmbeddings, candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RankAsync_ShouldSkipCandidatesWithoutEmbeddings()
    {
        // Arrange
        var queryEmbeddings = new float[][] { new float[] { 1, 0, 0 } };
        var candidates = new ColBERTCandidate[]
        {
            new() { Id = "c1", TokenEmbeddings = null, Content = null },
            new() { Id = "c2", TokenEmbeddings = new float[][] { new float[] { 1, 0, 0 } } },
        };

        // Mock embedding service to not generate embeddings
        _embeddingServiceMock.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new NotImplementedException());

        // Act
        var results = await _service.RankAsync(queryEmbeddings, candidates, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("c2");
    }

    #endregion
}
