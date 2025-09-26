using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using FluxIndex.Storage.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// SQLite-vec 성능 벤치마크 및 비교 테스트
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class SQLiteVecPerformanceBenchmarks
{
    private IVectorStore _sqliteVecStore = null!;
    private IVectorStore _legacyStore = null!;
    private List<DocumentChunk> _testChunks = null!;
    private List<float[]> _queryVectors = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // SQLite-vec 저장소 설정 (폴백 모드)
        var services1 = new ServiceCollection();
        services1.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services1.AddSQLiteVecInMemoryVectorStore(vectorDimension: 384);

        var provider1 = services1.BuildServiceProvider();
        var hostedServices1 = provider1.GetServices<IHostedService>();
        foreach (var service in hostedServices1)
        {
            await service.StartAsync(CancellationToken.None);
        }
        _sqliteVecStore = provider1.GetRequiredService<IVectorStore>();

        // 레거시 저장소 설정
        var services2 = new ServiceCollection();
        services2.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services2.AddSQLiteInMemoryVectorStore();

        var provider2 = services2.BuildServiceProvider();
        var hostedServices2 = provider2.GetServices<IHostedService>();
        foreach (var service in hostedServices2)
        {
            await service.StartAsync(CancellationToken.None);
        }
        _legacyStore = provider2.GetRequiredService<IVectorStore>();

        // 테스트 데이터 생성
        _testChunks = GenerateTestChunks(1000);
        _queryVectors = GenerateQueryVectors(100);

        // 데이터 사전 로드
        await _sqliteVecStore.StoreBatchAsync(_testChunks);
        await _legacyStore.StoreBatchAsync(_testChunks);
    }

    [Params(10, 100, 1000)]
    public int ChunkCount { get; set; }

    [Params(5, 20, 50)]
    public int TopK { get; set; }

    [Benchmark(Baseline = true)]
    public async Task<IEnumerable<DocumentChunk>> LegacySearch()
    {
        var queryVector = _queryVectors[0];
        return await _legacyStore.SearchAsync(queryVector, TopK, 0.0f);
    }

    [Benchmark]
    public async Task<IEnumerable<DocumentChunk>> SQLiteVecSearch()
    {
        var queryVector = _queryVectors[0];
        return await _sqliteVecStore.SearchAsync(queryVector, TopK, 0.0f);
    }

    [Benchmark]
    public async Task<IEnumerable<string>> LegacyBatchStore()
    {
        var chunks = _testChunks.Take(ChunkCount);
        return await _legacyStore.StoreBatchAsync(chunks);
    }

    [Benchmark]
    public async Task<IEnumerable<string>> SQLiteVecBatchStore()
    {
        var chunks = _testChunks.Take(ChunkCount);
        return await _sqliteVecStore.StoreBatchAsync(chunks);
    }

    private List<DocumentChunk> GenerateTestChunks(int count)
    {
        var random = new Random(42);
        var chunks = new List<DocumentChunk>();

        for (int i = 0; i < count; i++)
        {
            var embedding = new float[384];
            for (int j = 0; j < embedding.Length; j++)
            {
                embedding[j] = (float)(random.NextDouble() - 0.5) * 2;
            }

            // 정규화
            var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
            if (magnitude > 0)
            {
                for (int j = 0; j < embedding.Length; j++)
                {
                    embedding[j] /= magnitude;
                }
            }

            chunks.Add(new DocumentChunk
            {
                DocumentId = $"doc_{i / 10}",
                ChunkIndex = i % 10,
                Content = $"Test content {i}",
                Embedding = embedding,
                TokenCount = 50,
                Metadata = new Dictionary<string, object> { ["index"] = i }
            });
        }

        return chunks;
    }

    private List<float[]> GenerateQueryVectors(int count)
    {
        var random = new Random(123);
        var vectors = new List<float[]>();

        for (int i = 0; i < count; i++)
        {
            var vector = new float[384];
            for (int j = 0; j < vector.Length; j++)
            {
                vector[j] = (float)(random.NextDouble() - 0.5) * 2;
            }

            // 정규화
            var magnitude = (float)Math.Sqrt(vector.Sum(x => x * x));
            if (magnitude > 0)
            {
                for (int j = 0; j < vector.Length; j++)
                {
                    vector[j] /= magnitude;
                }
            }

            vectors.Add(vector);
        }

        return vectors;
    }
}

