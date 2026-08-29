using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Quantization;

public class BinaryQuantizerTests
{
    private readonly ILogger<BinaryQuantizer> _loggerMock;

    public BinaryQuantizerTests()
    {
        _loggerMock = Substitute.For<ILogger<BinaryQuantizer>>();
    }

    private BinaryQuantizer CreateQuantizer(int dimension = 32)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = QuantizationType.Binary,
            Dimension = dimension
        });

        return new BinaryQuantizer(options, _loggerMock);
    }

    #region Quantization Tests

    [Fact]
    public async Task QuantizeAsync_CompressesToBits()
    {
        // Arrange
        var quantizer = CreateQuantizer(32);
        var vector = Enumerable.Range(0, 32)
            .Select(i => i % 2 == 0 ? 1.0f : -1.0f)
            .ToArray();

        // Act
        var result = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QuantizationType.Binary, result.Type);
        Assert.Equal(32, result.OriginalDimension);
        Assert.Equal(4, result.Data.Length); // 32 bits = 4 bytes
    }

    [Fact]
    public async Task QuantizeAsync_PositiveValues_SetBits()
    {
        // Arrange
        var quantizer = CreateQuantizer(8);
        var vector = new float[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f }; // 모두 양수

        // Act
        var result = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0xFF, result.Data[0]); // 모든 비트가 1
    }

    [Fact]
    public async Task QuantizeAsync_NegativeValues_ClearBits()
    {
        // Arrange
        var quantizer = CreateQuantizer(8);
        var vector = new float[] { -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f }; // 모두 음수

        // Act
        var result = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0x00, result.Data[0]); // 모든 비트가 0
    }

    [Fact]
    public async Task QuantizeAsync_AlternatingValues_SetAlternatingBits()
    {
        // Arrange
        var quantizer = CreateQuantizer(8);
        var vector = new float[] { 1f, -1f, 1f, -1f, 1f, -1f, 1f, -1f }; // 교대

        // Act
        var result = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0x55, result.Data[0]); // 01010101 in binary
    }

    #endregion

    #region Dequantization Tests

    [Fact]
    public async Task DequantizeAsync_ReturnsSignedValues()
    {
        // Arrange
        var quantizer = CreateQuantizer(8);
        var original = new float[] { 1f, -1f, 1f, -1f, 1f, -1f, 1f, -1f };

        // Act
        var quantized = await quantizer.QuantizeAsync(original, TestContext.Current.CancellationToken);
        var reconstructed = await quantizer.DequantizeAsync(quantized, TestContext.Current.CancellationToken);

        // Assert
        for (int i = 0; i < original.Length; i++)
        {
            var expected = original[i] > 0 ? 1.0f : -1.0f;
            Assert.Equal(expected, reconstructed[i]);
        }
    }

    [Fact]
    public async Task DequantizeAsync_PreservesSignOnly()
    {
        // Arrange
        var quantizer = CreateQuantizer(4);
        var original = new float[] { 0.1f, -0.5f, 0.9f, -0.3f }; // 다양한 크기

        // Act
        var quantized = await quantizer.QuantizeAsync(original, TestContext.Current.CancellationToken);
        var reconstructed = await quantizer.DequantizeAsync(quantized, TestContext.Current.CancellationToken);

        // Assert (크기는 보존 안됨, 부호만 보존)
        Assert.Equal(1.0f, reconstructed[0]);  // 0.1 > 0 → +1
        Assert.Equal(-1.0f, reconstructed[1]); // -0.5 < 0 → -1
        Assert.Equal(1.0f, reconstructed[2]);  // 0.9 > 0 → +1
        Assert.Equal(-1.0f, reconstructed[3]); // -0.3 < 0 → -1
    }

    #endregion

    #region Hamming Distance Tests

    [Fact]
    public async Task ComputeDistance_SameVectors_ReturnsZero()
    {
        // Arrange
        var quantizer = CreateQuantizer(32);
        var vector = Enumerable.Range(0, 32).Select(i => (float)i).ToArray();

        var q1 = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);
        var q2 = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);

        // Act
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.Equal(0f, distance);
    }

    [Fact]
    public async Task ComputeDistance_OppositeVectors_ReturnsOne()
    {
        // Arrange
        var quantizer = CreateQuantizer(32);
        var vector1 = Enumerable.Repeat(1.0f, 32).ToArray();
        var vector2 = Enumerable.Repeat(-1.0f, 32).ToArray();

        var q1 = await quantizer.QuantizeAsync(vector1, TestContext.Current.CancellationToken);
        var q2 = await quantizer.QuantizeAsync(vector2, TestContext.Current.CancellationToken);

        // Act
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.Equal(1.0f, distance); // 모든 비트가 다름
    }

    [Fact]
    public async Task ComputeDistance_HalfDifferent_ReturnsPointFive()
    {
        // Arrange
        var quantizer = CreateQuantizer(32);
        var vector1 = Enumerable.Range(0, 32).Select(i => i < 16 ? 1.0f : -1.0f).ToArray();
        var vector2 = Enumerable.Range(0, 32).Select(i => i < 16 ? -1.0f : -1.0f).ToArray();

        var q1 = await quantizer.QuantizeAsync(vector1, TestContext.Current.CancellationToken);
        var q2 = await quantizer.QuantizeAsync(vector2, TestContext.Current.CancellationToken);

        // Act
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.Equal(0.5f, distance); // 16/32 비트가 다름
    }

    [Fact]
    public async Task ComputeHammingSimilarity_ReturnsComplementOfDistance()
    {
        // Arrange
        var quantizer = CreateQuantizer(32);
        var vector1 = Enumerable.Range(0, 32).Select(i => i < 8 ? 1.0f : -1.0f).ToArray();
        var vector2 = Enumerable.Range(0, 32).Select(i => i < 8 ? -1.0f : -1.0f).ToArray();

        var q1 = await quantizer.QuantizeAsync(vector1, TestContext.Current.CancellationToken);
        var q2 = await quantizer.QuantizeAsync(vector2, TestContext.Current.CancellationToken);

        // Act
        var similarity = BinaryQuantizer.ComputeHammingSimilarity(q1, q2);
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.Equal(1.0f - distance, similarity);
    }

    #endregion

    #region Training Tests

    [Fact]
    public async Task TrainAsync_SetsMedianThresholds()
    {
        // Arrange
        var quantizer = CreateQuantizer(4);
        var trainingVectors = new List<float[]>
        {
            new[] { 1f, 2f, 3f, 4f },
            new[] { -1f, -2f, -3f, -4f },
            new[] { 0f, 0f, 0f, 0f }
        };

        // Act
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(quantizer.IsTrained);
    }

    [Fact]
    public async Task TrainAsync_EmptyVectors_UsesZeroThreshold()
    {
        // Arrange
        var quantizer = CreateQuantizer(4);

        // Act
        await quantizer.TrainAsync(new List<float[]>(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(quantizer.IsTrained);
    }

    [Fact]
    public async Task TrainAsync_AffectsQuantization()
    {
        // Arrange
        var quantizer = CreateQuantizer(4);

        // 임계값을 높게 설정하는 학습 데이터
        var trainingVectors = new List<float[]>
        {
            new[] { 10f, 10f, 10f, 10f },
            new[] { 20f, 20f, 20f, 20f },
            new[] { 30f, 30f, 30f, 30f }
        };

        // Act
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        // 중간값(20)보다 작은 값들은 0비트
        var testVector = new float[] { 15f, 15f, 15f, 15f };
        var result = await quantizer.QuantizeAsync(testVector, TestContext.Current.CancellationToken);

        // Assert
        // 모든 값이 임계값(20)보다 작으므로 모든 비트가 0
        Assert.Equal(0x00, result.Data[0]);
    }

    #endregion

    #region Compression Ratio Tests

    [Fact]
    public void CompressionRatio_Returns32xCompression()
    {
        // Arrange
        var quantizer = CreateQuantizer();

        // Assert
        Assert.Equal(1.0f / 32.0f, quantizer.CompressionRatio);
    }

    [Theory]
    [InlineData(8, 1)]    // 8 bits = 1 byte
    [InlineData(16, 2)]   // 16 bits = 2 bytes
    [InlineData(32, 4)]   // 32 bits = 4 bytes
    [InlineData(33, 5)]   // 33 bits = 5 bytes (올림)
    public void QuantizedSizeBytes_ReturnsCorrectSize(int dimension, int expectedBytes)
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension);

        // Assert
        Assert.Equal(expectedBytes, quantizer.QuantizedSizeBytes);
    }

    #endregion

    #region Batch Operations Tests

    [Fact]
    public async Task QuantizeBatchAsync_ProcessesMultipleVectors()
    {
        // Arrange
        var quantizer = CreateQuantizer(16);
        var vectors = new List<float[]>
        {
            Enumerable.Repeat(1.0f, 16).ToArray(),
            Enumerable.Repeat(-1.0f, 16).ToArray(),
            Enumerable.Range(0, 16).Select(i => (float)i).ToArray()
        };

        // Act
        var results = (await quantizer.QuantizeBatchAsync(vectors, TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(QuantizationType.Binary, r.Type));
    }

    [Fact]
    public async Task ComputeHammingDistancesBatch_ComputesAllDistances()
    {
        // Arrange
        var quantizer = CreateQuantizer(16);
        var query = Enumerable.Repeat(1.0f, 16).ToArray();
        var candidates = new List<float[]>
        {
            Enumerable.Repeat(1.0f, 16).ToArray(),
            Enumerable.Repeat(-1.0f, 16).ToArray(),
            Enumerable.Range(0, 16).Select(i => i < 8 ? 1.0f : -1.0f).ToArray()
        };

        var qQuery = await quantizer.QuantizeAsync(query, TestContext.Current.CancellationToken);
        var qCandidates = await quantizer.QuantizeBatchAsync(candidates, TestContext.Current.CancellationToken);

        // Act
        var distances = BinaryQuantizer.ComputeHammingDistancesBatch(
            qQuery.Data,
            qCandidates.Select(c => c.Data));

        // Assert
        Assert.Equal(3, distances.Length);
        Assert.Equal(0, distances[0]);  // 같은 벡터
        Assert.Equal(16, distances[1]); // 반대 벡터
        Assert.Equal(8, distances[2]);  // 절반 다른 벡터
    }

    #endregion

    #region Utility Tests

    [Fact]
    public async Task ToBinaryString_ReturnsCorrectRepresentation()
    {
        // Arrange
        var quantizer = CreateQuantizer(8);
        var vector = new float[] { 1f, -1f, 1f, -1f, 1f, -1f, 1f, -1f };

        // Act
        var quantized = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);
        var binaryString = BinaryQuantizer.ToBinaryString(quantized.Data, 8);

        // Assert
        Assert.Equal("10101010", binaryString);
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void QuantizationType_ReturnsBinary()
    {
        // Arrange
        var quantizer = CreateQuantizer();

        // Assert
        Assert.Equal(QuantizationType.Binary, quantizer.QuantizationType);
    }

    [Fact]
    public void OriginalDimension_ReturnsConfiguredValue()
    {
        // Arrange
        var quantizer = CreateQuantizer(64);

        // Assert
        Assert.Equal(64, quantizer.OriginalDimension);
    }

    [Fact]
    public void IsTrained_InitiallyFalse()
    {
        // Arrange
        var quantizer = CreateQuantizer();

        // Assert
        Assert.False(quantizer.IsTrained);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task QuantizeAsync_ThrowsOnDimensionMismatch()
    {
        // Arrange
        var quantizer = CreateQuantizer(16);
        var wrongVector = new float[8];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => quantizer.QuantizeAsync(wrongVector, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DequantizeAsync_ThrowsOnNull()
    {
        // Arrange
        var quantizer = CreateQuantizer();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => quantizer.DequantizeAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ComputeDistance_ThrowsOnSizeMismatch()
    {
        // Arrange
        var quantizer = CreateQuantizer(16);
        var q1 = await quantizer.QuantizeAsync(Enumerable.Repeat(1.0f, 16).ToArray(), TestContext.Current.CancellationToken);

        var fakeQ2 = new QuantizedVector
        {
            Data = new byte[4], // 다른 크기
            Type = QuantizationType.Binary,
            OriginalDimension = 32
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => quantizer.ComputeDistance(q1, fakeQ2));
    }

    #endregion
}
