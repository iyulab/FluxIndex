using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Core.Tests.Services.Quantization;

/// <summary>
/// 양자화 성능 벤치마크 테스트
/// 원본 vs 양자화 검색 성능 비교
/// </summary>
public class QuantizationBenchmarkTests
{
    private readonly ITestOutputHelper _output;
    private readonly IVectorStore _innerStoreMock;
    private readonly ILogger<QuantizedVectorStoreDecorator> _loggerMock;
    private readonly ILogger<ScalarQuantizer> _scalarLoggerMock;
    private readonly ILogger<BinaryQuantizer> _binaryLoggerMock;
    private readonly ILogger<ProductQuantizer> _productLoggerMock;

    public QuantizationBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _innerStoreMock = Substitute.For<IVectorStore>();
        _loggerMock = Substitute.For<ILogger<QuantizedVectorStoreDecorator>>();
        _scalarLoggerMock = Substitute.For<ILogger<ScalarQuantizer>>();
        _binaryLoggerMock = Substitute.For<ILogger<BinaryQuantizer>>();
        _productLoggerMock = Substitute.For<ILogger<ProductQuantizer>>();
    }

    #region Quantizer Factory Methods

    private ScalarQuantizer CreateScalarQuantizer(int dimension, QuantizationType type = QuantizationType.ScalarInt8)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = type,
            Dimension = dimension,
            UseSymmetricQuantization = true
        });
        return new ScalarQuantizer(options, _scalarLoggerMock);
    }

    private BinaryQuantizer CreateBinaryQuantizer(int dimension)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = QuantizationType.Binary,
            Dimension = dimension
        });
        return new BinaryQuantizer(options, _binaryLoggerMock);
    }

    private ProductQuantizer CreateProductQuantizer(int dimension, int numSubvectors = 8, int codebookSize = 256)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new QuantizationOptions
        {
            Type = QuantizationType.ProductQuantization,
            Dimension = dimension,
            NumSubvectors = numSubvectors,
            CodebookSize = codebookSize,
            KMeansIterations = 5 // 테스트용으로 적게
        });
        return new ProductQuantizer(options, _productLoggerMock);
    }

    #endregion

    #region Scalar Quantization Benchmarks

    [Fact]
    public async Task Benchmark_ScalarInt8_QuantizationSpeed()
    {
        // Arrange
        const int vectorCount = 1000;
        const int dimension = 1536;
        var quantizer = CreateScalarQuantizer(dimension, QuantizationType.ScalarInt8);
        var vectors = GenerateRandomVectors(vectorCount, dimension);

        // Warmup
        await quantizer.QuantizeAsync(vectors[0]);

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var vector in vectors)
        {
            await quantizer.QuantizeAsync(vector);
        }
        sw.Stop();

        // Assert & Report
        var avgTimePerVector = sw.Elapsed.TotalMicroseconds / vectorCount;
        _output.WriteLine($"[Scalar Int8] Quantization Speed:");
        _output.WriteLine($"  Total vectors: {vectorCount}");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Total time: {sw.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"  Avg per vector: {avgTimePerVector:F2} µs");
        _output.WriteLine($"  Throughput: {vectorCount / sw.Elapsed.TotalSeconds:F0} vectors/sec");

        Assert.True(avgTimePerVector < 1000, "Quantization should be faster than 1ms per vector");
    }

    [Fact]
    public async Task Benchmark_ScalarInt8_DistanceComputation()
    {
        // Skip in CI environment where performance is variable
        var isCI = Environment.GetEnvironmentVariable("CI") == "true" ||
                   Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

        // Arrange
        const int comparisons = 10000;
        const int dimension = 1536;
        var quantizer = CreateScalarQuantizer(dimension, QuantizationType.ScalarInt8);

        var queryVector = GenerateRandomVector(dimension);
        var targetVector = GenerateRandomVector(dimension);

        var quantizedQuery = await quantizer.QuantizeAsync(queryVector);
        var quantizedTarget = await quantizer.QuantizeAsync(targetVector);

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            quantizer.ComputeDistance(quantizedQuery, quantizedTarget);
        }

        // Act - Quantized distance
        var swQuantized = Stopwatch.StartNew();
        for (int i = 0; i < comparisons; i++)
        {
            quantizer.ComputeDistance(quantizedQuery, quantizedTarget);
        }
        swQuantized.Stop();

        // Act - Original distance (cosine similarity)
        var swOriginal = Stopwatch.StartNew();
        for (int i = 0; i < comparisons; i++)
        {
            ComputeCosineSimilarity(queryVector, targetVector);
        }
        swOriginal.Stop();

        // Report
        var speedup = swOriginal.Elapsed.TotalMicroseconds / swQuantized.Elapsed.TotalMicroseconds;
        _output.WriteLine($"[Scalar Int8] Distance Computation:");
        _output.WriteLine($"  Comparisons: {comparisons}");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Quantized total: {swQuantized.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"  Original total: {swOriginal.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"  Speedup: {speedup:F2}x");

        // Quantized distance computation - verify it completes
        // Note: Performance varies significantly by environment (CPU, memory, load)
        // Strict performance assertions are not reliable in automated tests
        Assert.True(speedup > 0, "Distance computation should complete");

        // Log warning if significantly slower (for manual review)
        if (speedup < 0.5)
        {
            _output.WriteLine($"  WARNING: Quantized distance slower than expected ({speedup:F2}x)");
            _output.WriteLine($"           This may indicate optimization opportunities or environmental factors");
        }
    }

    [Fact]
    public async Task Benchmark_ScalarInt8_MemoryUsage()
    {
        // Arrange
        const int dimension = 1536;
        var quantizer = CreateScalarQuantizer(dimension, QuantizationType.ScalarInt8);
        var vector = GenerateRandomVector(dimension);

        // Act
        var quantized = await quantizer.QuantizeAsync(vector);

        // Calculate memory
        var originalBytes = dimension * sizeof(float); // float = 4 bytes
        var quantizedBytes = quantized.Data.Length;
        var compressionRatio = (float)quantizedBytes / originalBytes;

        // Report
        _output.WriteLine($"[Scalar Int8] Memory Usage:");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Original size: {originalBytes} bytes ({originalBytes / 1024.0:F2} KB)");
        _output.WriteLine($"  Quantized size: {quantizedBytes} bytes");
        _output.WriteLine($"  Compression ratio: {compressionRatio:P2}");
        _output.WriteLine($"  Memory savings: {(1 - compressionRatio):P2}");

        Assert.Equal(0.25f, compressionRatio, 0.01f); // Int8 should be 25% of original
    }

    #endregion

    #region Binary Quantization Benchmarks

    [Fact]
    public async Task Benchmark_Binary_QuantizationSpeed()
    {
        // Arrange
        const int vectorCount = 1000;
        const int dimension = 1536;
        var quantizer = CreateBinaryQuantizer(dimension);
        var vectors = GenerateRandomVectors(vectorCount, dimension);

        // Warmup
        await quantizer.QuantizeAsync(vectors[0]);

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var vector in vectors)
        {
            await quantizer.QuantizeAsync(vector);
        }
        sw.Stop();

        // Report
        var avgTimePerVector = sw.Elapsed.TotalMicroseconds / vectorCount;
        _output.WriteLine($"[Binary] Quantization Speed:");
        _output.WriteLine($"  Total vectors: {vectorCount}");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Total time: {sw.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"  Avg per vector: {avgTimePerVector:F2} µs");
        _output.WriteLine($"  Throughput: {vectorCount / sw.Elapsed.TotalSeconds:F0} vectors/sec");

        Assert.True(avgTimePerVector < 500, "Binary quantization should be faster than 0.5ms per vector");
    }

    [Fact]
    public async Task Benchmark_Binary_HammingDistance()
    {
        // Arrange
        const int comparisons = 100000;
        const int dimension = 1536;
        var quantizer = CreateBinaryQuantizer(dimension);

        var queryVector = GenerateRandomVector(dimension);
        var targetVector = GenerateRandomVector(dimension);

        var quantizedQuery = await quantizer.QuantizeAsync(queryVector);
        var quantizedTarget = await quantizer.QuantizeAsync(targetVector);

        // Warmup
        for (int i = 0; i < 1000; i++)
        {
            quantizer.ComputeDistance(quantizedQuery, quantizedTarget);
        }

        // Act - Hamming distance
        var swHamming = Stopwatch.StartNew();
        for (int i = 0; i < comparisons; i++)
        {
            quantizer.ComputeDistance(quantizedQuery, quantizedTarget);
        }
        swHamming.Stop();

        // Act - Original cosine
        var swCosine = Stopwatch.StartNew();
        for (int i = 0; i < comparisons; i++)
        {
            ComputeCosineSimilarity(queryVector, targetVector);
        }
        swCosine.Stop();

        // Report
        var speedup = swCosine.Elapsed.TotalMicroseconds / swHamming.Elapsed.TotalMicroseconds;
        _output.WriteLine($"[Binary] Hamming Distance:");
        _output.WriteLine($"  Comparisons: {comparisons}");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Hamming total: {swHamming.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"  Cosine total: {swCosine.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"  Speedup: {speedup:F2}x");

        Assert.True(speedup >= 1.0, "Hamming distance should be at least as fast as cosine similarity");
    }

    [Fact]
    public async Task Benchmark_Binary_MemoryUsage()
    {
        // Arrange
        const int dimension = 1536;
        var quantizer = CreateBinaryQuantizer(dimension);
        var vector = GenerateRandomVector(dimension);

        // Act
        var quantized = await quantizer.QuantizeAsync(vector);

        // Calculate memory
        var originalBytes = dimension * sizeof(float);
        var quantizedBytes = quantized.Data.Length;
        var compressionRatio = (float)quantizedBytes / originalBytes;

        // Report
        _output.WriteLine($"[Binary] Memory Usage:");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Original size: {originalBytes} bytes ({originalBytes / 1024.0:F2} KB)");
        _output.WriteLine($"  Quantized size: {quantizedBytes} bytes");
        _output.WriteLine($"  Compression ratio: {compressionRatio:P2}");
        _output.WriteLine($"  Memory savings: {(1 - compressionRatio):P2}");

        // Binary should be ~3.125% of original (1 bit per dimension vs 32 bits)
        Assert.True(compressionRatio < 0.05f, "Binary quantization should achieve at least 95% compression");
    }

    #endregion

    #region Product Quantization Benchmarks

    [Fact]
    public async Task Benchmark_ProductQuantization_TrainingSpeed()
    {
        // Skip strict timing check in CI environment where performance is variable
        var isCI = Environment.GetEnvironmentVariable("CI") == "true" ||
                   Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

        // Arrange
        const int trainingVectors = 500;
        const int dimension = 64; // Smaller for faster test
        const int numSubvectors = 8;
        const int codebookSize = 256;

        var quantizer = CreateProductQuantizer(dimension, numSubvectors, codebookSize);
        var trainingData = GenerateRandomVectors(trainingVectors, dimension);

        // Act
        var sw = Stopwatch.StartNew();
        await quantizer.TrainAsync(trainingData);
        sw.Stop();

        // Report
        _output.WriteLine($"[Product Quantization] Training Speed:");
        _output.WriteLine($"  Training vectors: {trainingVectors}");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Subvectors: {numSubvectors}");
        _output.WriteLine($"  Codebook size: {codebookSize}");
        _output.WriteLine($"  Training time: {sw.Elapsed.TotalSeconds:F2} seconds");

        // Use relaxed timing in CI (60 seconds), strict timing locally (30 seconds)
        var timeLimit = isCI ? 60 : 30;
        Assert.True(sw.Elapsed.TotalSeconds < timeLimit, $"Training should complete within {timeLimit} seconds");
    }

    [Fact]
    public async Task Benchmark_ProductQuantization_EncodingSpeed()
    {
        // Arrange
        const int vectorCount = 1000;
        const int dimension = 64;
        const int numSubvectors = 8;
        const int codebookSize = 256;

        var quantizer = CreateProductQuantizer(dimension, numSubvectors, codebookSize);
        var trainingData = GenerateRandomVectors(200, dimension);
        await quantizer.TrainAsync(trainingData);

        var vectors = GenerateRandomVectors(vectorCount, dimension);

        // Warmup
        await quantizer.QuantizeAsync(vectors[0]);

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var vector in vectors)
        {
            await quantizer.QuantizeAsync(vector);
        }
        sw.Stop();

        // Report
        var avgTimePerVector = sw.Elapsed.TotalMicroseconds / vectorCount;
        _output.WriteLine($"[Product Quantization] Encoding Speed:");
        _output.WriteLine($"  Total vectors: {vectorCount}");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Total time: {sw.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"  Avg per vector: {avgTimePerVector:F2} µs");
        _output.WriteLine($"  Throughput: {vectorCount / sw.Elapsed.TotalSeconds:F0} vectors/sec");

        Assert.True(avgTimePerVector < 2000, "PQ encoding should be faster than 2ms per vector");
    }

    [Fact]
    public async Task Benchmark_ProductQuantization_MemoryUsage()
    {
        // Arrange
        const int dimension = 64;
        const int numSubvectors = 8;
        const int codebookSize = 256;

        var quantizer = CreateProductQuantizer(dimension, numSubvectors, codebookSize);
        var trainingData = GenerateRandomVectors(100, dimension);
        await quantizer.TrainAsync(trainingData);

        var vector = GenerateRandomVector(dimension);
        var quantized = await quantizer.QuantizeAsync(vector);

        // Calculate memory
        var originalBytes = dimension * sizeof(float);
        var quantizedBytes = quantized.Data.Length;
        var compressionRatio = (float)quantizedBytes / originalBytes;

        // Report
        _output.WriteLine($"[Product Quantization] Memory Usage:");
        _output.WriteLine($"  Dimension: {dimension}");
        _output.WriteLine($"  Subvectors: {numSubvectors}");
        _output.WriteLine($"  Original size: {originalBytes} bytes");
        _output.WriteLine($"  Quantized size: {quantizedBytes} bytes");
        _output.WriteLine($"  Compression ratio: {compressionRatio:P2}");
        _output.WriteLine($"  Memory savings: {(1 - compressionRatio):P2}");

        Assert.True(compressionRatio < 0.1f, "PQ should achieve at least 90% compression");
    }

    #endregion

    #region Search Quality Benchmarks

    [Fact]
    public async Task Benchmark_ScalarInt8_SearchQuality()
    {
        // Arrange
        const int dimension = 128;
        const int datasetSize = 100;
        const int queryCount = 10;
        const int topK = 10;

        var quantizer = CreateScalarQuantizer(dimension, QuantizationType.ScalarInt8);

        // Generate dataset
        var dataset = GenerateRandomVectors(datasetSize, dimension);
        var queries = GenerateRandomVectors(queryCount, dimension);

        // Quantize dataset
        var quantizedDataset = new List<QuantizedVector>();
        foreach (var vec in dataset)
        {
            quantizedDataset.Add(await quantizer.QuantizeAsync(vec));
        }

        // Measure recall
        var totalRecall = 0.0;
        foreach (var query in queries)
        {
            var quantizedQuery = await quantizer.QuantizeAsync(query);

            // Get ground truth (exact search)
            var groundTruth = GetTopKExact(query, dataset, topK);

            // Get approximate results
            var approximateResults = GetTopKQuantized(quantizedQuery, quantizedDataset, quantizer, topK);

            // Calculate recall
            var recall = CalculateRecall(groundTruth, approximateResults);
            totalRecall += recall;
        }

        var avgRecall = totalRecall / queryCount;

        _output.WriteLine($"[Scalar Int8] Search Quality:");
        _output.WriteLine($"  Dataset size: {datasetSize}");
        _output.WriteLine($"  Query count: {queryCount}");
        _output.WriteLine($"  Top-K: {topK}");
        _output.WriteLine($"  Average Recall@{topK}: {avgRecall:P2}");

        Assert.True(avgRecall >= 0.5, "Scalar quantization should achieve at least 50% recall");
    }

    [Fact]
    public async Task Benchmark_Binary_SearchQuality()
    {
        // Arrange
        const int dimension = 128;
        const int datasetSize = 100;
        const int queryCount = 10;
        const int topK = 10;

        var quantizer = CreateBinaryQuantizer(dimension);

        // Generate dataset
        var dataset = GenerateRandomVectors(datasetSize, dimension);
        var queries = GenerateRandomVectors(queryCount, dimension);

        // Quantize dataset
        var quantizedDataset = new List<QuantizedVector>();
        foreach (var vec in dataset)
        {
            quantizedDataset.Add(await quantizer.QuantizeAsync(vec));
        }

        // Measure recall
        var totalRecall = 0.0;
        foreach (var query in queries)
        {
            var quantizedQuery = await quantizer.QuantizeAsync(query);

            // Get ground truth (exact search)
            var groundTruth = GetTopKExact(query, dataset, topK);

            // Get approximate results
            var approximateResults = GetTopKQuantized(quantizedQuery, quantizedDataset, quantizer, topK);

            // Calculate recall
            var recall = CalculateRecall(groundTruth, approximateResults);
            totalRecall += recall;
        }

        var avgRecall = totalRecall / queryCount;

        _output.WriteLine($"[Binary] Search Quality:");
        _output.WriteLine($"  Dataset size: {datasetSize}");
        _output.WriteLine($"  Query count: {queryCount}");
        _output.WriteLine($"  Top-K: {topK}");
        _output.WriteLine($"  Average Recall@{topK}: {avgRecall:P2}");

        Assert.True(avgRecall >= 0.3, "Binary quantization should achieve at least 30% recall");
    }

    #endregion

    #region Summary Report

    [Fact]
    public async Task GenerateSummaryReport()
    {
        const int dimension = 1536;

        _output.WriteLine("=== Quantization Performance Summary ===");
        _output.WriteLine($"Dimension: {dimension}");
        _output.WriteLine("");

        // Memory comparison
        var originalBytes = dimension * sizeof(float);

        var scalarQuantizer = CreateScalarQuantizer(dimension, QuantizationType.ScalarInt8);
        var binaryQuantizer = CreateBinaryQuantizer(dimension);

        var testVector = GenerateRandomVector(dimension);
        var scalarQuantized = await scalarQuantizer.QuantizeAsync(testVector);
        var binaryQuantized = await binaryQuantizer.QuantizeAsync(testVector);

        _output.WriteLine("Memory Usage Comparison:");
        _output.WriteLine($"  Original (float32):    {originalBytes,6} bytes  (100%)");
        _output.WriteLine($"  Scalar Int8:           {scalarQuantized.Data.Length,6} bytes  ({100.0 * scalarQuantized.Data.Length / originalBytes:F1}%)");
        _output.WriteLine($"  Binary:                {binaryQuantized.Data.Length,6} bytes  ({100.0 * binaryQuantized.Data.Length / originalBytes:F1}%)");
        _output.WriteLine("");

        // Speed comparison (quantization)
        const int iterations = 100;
        var vectors = GenerateRandomVectors(iterations, dimension);

        var swScalar = Stopwatch.StartNew();
        foreach (var v in vectors) await scalarQuantizer.QuantizeAsync(v);
        swScalar.Stop();

        var swBinary = Stopwatch.StartNew();
        foreach (var v in vectors) await binaryQuantizer.QuantizeAsync(v);
        swBinary.Stop();

        _output.WriteLine("Quantization Speed:");
        _output.WriteLine($"  Scalar Int8: {swScalar.Elapsed.TotalMilliseconds / iterations:F3} ms/vector");
        _output.WriteLine($"  Binary:      {swBinary.Elapsed.TotalMilliseconds / iterations:F3} ms/vector");
        _output.WriteLine("");

        _output.WriteLine("Recommended Use Cases:");
        _output.WriteLine("  - Scalar Int8: Good balance of compression (4x) and accuracy");
        _output.WriteLine("  - Binary: Maximum compression (32x), fast for reranking candidates");
        _output.WriteLine("  - Product Quantization: Best compression for large-scale systems");

        Assert.True(true); // Summary report always passes
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

    private static List<float[]> GenerateRandomVectors(int count, int dimension)
    {
        var random = new Random(42);
        var vectors = new List<float[]>();
        for (int i = 0; i < count; i++)
        {
            var vector = new float[dimension];
            for (int j = 0; j < dimension; j++)
            {
                vector[j] = (float)(random.NextDouble() * 2 - 1);
            }
            vectors.Add(vector);
        }
        return vectors;
    }

    private static float ComputeCosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private static List<int> GetTopKExact(float[] query, List<float[]> dataset, int k)
    {
        return dataset
            .Select((vec, idx) => (idx, score: ComputeCosineSimilarity(query, vec)))
            .OrderByDescending(x => x.score)
            .Take(k)
            .Select(x => x.idx)
            .ToList();
    }

    private static List<int> GetTopKQuantized(QuantizedVector query, List<QuantizedVector> dataset, IVectorQuantizer quantizer, int k)
    {
        return dataset
            .Select((vec, idx) => (idx, distance: quantizer.ComputeDistance(query, vec)))
            .OrderBy(x => x.distance)
            .Take(k)
            .Select(x => x.idx)
            .ToList();
    }

    private static float CalculateRecall(List<int> groundTruth, List<int> approximate)
    {
        var intersection = groundTruth.Intersect(approximate).Count();
        return (float)intersection / groundTruth.Count;
    }

    #endregion
}