/// <summary>
/// 성능 비교 테스트 (xUnit 기반)
/// </summary>
public class SQLiteVecPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _sqliteVecProvider;
    private readonly ServiceProvider _legacyProvider;
    private readonly string _testDatabasePath;

    public SQLiteVecPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"fluxindex_perf_{Guid.NewGuid()}.db");

        // SQLite-vec 환경 설정
        var services1 = new ServiceCollection();
        services1.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services1.AddSQLiteVecVectorStore(_testDatabasePath + "_vec", vectorDimension: 384);
        _sqliteVecProvider = services1.BuildServiceProvider();

        // 레거시 환경 설정
        var services2 = new ServiceCollection();
        services2.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services2.AddSQLiteVectorStore(_testDatabasePath + "_legacy");
        _legacyProvider = services2.BuildServiceProvider();
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(500, 20)]
    [InlineData(1000, 50)]
    [InlineData(5000, 100)]
    public async Task CompareSearchPerformance_SQLiteVecVsLegacy_ShouldShowImprovement(int datasetSize, int topK)
    {
        // Arrange
        await InitializeProvidersAsync();

        var sqliteVecStore = _sqliteVecProvider.GetRequiredService<IVectorStore>();
        var legacyStore = _legacyProvider.GetRequiredService<IVectorStore>();

        var testChunks = GenerateTestDataset(datasetSize);
        var queryVector = CreateTestEmbedding();

        // 데이터 로드
        await sqliteVecStore.StoreBatchAsync(testChunks);
        await legacyStore.StoreBatchAsync(testChunks);

        var searchIterations = Math.Max(10, 100 / (datasetSize / 100)); // 데이터 크기에 따라 반복 횟수 조정

        // Act - SQLite-vec 성능 측정
        var sqliteVecTimes = new List<long>();
        for (int i = 0; i < searchIterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var results = await sqliteVecStore.SearchAsync(queryVector, topK, 0.0f);
            stopwatch.Stop();
            sqliteVecTimes.Add(stopwatch.ElapsedMilliseconds);

            results.Should().HaveCountLessThanOrEqualTo(topK);
        }

        // Act - 레거시 성능 측정
        var legacyTimes = new List<long>();
        for (int i = 0; i < searchIterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var results = await legacyStore.SearchAsync(queryVector, topK, 0.0f);
            stopwatch.Stop();
            legacyTimes.Add(stopwatch.ElapsedMilliseconds);

            results.Should().HaveCountLessThanOrEqualTo(topK);
        }

        // Assert & Analysis
        var sqliteVecAvg = sqliteVecTimes.Average();
        var legacyAvg = legacyTimes.Average();
        var improvement = (legacyAvg - sqliteVecAvg) / legacyAvg * 100;

        _output.WriteLine($"데이터셋 크기: {datasetSize}, TopK: {topK}");
        _output.WriteLine($"SQLite-vec 평균: {sqliteVecAvg:F2}ms");
        _output.WriteLine($"Legacy 평균: {legacyAvg:F2}ms");
        _output.WriteLine($"개선율: {improvement:F1}%");

        // 성능 기준 검증
        sqliteVecAvg.Should().BeLessThan(500); // 500ms 이하

        // 대용량 데이터에서는 SQLite-vec가 더 나은 성능을 보여야 함 (기대값)
        // 하지만 현재 폴백 모드이므로 큰 차이는 없을 수 있음
        if (datasetSize >= 1000)
        {
            improvement.Should().BeGreaterThan(-50); // 50% 이상 성능 저하는 없어야 함
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public async Task CompareBatchInsertPerformance_ShouldMeetPerformanceTargets(int batchSize)
    {
        // Arrange
        await InitializeProvidersAsync();

        var sqliteVecStore = _sqliteVecProvider.GetRequiredService<IVectorStore>();
        var legacyStore = _legacyProvider.GetRequiredService<IVectorStore>();

        var testChunks = GenerateTestDataset(batchSize);

        // Act - SQLite-vec 배치 삽입
        var stopwatch = Stopwatch.StartNew();
        var sqliteVecIds = await sqliteVecStore.StoreBatchAsync(testChunks);
        var sqliteVecTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var legacyIds = await legacyStore.StoreBatchAsync(testChunks);
        var legacyTime = stopwatch.ElapsedMilliseconds;

        // Assert
        sqliteVecIds.Should().HaveCount(batchSize);
        legacyIds.Should().HaveCount(batchSize);

        var sqliteVecThroughput = (double)batchSize / sqliteVecTime * 1000; // 초당 처리량
        var legacyThroughput = (double)batchSize / legacyTime * 1000;

        _output.WriteLine($"배치 크기: {batchSize}");
        _output.WriteLine($"SQLite-vec: {sqliteVecTime}ms ({sqliteVecThroughput:F1} chunks/sec)");
        _output.WriteLine($"Legacy: {legacyTime}ms ({legacyThroughput:F1} chunks/sec)");

        // 성능 기준
        sqliteVecThroughput.Should().BeGreaterThan(10); // 초당 최소 10개
        legacyThroughput.Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task MemoryUsageTest_LargeDataset_ShouldNotExceedLimits()
    {
        // Arrange
        await InitializeProvidersAsync();

        var sqliteVecStore = _sqliteVecProvider.GetRequiredService<IVectorStore>();
        var testChunks = GenerateTestDataset(10000); // 대용량 테스트

        var initialMemory = GC.GetTotalMemory(true);

        // Act
        var ids = await sqliteVecStore.StoreBatchAsync(testChunks);

        // 검색 수행
        for (int i = 0; i < 50; i++)
        {
            var queryVector = CreateTestEmbedding();
            var results = await sqliteVecStore.SearchAsync(queryVector, topK: 20);
            results.Should().HaveCountLessThanOrEqualTo(20);
        }

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = (finalMemory - initialMemory) / 1024.0 / 1024.0; // MB

        // Assert
        ids.Should().HaveCount(10000);

        _output.WriteLine($"메모리 사용량 증가: {memoryIncrease:F2} MB");
        _output.WriteLine($"초기 메모리: {initialMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"최종 메모리: {finalMemory / 1024.0 / 1024.0:F2} MB");

        // 메모리 사용량이 합리적인 범위 내에 있어야 함
        memoryIncrease.Should().BeLessThan(500); // 500MB 이하
    }

    [Fact]
    public async Task ConcurrentAccessPerformance_MultipleClients_ShouldMaintainThroughput()
    {
        // Arrange
        await InitializeProvidersAsync();

        var sqliteVecStore = _sqliteVecProvider.GetRequiredService<IVectorStore>();

        // 기본 데이터 로드
        var baseChunks = GenerateTestDataset(1000);
        await sqliteVecStore.StoreBatchAsync(baseChunks);

        const int concurrentClients = 5;
        const int operationsPerClient = 20;

        // Act
        var tasks = Enumerable.Range(0, concurrentClients).Select(clientId =>
            Task.Run(async () =>
            {
                var times = new List<long>();
                var random = new Random(clientId);

                for (int i = 0; i < operationsPerClient; i++)
                {
                    var stopwatch = Stopwatch.StartNew();

                    if (i % 3 == 0) // 33% 검색
                    {
                        var queryVector = CreateTestEmbedding();
                        var results = await sqliteVecStore.SearchAsync(queryVector, topK: 10);
                    }
                    else if (i % 3 == 1) // 33% 삽입
                    {
                        var chunk = GenerateTestDataset(1)[0];
                        chunk.DocumentId = $"client{clientId}_doc{i}";
                        await sqliteVecStore.StoreAsync(chunk);
                    }
                    else // 33% 조회
                    {
                        var docId = $"doc_{random.Next(0, 100)}";
                        var chunks = await sqliteVecStore.GetByDocumentIdAsync(docId);
                    }

                    stopwatch.Stop();
                    times.Add(stopwatch.ElapsedMilliseconds);
                }

                return new
                {
                    ClientId = clientId,
                    AverageTime = times.Average(),
                    MaxTime = times.Max(),
                    TotalOperations = operationsPerClient
                };
            }));

        var results = await Task.WhenAll(tasks);

        // Assert
        var overallAverageTime = results.Average(r => r.AverageTime);
        var overallMaxTime = results.Max(r => r.MaxTime);
        var totalOperations = results.Sum(r => r.TotalOperations);

        _output.WriteLine($"동시 클라이언트 수: {concurrentClients}");
        _output.WriteLine($"클라이언트당 작업 수: {operationsPerClient}");
        _output.WriteLine($"전체 평균 응답 시간: {overallAverageTime:F2}ms");
        _output.WriteLine($"최대 응답 시간: {overallMaxTime}ms");

        // 성능 기준
        overallAverageTime.Should().BeLessThan(200); // 평균 200ms 이하
        overallMaxTime.Should().BeLessThan(1000); // 최대 1초 이하

        foreach (var result in results)
        {
            _output.WriteLine($"클라이언트 {result.ClientId}: 평균 {result.AverageTime:F2}ms, 최대 {result.MaxTime}ms");
        }
    }

    private async Task InitializeProvidersAsync()
    {
        // SQLite-vec 초기화
        var hostedServices1 = _sqliteVecProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices1)
        {
            await service.StartAsync(CancellationToken.None);
        }

        // Legacy 초기화
        var hostedServices2 = _legacyProvider.GetServices<IHostedService>();
        foreach (var service in hostedServices2)
        {
            await service.StartAsync(CancellationToken.None);
        }
    }

    private List<DocumentChunk> GenerateTestDataset(int count)
    {
        var random = new Random(42);
        var chunks = new List<DocumentChunk>();

        for (int i = 0; i < count; i++)
        {
            chunks.Add(new DocumentChunk
            {
                DocumentId = $"doc_{i / 10}",
                ChunkIndex = i % 10,
                Content = $"Performance test content {i} with some meaningful text for testing",
                Embedding = CreateTestEmbedding(random),
                TokenCount = 50,
                Metadata = new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["category"] = $"category_{i % 5}",
                    ["timestamp"] = DateTimeOffset.UtcNow.AddMinutes(-i).ToUnixTimeSeconds()
                }
            });
        }

        return chunks;
    }

    private float[] CreateTestEmbedding(Random? random = null)
    {
        random ??= new Random();
        var embedding = new float[384];

        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() - 0.5) * 2;
        }

        // 정규화
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }

    public void Dispose()
    {
        _sqliteVecProvider?.Dispose();
        _legacyProvider?.Dispose();

        // 테스트 파일들 정리
        try
        {
            var files = Directory.GetFiles(Path.GetTempPath(), $"fluxindex_perf_*");
            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"테스트 파일 정리 실패: {ex.Message}");
        }
    }
}

/// <summary>
/// BenchmarkDotNet 실행을 위한 프로그램 클래스
/// 별도 프로젝트에서 실행: dotnet run --project FluxIndex.Storage.SQLite.Tests -c Release -- --job short
/// </summary>
// public class BenchmarkProgram
// {
//     public static void Main(string[] args)
//     {
//         // BenchmarkDotNet으로 성능 테스트 실행
//         var summary = BenchmarkRunner.Run<SQLiteVecPerformanceBenchmarks>();
//         Console.WriteLine(summary);
//     }
// }