using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Storage.PostgreSQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Storage.PostgreSQL.Tests.Integration;

/// <summary>
/// HNSW 인덱스 벤치마킹 통합 테스트
/// </summary>
public class VectorIndexBenchmarkTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly PostgreSqlContainer _postgresContainer;
    private readonly ILogger<PostgreSQLVectorIndexBenchmark> _logger;
    private string _connectionString = string.Empty;

    public VectorIndexBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;

        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg16")
            .WithDatabase("fluxindex_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .WithPortBinding(54321, 5432)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new XunitLoggerProvider(_output)));
        _logger = loggerFactory.CreateLogger<PostgreSQLVectorIndexBenchmark>();
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        // pgvector 확장 활성화
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    /// <summary>
    /// 기본 HNSW 벤치마크 테스트
    /// </summary>
    [Fact]
    public async Task BenchmarkHnswIndexAsync_ShouldReturnValidResults()
    {
        // Arrange
        var benchmark = new PostgreSQLVectorIndexBenchmark(_connectionString, _logger);
        var options = new HnswBenchmarkOptions
        {
            TestVectorCount = 1000, // 테스트용으로 작은 크기
            VectorDimensions = 384,
            QueryCount = 50,
            TopK = 10,
            WarmupQueries = 5,
            RecreateIndex = true,
            Iterations = 1,
            MonitorMemoryUsage = true,
            MeasureAccuracy = true,
            RandomSeed = 42
        };

        // Act
        var result = await benchmark.BenchmarkHnswIndexAsync(options);

        // Assert
        Assert.True(result.IsSuccessful, $"벤치마크 실패: {result.ErrorMessage}");
        Assert.True(result.AverageQueryTimeMs > 0, "평균 쿼리 시간이 0보다 커야 합니다");
        Assert.True(result.IndexBuildTimeMs > 0, "인덱스 빌드 시간이 0보다 커야 합니다");
        Assert.True(result.RecallAtK >= 0 && result.RecallAtK <= 1, "Recall은 0과 1 사이의 값이어야 합니다");
        Assert.True(result.IndexSizeBytes > 0, "인덱스 크기가 0보다 커야 합니다");
        Assert.True(result.QueriesPerSecond > 0, "QPS가 0보다 커야 합니다");

        _output.WriteLine(result.GenerateSummary());
    }

    /// <summary>
    /// 여러 매개변수 조합 벤치마크 테스트
    /// </summary>
    [Fact]
    public async Task BenchmarkParameterCombinationsAsync_ShouldCompareParameters()
    {
        // Arrange
        var benchmark = new PostgreSQLVectorIndexBenchmark(_connectionString, _logger);
        var options = new HnswBenchmarkOptions
        {
            TestVectorCount = 500,
            VectorDimensions = 384,
            QueryCount = 20,
            TopK = 5,
            WarmupQueries = 2,
            RecreateIndex = true,
            Iterations = 1,
            MonitorMemoryUsage = true,
            MeasureAccuracy = false, // 시간 단축을 위해 비활성화
            RandomSeed = 42
        };

        var parameterCombinations = new[]
        {
            new HnswParameters { M = 8, EfConstruction = 32, EfSearch = 20 },
            new HnswParameters { M = 16, EfConstruction = 64, EfSearch = 40 },
            new HnswParameters { M = 24, EfConstruction = 96, EfSearch = 60 }
        };

        // Act
        var results = await benchmark.BenchmarkParameterCombinationsAsync(parameterCombinations, options);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.True(result.IsSuccessful, $"벤치마크 실패: {result.ErrorMessage}"));

        // 결과 출력
        foreach (var result in results)
        {
            _output.WriteLine($"매개변수: {result.Parameters.GetIdentifier()}");
            _output.WriteLine($"평균 쿼리 시간: {result.AverageQueryTimeMs:F2}ms");
            _output.WriteLine($"QPS: {result.QueriesPerSecond:F0}");
            _output.WriteLine($"인덱스 크기: {result.IndexSizeBytes / (1024 * 1024):F1}MB");
            _output.WriteLine("---");
        }
    }

    /// <summary>
    /// 자동 튜닝 테스트
    /// </summary>
    [Fact]
    public async Task AutoTuneParametersAsync_ShouldFindOptimalParameters()
    {
        // Arrange
        var benchmark = new PostgreSQLVectorIndexBenchmark(_connectionString, _logger);
        var tuningOptions = new HnswAutoTuningOptions
        {
            TargetQueryTimeMs = 50.0,
            MinRecallRequired = 0.80,
            MaxMemoryUsageMB = 1024,
            MaxBuildTimeMinutes = 5.0,
            Strategy = TuningStrategy.BalancedOptimization,
            MaxTuningAttempts = 6, // 테스트용으로 제한
            BenchmarkOptions = new HnswBenchmarkOptions
            {
                TestVectorCount = 500,
                VectorDimensions = 384,
                QueryCount = 20,
                TopK = 5,
                WarmupQueries = 2,
                RecreateIndex = true,
                Iterations = 1,
                MonitorMemoryUsage = true,
                MeasureAccuracy = true,
                RandomSeed = 42
            }
        };

        // Act
        var optimalParameters = await benchmark.AutoTuneParametersAsync(tuningOptions);

        // Assert
        Assert.NotNull(optimalParameters);
        Assert.True(optimalParameters.M >= 4, "M 값이 유효해야 합니다");
        Assert.True(optimalParameters.EfConstruction >= 16, "EfConstruction 값이 유효해야 합니다");
        Assert.True(optimalParameters.EfSearch >= 10, "EfSearch 값이 유효해야 합니다");

        _output.WriteLine($"최적 매개변수: {optimalParameters.GetIdentifier()}");
        _output.WriteLine($"M: {optimalParameters.M}");
        _output.WriteLine($"EfConstruction: {optimalParameters.EfConstruction}");
        _output.WriteLine($"EfSearch: {optimalParameters.EfSearch}");
    }

    /// <summary>
    /// 속도 최적화 전략 테스트
    /// </summary>
    [Fact]
    public async Task AutoTuneParametersAsync_SpeedOptimization_ShouldPrioritizeSpeed()
    {
        // Arrange
        var benchmark = new PostgreSQLVectorIndexBenchmark(_connectionString, _logger);
        var tuningOptions = new HnswAutoTuningOptions
        {
            TargetQueryTimeMs = 30.0, // 더 빠른 목표
            MinRecallRequired = 0.70, // 낮은 Recall 허용
            MaxMemoryUsageMB = 2048,
            MaxBuildTimeMinutes = 10.0,
            Strategy = TuningStrategy.SpeedOptimization,
            MaxTuningAttempts = 6,
            BenchmarkOptions = new HnswBenchmarkOptions
            {
                TestVectorCount = 300,
                VectorDimensions = 384,
                QueryCount = 15,
                TopK = 5,
                WarmupQueries = 2,
                RecreateIndex = true,
                Iterations = 1,
                MonitorMemoryUsage = true,
                MeasureAccuracy = true,
                RandomSeed = 42
            }
        };

        // Act
        var speedOptimizedParameters = await benchmark.AutoTuneParametersAsync(tuningOptions);

        // Assert
        Assert.NotNull(speedOptimizedParameters);

        // 속도 최적화에서는 일반적으로 더 작은 M과 EfConstruction 값을 선호
        Assert.True(speedOptimizedParameters.M <= 16, $"속도 최적화에서는 M이 16 이하여야 하는데 {speedOptimizedParameters.M}입니다");

        _output.WriteLine($"속도 최적화 매개변수: {speedOptimizedParameters.GetIdentifier()}");
        _output.WriteLine($"M: {speedOptimizedParameters.M}");
        _output.WriteLine($"EfConstruction: {speedOptimizedParameters.EfConstruction}");
        _output.WriteLine($"EfSearch: {speedOptimizedParameters.EfSearch}");
    }

    /// <summary>
    /// 인덱스 성능 메트릭 수집 테스트
    /// </summary>
    [Fact]
    public async Task CollectPerformanceMetricsAsync_ShouldReturnMetrics()
    {
        // Arrange
        var benchmark = new PostgreSQLVectorIndexBenchmark(_connectionString, _logger);
        var testTableName = $"test_vectors_{Guid.NewGuid():N}";
        var indexName = $"idx_{testTableName}_embedding";

        // 테스트 테이블과 인덱스 생성
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var createTableSql = $@"
            CREATE TABLE {testTableName} (
                id SERIAL PRIMARY KEY,
                embedding vector(384),
                content TEXT
            )";
        await using var createCommand = new NpgsqlCommand(createTableSql, connection);
        await createCommand.ExecuteNonQueryAsync();

        // 샘플 데이터 삽입
        var insertSql = $@"
            INSERT INTO {testTableName} (embedding, content)
            SELECT (ARRAY(SELECT random() FROM generate_series(1, 384)))::vector, 'content_' || i
            FROM generate_series(1, 100) AS i";
        await using var insertCommand = new NpgsqlCommand(insertSql, connection);
        await insertCommand.ExecuteNonQueryAsync();

        // 인덱스 생성
        var createIndexSql = $@"
            CREATE INDEX {indexName}
            ON {testTableName}
            USING hnsw (embedding vector_cosine_ops)
            WITH (m = 16, ef_construction = 64)";
        await using var indexCommand = new NpgsqlCommand(createIndexSql, connection);
        await indexCommand.ExecuteNonQueryAsync();

        // Act
        var metrics = await benchmark.CollectPerformanceMetricsAsync(indexName);

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(indexName, metrics.IndexName);
        Assert.True(metrics.CollectedAt <= DateTime.UtcNow.AddMinutes(1));

        // 정리
        var dropSql = $"DROP TABLE IF EXISTS {testTableName}";
        await using var dropCommand = new NpgsqlCommand(dropSql, connection);
        await dropCommand.ExecuteNonQueryAsync();

        _output.WriteLine($"인덱스 메트릭 수집 완료: {metrics.IndexName}");
        _output.WriteLine($"수집 시간: {metrics.CollectedAt}");
        _output.WriteLine($"효율성 점수: {metrics.GetEfficiencyScore():F3}");
    }

    /// <summary>
    /// 메모리 최적화 전략 테스트
    /// </summary>
    [Fact]
    public async Task AutoTuneParametersAsync_MemoryOptimization_ShouldMinimizeMemory()
    {
        // Arrange
        var benchmark = new PostgreSQLVectorIndexBenchmark(_connectionString, _logger);
        var tuningOptions = new HnswAutoTuningOptions
        {
            TargetQueryTimeMs = 100.0, // 메모리 절약을 위해 느린 쿼리 허용
            MinRecallRequired = 0.75,
            MaxMemoryUsageMB = 512, // 작은 메모리 제한
            MaxBuildTimeMinutes = 5.0,
            Strategy = TuningStrategy.MemoryOptimization,
            MaxTuningAttempts = 6,
            BenchmarkOptions = new HnswBenchmarkOptions
            {
                TestVectorCount = 400,
                VectorDimensions = 384,
                QueryCount = 15,
                TopK = 5,
                WarmupQueries = 2,
                RecreateIndex = true,
                Iterations = 1,
                MonitorMemoryUsage = true,
                MeasureAccuracy = true,
                RandomSeed = 42
            }
        };

        // Act
        var memoryOptimizedParameters = await benchmark.AutoTuneParametersAsync(tuningOptions);

        // Assert
        Assert.NotNull(memoryOptimizedParameters);

        // 메모리 최적화에서는 일반적으로 더 작은 M 값을 선호
        Assert.True(memoryOptimizedParameters.M <= 12, $"메모리 최적화에서는 M이 12 이하여야 하는데 {memoryOptimizedParameters.M}입니다");

        _output.WriteLine($"메모리 최적화 매개변수: {memoryOptimizedParameters.GetIdentifier()}");
        _output.WriteLine($"M: {memoryOptimizedParameters.M}");
        _output.WriteLine($"EfConstruction: {memoryOptimizedParameters.EfConstruction}");
        _output.WriteLine($"EfSearch: {memoryOptimizedParameters.EfSearch}");
    }
}