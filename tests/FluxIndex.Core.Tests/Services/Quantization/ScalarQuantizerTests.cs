using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Quantization;

public class ScalarQuantizerTests
{
    private readonly ILogger<ScalarQuantizer> _loggerMock;

    public ScalarQuantizerTests()
    {
        _loggerMock = Substitute.For<ILogger<ScalarQuantizer>>();
    }

    private ScalarQuantizer CreateQuantizer(QuantizationType type = QuantizationType.ScalarInt8, int dimension = 8)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = type,
            Dimension = dimension,
            UseSymmetricQuantization = true
        });

        return new ScalarQuantizer(options, _loggerMock);
    }

    #region Int8 Quantization Tests

    [Fact]
    public async Task QuantizeAsync_Int8_CompressesVector()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var vector = new float[] { 0.5f, -0.5f, 1.0f, -1.0f };

        // Act
        var result = await quantizer.QuantizeAsync(vector);

        // Assert
        Assert.Equal(QuantizationType.ScalarInt8, result.Type);
        Assert.Equal(4, result.OriginalDimension);
        Assert.Equal(4, result.Data.Length); // 4 bytes for 4 dimensions
    }

    [Fact]
    public async Task DequantizeAsync_Int8_ReconstructsApproximately()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var original = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };

        // Act
        var quantized = await quantizer.QuantizeAsync(original);
        var reconstructed = await quantizer.DequantizeAsync(quantized);

        // Assert
        Assert.Equal(original.Length, reconstructed.Length);

        // 양자화 오차는 작아야 함 (int8 기준 약 1/127 이내)
        for (int i = 0; i < original.Length; i++)
        {
            Assert.True(Math.Abs(original[i] - reconstructed[i]) < 0.1f,
                $"Dimension {i}: original={original[i]}, reconstructed={reconstructed[i]}");
        }
    }

    [Fact]
    public async Task QuantizeAsync_Int8_PreservesZero()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var vector = new float[] { 0f, 0f, 0f, 0f };

        // Act
        var quantized = await quantizer.QuantizeAsync(vector);
        var reconstructed = await quantizer.DequantizeAsync(quantized);

        // Assert
        foreach (var val in reconstructed)
        {
            Assert.True(Math.Abs(val) < 0.01f, $"Expected zero, got {val}");
        }
    }

    #endregion

    #region UInt8 Quantization Tests

    [Fact]
    public async Task QuantizeAsync_UInt8_CompressesVector()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarUInt8, 4);
        var vector = new float[] { 0.1f, 0.5f, 0.8f, 1.0f };

        // Act
        var result = await quantizer.QuantizeAsync(vector);

        // Assert
        Assert.Equal(QuantizationType.ScalarUInt8, result.Type);
        Assert.Equal(4, result.Data.Length);
    }

    [Fact]
    public async Task DequantizeAsync_UInt8_ReconstructsApproximately()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarUInt8, 4);
        var original = new float[] { 0.1f, 0.4f, 0.6f, 0.9f };

        // Act
        var quantized = await quantizer.QuantizeAsync(original);
        var reconstructed = await quantizer.DequantizeAsync(quantized);

        // Assert
        for (int i = 0; i < original.Length; i++)
        {
            Assert.True(Math.Abs(original[i] - reconstructed[i]) < 0.1f,
                $"Dimension {i}: original={original[i]}, reconstructed={reconstructed[i]}");
        }
    }

    #endregion

    #region Int4 Quantization Tests

    [Fact]
    public async Task QuantizeAsync_Int4_CompressesTo50Percent()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt4, 8);
        var vector = new float[] { 0.1f, -0.1f, 0.5f, -0.5f, 0.9f, -0.9f, 0.3f, -0.3f };

        // Act
        var result = await quantizer.QuantizeAsync(vector);

        // Assert
        Assert.Equal(QuantizationType.ScalarInt4, result.Type);
        Assert.Equal(4, result.Data.Length); // 8차원 / 2 = 4바이트
    }

    [Fact]
    public async Task DequantizeAsync_Int4_ReconstructsApproximately()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt4, 4);
        var original = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };

        // Act
        var quantized = await quantizer.QuantizeAsync(original);
        var reconstructed = await quantizer.DequantizeAsync(quantized);

        // Assert (Int4는 정밀도가 낮으므로 더 큰 오차 허용)
        for (int i = 0; i < original.Length; i++)
        {
            Assert.True(Math.Abs(original[i] - reconstructed[i]) < 0.3f,
                $"Dimension {i}: original={original[i]}, reconstructed={reconstructed[i]}");
        }
    }

    #endregion

    #region Compression Ratio Tests

    [Theory]
    [InlineData(QuantizationType.ScalarInt8, 0.25f)]
    [InlineData(QuantizationType.ScalarUInt8, 0.25f)]
    [InlineData(QuantizationType.ScalarInt4, 0.125f)]
    public void CompressionRatio_ReturnsExpectedValue(QuantizationType type, float expectedRatio)
    {
        // Arrange
        var quantizer = CreateQuantizer(type);

        // Assert
        Assert.Equal(expectedRatio, quantizer.CompressionRatio);
    }

    #endregion

    #region Distance Computation Tests

    [Fact]
    public async Task ComputeDistance_SameVectors_ReturnsZero()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var vector = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };

        var q1 = await quantizer.QuantizeAsync(vector);
        var q2 = await quantizer.QuantizeAsync(vector);

        // Act
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.True(distance < 0.001f, $"Same vectors should have near-zero distance, got {distance}");
    }

    [Fact]
    public async Task ComputeDistance_DifferentVectors_ReturnsPositive()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.0f, 1.0f, 0.0f, 0.0f };

        var q1 = await quantizer.QuantizeAsync(vector1);
        var q2 = await quantizer.QuantizeAsync(vector2);

        // Act
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.True(distance > 0.5f, $"Different vectors should have positive distance, got {distance}");
    }

    [Fact]
    public async Task ComputeDistanceToVector_ReturnsReasonableDistance()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.9f, 0.1f, 0.0f, 0.0f };

        var quantized = await quantizer.QuantizeAsync(vector1);

        // Act
        var distance = quantizer.ComputeDistanceToVector(quantized, vector2);

        // Assert
        Assert.True(distance < 0.5f, $"Similar vectors should have small distance, got {distance}");
    }

    #endregion

    #region Training Tests

    [Fact]
    public async Task TrainAsync_SetsGlobalParameters()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var trainingVectors = new List<float[]>
        {
            new[] { 1.0f, 2.0f, 3.0f, 4.0f },
            new[] { -1.0f, -2.0f, -3.0f, -4.0f },
            new[] { 0.5f, 1.5f, 2.5f, 3.5f }
        };

        // Act
        await quantizer.TrainAsync(trainingVectors);

        // Assert
        Assert.True(quantizer.IsTrained);
    }

    [Fact]
    public async Task TrainAsync_EmptyVectors_StillMarksAsTrained()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var emptyVectors = new List<float[]>();

        // Act
        await quantizer.TrainAsync(emptyVectors);

        // Assert
        Assert.True(quantizer.IsTrained);
    }

    #endregion

    #region Batch Operations Tests

    [Fact]
    public async Task QuantizeBatchAsync_ProcessesMultipleVectors()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var vectors = new List<float[]>
        {
            new[] { 0.1f, 0.2f, 0.3f, 0.4f },
            new[] { 0.5f, 0.6f, 0.7f, 0.8f },
            new[] { -0.1f, -0.2f, -0.3f, -0.4f }
        };

        // Act
        var results = (await quantizer.QuantizeBatchAsync(vectors)).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(QuantizationType.ScalarInt8, r.Type));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task QuantizeAsync_ThrowsOnNullVector()
    {
        // Arrange
        var quantizer = CreateQuantizer();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => quantizer.QuantizeAsync(null!));
    }

    [Fact]
    public async Task QuantizeAsync_ThrowsOnDimensionMismatch()
    {
        // Arrange
        var quantizer = CreateQuantizer(QuantizationType.ScalarInt8, 4);
        var wrongDimension = new float[] { 1.0f, 2.0f }; // 2차원 instead of 4

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => quantizer.QuantizeAsync(wrongDimension));
    }

    [Fact]
    public async Task DequantizeAsync_ThrowsOnNull()
    {
        // Arrange
        var quantizer = CreateQuantizer();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => quantizer.DequantizeAsync(null!));
    }

    #endregion
}
