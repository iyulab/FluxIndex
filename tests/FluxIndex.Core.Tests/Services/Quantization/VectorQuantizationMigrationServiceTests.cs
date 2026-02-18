using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Quantization;

/// <summary>
/// VectorQuantizationMigrationService 단위 테스트
/// </summary>
public class VectorQuantizationMigrationServiceTests
{
    private readonly IVectorStore _vectorStoreMock;
    private readonly IVectorQuantizer _quantizerMock;
    private readonly ILogger<VectorQuantizationMigrationService> _loggerMock;

    public VectorQuantizationMigrationServiceTests()
    {
        _vectorStoreMock = Substitute.For<IVectorStore>();
        _quantizerMock = Substitute.For<IVectorQuantizer>();
        _loggerMock = Substitute.For<ILogger<VectorQuantizationMigrationService>>();

        // 기본 설정
        _quantizerMock.OriginalDimension.Returns(128);
        _quantizerMock.QuantizationType.Returns(QuantizationType.ScalarInt8);
    }

    private VectorQuantizationMigrationService CreateService()
    {
        return new VectorQuantizationMigrationService(
            _vectorStoreMock,
            _quantizerMock,
            _loggerMock);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullVectorStore_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new VectorQuantizationMigrationService(
                null!,
                _quantizerMock,
                _loggerMock));
    }

    [Fact]
    public void Constructor_WithNullQuantizer_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new VectorQuantizationMigrationService(
                _vectorStoreMock,
                null!,
                _loggerMock));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new VectorQuantizationMigrationService(
                _vectorStoreMock,
                _quantizerMock,
                null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region AnalyzeQuantizationAsync Tests

    [Fact]
    public async Task AnalyzeQuantizationAsync_WithEmptyVectors_ReturnsEmptyResult()
    {
        // Arrange
        var service = CreateService();
        var vectors = new List<float[]>();

        // Act
        var result = await service.AnalyzeQuantizationAsync(vectors);

        // Assert
        Assert.Equal(0, result.VectorCount);
        Assert.Equal(0, result.Dimension);
        Assert.Equal(0, result.OriginalSizeBytes);
    }

    [Fact]
    public async Task AnalyzeQuantizationAsync_WithValidVectors_ReturnsAnalysis()
    {
        // Arrange
        var service = CreateService();
        const int dimension = 128;
        const int vectorCount = 10;

        var vectors = Enumerable.Range(0, vectorCount)
            .Select(_ => GenerateRandomVector(dimension))
            .ToList();

        var quantizedData = new byte[dimension]; // Int8: 1 byte per dimension
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = quantizedData,
            OriginalDimension = dimension
        };

        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(quantizedVector);

        _quantizerMock.DequantizeAsync(Arg.Any<QuantizedVector>(), Arg.Any<CancellationToken>()).Returns(callInfo => { var qv = callInfo.ArgAt<QuantizedVector>(0); return new float[qv.OriginalDimension]; });

        // Act
        var result = await service.AnalyzeQuantizationAsync(vectors);

        // Assert
        Assert.Equal(vectorCount, result.VectorCount);
        Assert.Equal(dimension, result.Dimension);
        Assert.Equal(vectorCount * dimension * sizeof(float), result.OriginalSizeBytes);
        Assert.Equal(vectorCount * dimension, result.QuantizedSizeBytes); // Int8: 1 byte per dimension
        Assert.True(result.CompressionRatio < 1.0); // Should compress
        Assert.Equal(QuantizationType.ScalarInt8, result.QuantizationType);
    }

    [Fact]
    public async Task AnalyzeQuantizationAsync_CalculatesCompressionRatio()
    {
        // Arrange
        var service = CreateService();
        const int dimension = 128;

        var vectors = new List<float[]> { GenerateRandomVector(dimension) };

        // Binary quantization: 1 bit per dimension
        var quantizedData = new byte[dimension / 8];
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.Binary,
            Data = quantizedData,
            OriginalDimension = dimension
        };

        _quantizerMock.QuantizationType.Returns(QuantizationType.Binary);
        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(quantizedVector);

        _quantizerMock.DequantizeAsync(Arg.Any<QuantizedVector>(), Arg.Any<CancellationToken>()).Returns(callInfo => { var qv = callInfo.ArgAt<QuantizedVector>(0); return new float[qv.OriginalDimension]; });

        // Act
        var result = await service.AnalyzeQuantizationAsync(vectors);

        // Assert
        var expectedOriginalSize = dimension * sizeof(float); // 512 bytes
        var expectedQuantizedSize = dimension / 8; // 16 bytes
        var expectedRatio = (double)expectedQuantizedSize / expectedOriginalSize; // ~0.03125

        Assert.Equal(expectedOriginalSize, result.OriginalSizeBytes);
        Assert.Equal(expectedQuantizedSize, result.QuantizedSizeBytes);
        Assert.Equal(expectedRatio, result.CompressionRatio, 4);
    }

    #endregion

    #region MigrateAllAsync Tests

    [Fact]
    public async Task MigrateAllAsync_WithNoChunks_ReturnsSuccessResult()
    {
        // Arrange
        var service = CreateService();

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(Enumerable.Empty<DocumentChunk>());

        // Act
        var result = await service.MigrateAllAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public async Task MigrateAllAsync_WithChunks_QuantizesAll()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(5, 128);
        var quantizedVector = CreateQuantizedVector(128);
        var callCount = 0;

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                // 첫 번째 호출에만 청크 반환
                return callCount == 1 ? chunks : Enumerable.Empty<DocumentChunk>();
            });

        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(quantizedVector);

        // Act
        var result = await service.MigrateAllAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        await _quantizerMock.Received(5).QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateAllAsync_WithNullEmbeddings_SkipsChunks()
    {
        // Arrange
        var service = CreateService();
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk { Id = "1", DocumentId = "doc1", Content = "Test", Embedding = null },
            new DocumentChunk { Id = "2", DocumentId = "doc1", Content = "Test", Embedding = null }
        };
        var callCount = 0;

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                return callCount == 1 ? chunks : Enumerable.Empty<DocumentChunk>();
            });

        // Act
        var result = await service.MigrateAllAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(2, result.SkippedCount);
        await _quantizerMock.DidNotReceive().QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateAllAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(3, 128);
        var quantizedVector = CreateQuantizedVector(128);
        var progressReports = new List<MigrationProgress>();
        var progress = new Progress<MigrationProgress>(p => progressReports.Add(p));
        var callCount = 0;

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                return callCount == 1 ? chunks : Enumerable.Empty<DocumentChunk>();
            });

        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(quantizedVector);

        // Act
        var result = await service.MigrateAllAsync(progress: progress);

        // Assert
        await Task.Delay(100); // Progress 콜백 대기
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(progressReports);
        Assert.Equal(3, progressReports.Last().SuccessCount);
    }

    [Fact]
    public async Task MigrateAllAsync_WithContinueOnError_ContinuesAfterFailure()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(3, 128);
        var quantizedVector = CreateQuantizedVector(128);
        var failedOnSecond = false;
        var callCount = 0;

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                return callCount == 1 ? chunks : Enumerable.Empty<DocumentChunk>();
            });

        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(callInfo => {
                if (!failedOnSecond)
                {
                    failedOnSecond = true;
                    throw new InvalidOperationException("Test error");
                }
                return quantizedVector;
            });

        var options = new MigrationOptions { ContinueOnError = true };

        // Act
        var result = await service.MigrateAllAsync(options);

        // Assert
        Assert.False(result.IsSuccess); // HasFailures
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
    }

    [Fact]
    public async Task MigrateAllAsync_TracksCompression()
    {
        // Arrange
        var service = CreateService();
        const int dimension = 128;
        var chunks = CreateTestChunks(1, dimension);
        var quantizedData = new byte[dimension]; // Int8: 1 byte per dimension
        var quantizedVector = new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = quantizedData,
            OriginalDimension = dimension
        };
        var callCount = 0;

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                return callCount == 1 ? chunks : Enumerable.Empty<DocumentChunk>();
            });

        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(quantizedVector);

        // Act
        var result = await service.MigrateAllAsync();

        // Assert
        var expectedOriginal = dimension * sizeof(float); // 512 bytes
        var expectedQuantized = dimension; // 128 bytes (Int8)

        Assert.Equal(expectedOriginal, result.TotalBytesOriginal);
        Assert.Equal(expectedQuantized, result.TotalBytesQuantized);
        Assert.Equal(0.25, result.CompressionRatio, 2);
    }

    #endregion

    #region MigrateByDocumentIdsAsync Tests

    [Fact]
    public async Task MigrateByDocumentIdsAsync_WithEmptyIds_ReturnsSuccessResult()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.MigrateByDocumentIdsAsync(Enumerable.Empty<string>());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.SuccessCount);
    }

    [Fact]
    public async Task MigrateByDocumentIdsAsync_FiltersCorrectDocuments()
    {
        // Arrange
        var service = CreateService();
        const int dimension = 128;

        var allChunks = new List<DocumentChunk>
        {
            new DocumentChunk { Id = "1", DocumentId = "doc1", Content = "Test1", Embedding = GenerateRandomVector(dimension) },
            new DocumentChunk { Id = "2", DocumentId = "doc1", Content = "Test2", Embedding = GenerateRandomVector(dimension) },
            new DocumentChunk { Id = "3", DocumentId = "doc2", Content = "Test3", Embedding = GenerateRandomVector(dimension) },
            new DocumentChunk { Id = "4", DocumentId = "doc3", Content = "Test4", Embedding = GenerateRandomVector(dimension) }
        };

        var quantizedVector = CreateQuantizedVector(dimension);

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(allChunks);

        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(quantizedVector);

        // Act - doc1만 마이그레이션
        var result = await service.MigrateByDocumentIdsAsync(new[] { "doc1" });

        // Assert - doc1의 청크 2개만 처리
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.SuccessCount);
    }

    #endregion

    #region CancellationToken Tests

    [Fact]
    public async Task MigrateAllAsync_WithCancellation_StopsEarly()
    {
        // Arrange
        var service = CreateService();
        var chunks = CreateTestChunks(100, 128);
        var quantizedVector = CreateQuantizedVector(128);
        var cts = new CancellationTokenSource();
        var quantizeCallCount = 0;
        var callCount = 0;

        _vectorStoreMock.SearchAsync(
                Arg.Any<float[]>(),
                Arg.Any<int>(),
                Arg.Any<float>(),
                Arg.Any<CancellationToken>()).Returns(callInfo => {
                callCount++;
                return callCount == 1 ? chunks : Enumerable.Empty<DocumentChunk>();
            });

        _quantizerMock.QuantizeAsync(Arg.Any<float[]>(), Arg.Any<CancellationToken>()).Returns(callInfo => {
                quantizeCallCount++;
                if (quantizeCallCount >= 10)
                {
                    cts.Cancel();
                }
                return quantizedVector;
            });

        // Act
        var result = await service.MigrateAllAsync(cancellationToken: cts.Token);

        // Assert - 취소 전에 처리된 양만 확인
        Assert.True(result.SuccessCount <= quantizeCallCount);
    }

    #endregion

    #region Helper Methods

    private static float[] GenerateRandomVector(int dimension)
    {
        var random = new Random(42);
        var vector = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
        }
        return vector;
    }

    private static List<DocumentChunk> CreateTestChunks(int count, int dimension)
    {
        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < count; i++)
        {
            chunks.Add(new DocumentChunk
            {
                Id = $"chunk-{i}",
                DocumentId = $"doc-{i}",
                Content = $"Test content {i}",
                Embedding = GenerateRandomVector(dimension)
            });
        }
        return chunks;
    }

    private static QuantizedVector CreateQuantizedVector(int dimension)
    {
        return new QuantizedVector
        {
            Type = QuantizationType.ScalarInt8,
            Data = new byte[dimension],
            OriginalDimension = dimension
        };
    }

    #endregion
}
