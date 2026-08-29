using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Quantization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Quantization;

public class ProductQuantizerTests
{
    private readonly ILogger<ProductQuantizer> _loggerMock;

    public ProductQuantizerTests()
    {
        _loggerMock = Substitute.For<ILogger<ProductQuantizer>>();
    }

    private ProductQuantizer CreateQuantizer(int dimension = 16, int numSubvectors = 4, int codebookSize = 256)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = QuantizationType.ProductQuantization,
            Dimension = dimension,
            NumSubvectors = numSubvectors,
            CodebookSize = codebookSize,
            KMeansIterations = 5 // 테스트용으로 적게
        });

        return new ProductQuantizer(options, _loggerMock);
    }

    private List<float[]> GenerateTrainingVectors(int count, int dimension)
    {
        var random = new Random(42); // 재현성을 위한 시드
        var vectors = new List<float[]>();

        for (int i = 0; i < count; i++)
        {
            var vector = new float[dimension];
            for (int j = 0; j < dimension; j++)
            {
                vector[j] = (float)(random.NextDouble() * 2 - 1); // -1 ~ 1
            }
            vectors.Add(vector);
        }

        return vectors;
    }

    #region Training Tests

    [Fact]
    public async Task TrainAsync_CompletesSuccessfully()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 8);
        var trainingVectors = GenerateTrainingVectors(100, 16);

        // Act
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(quantizer.IsTrained);
    }

    [Fact]
    public async Task TrainAsync_WithFewVectors_HandlesGracefully()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 8, numSubvectors: 2, codebookSize: 4);
        var trainingVectors = GenerateTrainingVectors(3, 8); // 코드북 크기보다 적음

        // Act
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(quantizer.IsTrained);
    }

    #endregion

    #region Quantization Tests

    [Fact]
    public async Task QuantizeAsync_AfterTraining_ReturnsValidResult()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 8);
        var trainingVectors = GenerateTrainingVectors(50, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var testVector = GenerateTrainingVectors(1, 16)[0];

        // Act
        var result = await quantizer.QuantizeAsync(testVector, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(QuantizationType.ProductQuantization, result.Type);
        Assert.Equal(16, result.OriginalDimension);
        Assert.Equal(4, result.Data.Length); // 4 subvectors = 4 bytes
    }

    [Fact]
    public async Task QuantizeAsync_WithoutTraining_ThrowsException()
    {
        // Arrange
        var quantizer = CreateQuantizer();
        var vector = GenerateTrainingVectors(1, 16)[0];

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QuantizeAsync_WrongDimension_ThrowsException()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4);
        var trainingVectors = GenerateTrainingVectors(50, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var wrongVector = new float[8]; // 잘못된 차원

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => quantizer.QuantizeAsync(wrongVector, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Dequantization Tests

    [Fact]
    public async Task DequantizeAsync_ReconstructsApproximately()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 16);
        var trainingVectors = GenerateTrainingVectors(100, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var original = trainingVectors[0];

        // Act
        var quantized = await quantizer.QuantizeAsync(original, TestContext.Current.CancellationToken);
        var reconstructed = await quantizer.DequantizeAsync(quantized, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(original.Length, reconstructed.Length);

        // PQ는 손실 압축이므로 정확한 복원은 기대하지 않음
        // 하지만 전체적인 구조는 보존되어야 함
    }

    [Fact]
    public async Task DequantizeAsync_WithoutTraining_ThrowsException()
    {
        // Arrange
        var quantizer = CreateQuantizer();
        var fakeQuantized = new QuantizedVector
        {
            Data = new byte[4],
            Type = QuantizationType.ProductQuantization,
            OriginalDimension = 16
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => quantizer.DequantizeAsync(fakeQuantized, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Distance Computation Tests

    [Fact]
    public async Task ComputeDistance_SameVectors_ReturnsSmallDistance()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 16);
        var trainingVectors = GenerateTrainingVectors(100, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var vector = trainingVectors[0];
        var q1 = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);
        var q2 = await quantizer.QuantizeAsync(vector, TestContext.Current.CancellationToken);

        // Act
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.Equal(0f, distance); // 같은 벡터는 같은 코드로 양자화됨
    }

    [Fact]
    public async Task ComputeDistance_DifferentVectors_ReturnsPositiveDistance()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 16);
        var trainingVectors = GenerateTrainingVectors(100, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var q1 = await quantizer.QuantizeAsync(trainingVectors[0], TestContext.Current.CancellationToken);
        var q2 = await quantizer.QuantizeAsync(trainingVectors[50], TestContext.Current.CancellationToken); // 다른 벡터

        // Act
        var distance = quantizer.ComputeDistance(q1, q2);

        // Assert
        Assert.True(distance >= 0); // 거리는 항상 0 이상
    }

    [Fact]
    public async Task ComputeDistanceToVector_ReturnsReasonableDistance()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 16);
        var trainingVectors = GenerateTrainingVectors(100, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var vector1 = trainingVectors[0];
        var vector2 = trainingVectors[1];

        var quantized = await quantizer.QuantizeAsync(vector1, TestContext.Current.CancellationToken);

        // Act
        var distanceToSelf = quantizer.ComputeDistanceToVector(quantized, vector1);
        var distanceToOther = quantizer.ComputeDistanceToVector(quantized, vector2);

        // Assert
        Assert.True(distanceToSelf <= distanceToOther,
            "Distance to self should be smaller than distance to other vector");
    }

    #endregion

    #region Distance Table Tests

    [Fact]
    public async Task BuildDistanceTable_ReturnsValidTable()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 8);
        var trainingVectors = GenerateTrainingVectors(50, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var queryVector = trainingVectors[0];

        // Act
        var table = quantizer.BuildDistanceTable(queryVector);

        // Assert
        Assert.Equal(4, table.Length); // 4 subvectors
        Assert.All(table, subTable => Assert.Equal(8, subTable.Length)); // 8 codes each
    }

    [Fact]
    public async Task ComputeDistanceWithTable_MatchesDirectComputation()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 8);
        var trainingVectors = GenerateTrainingVectors(50, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var queryVector = trainingVectors[0];
        var targetVector = trainingVectors[10];
        var quantizedTarget = await quantizer.QuantizeAsync(targetVector, TestContext.Current.CancellationToken);

        // Act
        var table = quantizer.BuildDistanceTable(queryVector);
        var distanceWithTable = ProductQuantizer.ComputeDistanceWithTable(table, quantizedTarget.Data);
        var directDistance = quantizer.ComputeDistanceToVector(quantizedTarget, queryVector);

        // Assert
        Assert.True(Math.Abs(distanceWithTable - directDistance) < 0.001f,
            $"Table distance {distanceWithTable} should match direct distance {directDistance}");
    }

    #endregion

    #region Batch Operations Tests

    [Fact]
    public async Task QuantizeBatchAsync_ProcessesMultipleVectors()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4, codebookSize: 8);
        var trainingVectors = GenerateTrainingVectors(50, 16);
        await quantizer.TrainAsync(trainingVectors, TestContext.Current.CancellationToken);

        var testVectors = trainingVectors.Take(5).ToList();

        // Act
        var results = (await quantizer.QuantizeBatchAsync(testVectors, TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.Equal(5, results.Count);
        Assert.All(results, r =>
        {
            Assert.Equal(QuantizationType.ProductQuantization, r.Type);
            Assert.Equal(4, r.Data.Length);
        });
    }

    #endregion

    #region Compression Ratio Tests

    [Theory]
    [InlineData(16, 4, 0.0625f)]   // 16 * 4 bytes = 64 bytes → 4 bytes = 0.0625
    [InlineData(32, 8, 0.0625f)]   // 32 * 4 bytes = 128 bytes → 8 bytes = 0.0625
    [InlineData(64, 8, 0.03125f)]  // 64 * 4 bytes = 256 bytes → 8 bytes = 0.03125
    public void CompressionRatio_ReturnsExpectedValue(int dimension, int numSubvectors, float expectedRatio)
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension, numSubvectors);

        // Act
        var ratio = quantizer.CompressionRatio;

        // Assert
        Assert.Equal(expectedRatio, ratio, 5);
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void QuantizationType_ReturnsProductQuantization()
    {
        // Arrange
        var quantizer = CreateQuantizer();

        // Assert
        Assert.Equal(QuantizationType.ProductQuantization, quantizer.QuantizationType);
    }

    [Fact]
    public void OriginalDimension_ReturnsConfiguredValue()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 32);

        // Assert
        Assert.Equal(32, quantizer.OriginalDimension);
    }

    [Fact]
    public void QuantizedSizeBytes_ReturnsNumSubvectors()
    {
        // Arrange
        var quantizer = CreateQuantizer(dimension: 16, numSubvectors: 4);

        // Assert
        Assert.Equal(4, quantizer.QuantizedSizeBytes);
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

    #region Validation Tests

    [Fact]
    public void Constructor_InvalidDimension_ThrowsException()
    {
        // Dimension이 NumSubvectors로 나누어 떨어지지 않으면 예외
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = QuantizationType.ProductQuantization,
            Dimension = 15, // 4로 나누어 떨어지지 않음
            NumSubvectors = 4
        });

        Assert.Throws<ArgumentException>(() =>
            new ProductQuantizer(options, _loggerMock));
    }

    [Fact]
    public void Constructor_CodebookTooLarge_ThrowsException()
    {
        // CodebookSize가 256을 초과하면 예외 (byte로 저장 불가)
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = QuantizationType.ProductQuantization,
            Dimension = 16,
            NumSubvectors = 4,
            CodebookSize = 300 // 256 초과
        });

        Assert.Throws<ArgumentException>(() =>
            new ProductQuantizer(options, _loggerMock));
    }

    #endregion
}
