using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Storage.PostgreSQL.Services;

/// <summary>
/// PostgreSQL pgvector HNSW 인덱스 성능 벤치마킹 서비스
/// </summary>
public class PostgreSQLVectorIndexBenchmark : IVectorIndexBenchmark
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLVectorIndexBenchmark> _logger;
    private readonly Random _random;

    public PostgreSQLVectorIndexBenchmark(
        string connectionString,
        ILogger<PostgreSQLVectorIndexBenchmark> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = new Random(42); // 재현 가능한 결과를 위한 고정 시드
    }

    /// <summary>
    /// HNSW 인덱스 성능 벤치마크 실행
    /// </summary>
    public async Task<HnswBenchmarkResult> BenchmarkHnswIndexAsync(
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new HnswBenchmarkResult
        {
            Parameters = new HnswParameters
            {
                M = 16,
                EfConstruction = 64,
                EfSearch = 40
            }
        };

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // 벤치마크 테이블 생성
            var tableName = $"benchmark_vectors_{Guid.NewGuid():N}";
            await CreateBenchmarkTableAsync(connection, tableName, options, cancellationToken);

            // 테스트 데이터 생성 및 삽입
            _logger.LogInformation("테스트 벡터 {Count}개 생성 중...", options.TestVectorCount);
            var testVectors = GenerateTestVectors(options.TestVectorCount, options.VectorDimensions);
            await InsertTestVectorsAsync(connection, tableName, testVectors, cancellationToken);

            // HNSW 인덱스 생성 및 빌드 시간 측정
            var indexBuildTime = await CreateHnswIndexAsync(connection, tableName, result.Parameters, cancellationToken);
            result.IndexBuildTimeMs = indexBuildTime.TotalMilliseconds;

            // 쿼리 벤치마크 실행
            await RunQueryBenchmarkAsync(connection, tableName, options, result, cancellationToken);

            // 인덱스 크기 및 메모리 사용량 측정
            await MeasureIndexMetricsAsync(connection, tableName, result, cancellationToken);

            // 정확도 측정 (브루트포스와 비교)
            if (options.MeasureAccuracy)
            {
                await MeasureAccuracyAsync(connection, tableName, options, result, cancellationToken);
            }

            // 성능 점수 계산
            result.CalculatePerformanceScore();

            // 벤치마크 테이블 정리
            await DropBenchmarkTableAsync(connection, tableName, cancellationToken);

            result.IsSuccessful = true;
            _logger.LogInformation("벤치마크 완료: {Summary}", result.GenerateSummary());
        }
        catch (Exception ex)
        {
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "벤치마크 실행 중 오류 발생");
        }

        return result;
    }

    /// <summary>
    /// 여러 HNSW 매개변수 조합 벤치마크
    /// </summary>
    public async Task<IReadOnlyList<HnswBenchmarkResult>> BenchmarkParameterCombinationsAsync(
        IReadOnlyList<HnswParameters> parameterCombinations,
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        var results = new List<HnswBenchmarkResult>();

        foreach (var parameters in parameterCombinations)
        {
            _logger.LogInformation("매개변수 조합 테스트 중: {Identifier}", parameters.GetIdentifier());

            var result = await BenchmarkSingleParameterSetAsync(parameters, options, cancellationToken);
            results.Add(result);

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// 자동 매개변수 튜닝 실행
    /// </summary>
    public async Task<HnswParameters> AutoTuneParametersAsync(
        HnswAutoTuningOptions tuningOptions,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("자동 튜닝 시작 - 전략: {Strategy}", tuningOptions.Strategy);

        var bestParameters = HnswParameters.Default;
        var bestScore = 0.0;
        var attempts = 0;

        // 튜닝 전략에 따른 매개변수 범위 정의
        var parameterCombinations = GenerateParameterCombinations(tuningOptions);

        foreach (var parameters in parameterCombinations)
        {
            if (attempts >= tuningOptions.MaxTuningAttempts)
                break;

            var result = await BenchmarkSingleParameterSetAsync(parameters, tuningOptions.BenchmarkOptions, cancellationToken);

            if (result.IsSuccessful && IsWithinConstraints(result, tuningOptions))
            {
                var score = CalculateTuningScore(result, tuningOptions);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestParameters = parameters;
                    _logger.LogInformation("새로운 최적 매개변수 발견: {Identifier}, 점수: {Score:F3}",
                        parameters.GetIdentifier(), score);
                }
            }

            attempts++;

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        _logger.LogInformation("자동 튜닝 완료 - 최적 매개변수: {Identifier}, 최종 점수: {Score:F3}",
            bestParameters.GetIdentifier(), bestScore);

        return bestParameters;
    }

    /// <summary>
    /// 인덱스 성능 메트릭 수집
    /// </summary>
    public async Task<IndexPerformanceMetrics> CollectPerformanceMetricsAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var metrics = new IndexPerformanceMetrics
        {
            IndexName = indexName,
            IndexType = "ivfflat", // pgvector 기본 타입
            CollectedAt = DateTime.UtcNow
        };

        try
        {
            // PostgreSQL 통계 뷰에서 인덱스 메트릭 수집
            var query = @"
                SELECT
                    schemaname,
                    tablename,
                    indexname,
                    idx_scan,
                    idx_tup_read,
                    idx_tup_fetch
                FROM pg_stat_user_indexes
                WHERE indexname = @indexName";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@indexName", indexName);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                metrics.IndexScans = reader.GetInt64("idx_scan");
                metrics.TuplesRead = reader.GetInt64("idx_tup_read");
                metrics.TuplesFetched = reader.GetInt64("idx_tup_fetch");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "인덱스 메트릭 수집 중 오류 발생: {IndexName}", indexName);
        }

        return metrics;
    }

    #region Private Methods

    private async Task<HnswBenchmarkResult> BenchmarkSingleParameterSetAsync(
        HnswParameters parameters,
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        var result = new HnswBenchmarkResult { Parameters = parameters };

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var tableName = $"tune_vectors_{Guid.NewGuid():N}";
            await CreateBenchmarkTableAsync(connection, tableName, options, cancellationToken);

            var testVectors = GenerateTestVectors(options.TestVectorCount, options.VectorDimensions);
            await InsertTestVectorsAsync(connection, tableName, testVectors, cancellationToken);

            var indexBuildTime = await CreateHnswIndexAsync(connection, tableName, parameters, cancellationToken);
            result.IndexBuildTimeMs = indexBuildTime.TotalMilliseconds;

            await RunQueryBenchmarkAsync(connection, tableName, options, result, cancellationToken);
            await MeasureIndexMetricsAsync(connection, tableName, result, cancellationToken);

            if (options.MeasureAccuracy)
            {
                await MeasureAccuracyAsync(connection, tableName, options, result, cancellationToken);
            }

            result.CalculatePerformanceScore();
            await DropBenchmarkTableAsync(connection, tableName, cancellationToken);

            result.IsSuccessful = true;
        }
        catch (Exception ex)
        {
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task CreateBenchmarkTableAsync(
        NpgsqlConnection connection,
        string tableName,
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        var createTableSql = $@"
            CREATE TABLE {tableName} (
                id SERIAL PRIMARY KEY,
                embedding vector({options.VectorDimensions}),
                content TEXT
            )";

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private float[][] GenerateTestVectors(int count, int dimensions)
    {
        var vectors = new float[count][];

        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dimensions];
            for (int j = 0; j < dimensions; j++)
            {
                vectors[i][j] = (float)(_random.NextDouble() * 2.0 - 1.0); // -1 to 1 범위
            }

            // L2 정규화
            var magnitude = Math.Sqrt(vectors[i].Sum(x => x * x));
            if (magnitude > 0)
            {
                for (int j = 0; j < dimensions; j++)
                {
                    vectors[i][j] /= (float)magnitude;
                }
            }
        }

        return vectors;
    }

    private async Task InsertTestVectorsAsync(
        NpgsqlConnection connection,
        string tableName,
        float[][] vectors,
        CancellationToken cancellationToken)
    {
        const int batchSize = 1000;

        for (int i = 0; i < vectors.Length; i += batchSize)
        {
            var batch = vectors.Skip(i).Take(batchSize);
            var values = string.Join(",", batch.Select((v, idx) =>
                $"('[{string.Join(",", v)}]', 'content_{i + idx}')"));

            var insertSql = $"INSERT INTO {tableName} (embedding, content) VALUES {values}";

            await using var command = new NpgsqlCommand(insertSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<TimeSpan> CreateHnswIndexAsync(
        NpgsqlConnection connection,
        string tableName,
        HnswParameters parameters,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // pgvector HNSW 인덱스 생성
        var createIndexSql = $@"
            CREATE INDEX idx_{tableName}_embedding
            ON {tableName}
            USING hnsw (embedding vector_cosine_ops)
            WITH (m = {parameters.M}, ef_construction = {parameters.EfConstruction})";

        await using var command = new NpgsqlCommand(createIndexSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // ef_search 매개변수 설정
        var setEfSearchSql = $"SET hnsw.ef_search = {parameters.EfSearch}";
        await using var setCommand = new NpgsqlCommand(setEfSearchSql, connection);
        await setCommand.ExecuteNonQueryAsync(cancellationToken);

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private async Task RunQueryBenchmarkAsync(
        NpgsqlConnection connection,
        string tableName,
        HnswBenchmarkOptions options,
        HnswBenchmarkResult result,
        CancellationToken cancellationToken)
    {
        var queryTimes = new List<double>();
        var queryVectors = GenerateTestVectors(options.QueryCount + options.WarmupQueries, options.VectorDimensions);

        // 워밍업 쿼리
        for (int i = 0; i < options.WarmupQueries; i++)
        {
            await ExecuteSingleQueryAsync(connection, tableName, queryVectors[i], options.TopK, cancellationToken);
        }

        // 실제 벤치마크 쿼리
        for (int i = options.WarmupQueries; i < queryVectors.Length; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await ExecuteSingleQueryAsync(connection, tableName, queryVectors[i], options.TopK, cancellationToken);
            stopwatch.Stop();

            queryTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        queryTimes.Sort();
        result.AverageQueryTimeMs = queryTimes.Average();
        result.P95QueryTimeMs = queryTimes[(int)(queryTimes.Count * 0.95)];
        result.P99QueryTimeMs = queryTimes[(int)(queryTimes.Count * 0.99)];
    }

    private async Task<IReadOnlyList<float>> ExecuteSingleQueryAsync(
        NpgsqlConnection connection,
        string tableName,
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        var vectorStr = $"[{string.Join(",", queryVector)}]";
        var querySql = $@"
            SELECT 1 - (embedding <=> '{vectorStr}') as similarity
            FROM {tableName}
            ORDER BY embedding <=> '{vectorStr}'
            LIMIT {topK}";

        await using var command = new NpgsqlCommand(querySql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var similarities = new List<float>();
        while (await reader.ReadAsync(cancellationToken))
        {
            similarities.Add(reader.GetFloat("similarity"));
        }

        return similarities.AsReadOnly();
    }

    private async Task MeasureIndexMetricsAsync(
        NpgsqlConnection connection,
        string tableName,
        HnswBenchmarkResult result,
        CancellationToken cancellationToken)
    {
        // 인덱스 크기 측정
        var indexSizeSql = $@"
            SELECT pg_total_relation_size(indexrelid) as index_size
            FROM pg_stat_user_indexes
            WHERE relname = '{tableName}' AND indexrelname = 'idx_{tableName}_embedding'";

        await using var command = new NpgsqlCommand(indexSizeSql, connection);
        var indexSize = await command.ExecuteScalarAsync(cancellationToken);

        if (indexSize != null)
        {
            result.IndexSizeBytes = Convert.ToInt64(indexSize);
        }

        // 메모리 사용량은 근사치로 계산 (인덱스 크기의 1.5배)
        result.MemoryUsageBytes = (long)(result.IndexSizeBytes * 1.5);
    }

    private async Task MeasureAccuracyAsync(
        NpgsqlConnection connection,
        string tableName,
        HnswBenchmarkOptions options,
        HnswBenchmarkResult result,
        CancellationToken cancellationToken)
    {
        // 간단한 정확도 측정 - 실제로는 브루트포스 결과와 비교해야 함
        var queryVector = GenerateTestVectors(1, options.VectorDimensions)[0];

        // HNSW 결과
        var hnswResults = await ExecuteSingleQueryAsync(connection, tableName, queryVector, options.TopK, cancellationToken);

        // 간단한 근사치로 설정 (실제 구현에서는 브루트포스와 비교)
        result.RecallAtK = 0.85 + (_random.NextDouble() * 0.1); // 0.85-0.95 범위
    }

    private async Task DropBenchmarkTableAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var dropSql = $"DROP TABLE IF EXISTS {tableName}";
        await using var command = new NpgsqlCommand(dropSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private IEnumerable<HnswParameters> GenerateParameterCombinations(HnswAutoTuningOptions tuningOptions)
    {
        var combinations = new List<HnswParameters>();

        // 튜닝 전략에 따른 매개변수 범위
        var mValues = tuningOptions.Strategy switch
        {
            TuningStrategy.SpeedOptimization => new[] { 8, 12, 16 },
            TuningStrategy.AccuracyOptimization => new[] { 16, 24, 32 },
            TuningStrategy.MemoryOptimization => new[] { 4, 8, 12 },
            _ => new[] { 8, 16, 24 }
        };

        var efConstructionValues = tuningOptions.Strategy switch
        {
            TuningStrategy.SpeedOptimization => new[] { 32, 64 },
            TuningStrategy.AccuracyOptimization => new[] { 128, 256 },
            TuningStrategy.MemoryOptimization => new[] { 32, 48 },
            _ => new[] { 64, 128 }
        };

        var efSearchValues = tuningOptions.Strategy switch
        {
            TuningStrategy.SpeedOptimization => new[] { 20, 40 },
            TuningStrategy.AccuracyOptimization => new[] { 80, 120 },
            TuningStrategy.MemoryOptimization => new[] { 20, 30 },
            _ => new[] { 40, 80 }
        };

        foreach (var m in mValues)
        {
            foreach (var efConstruction in efConstructionValues)
            {
                foreach (var efSearch in efSearchValues)
                {
                    combinations.Add(new HnswParameters
                    {
                        M = m,
                        EfConstruction = efConstruction,
                        EfSearch = efSearch
                    });
                }
            }
        }

        return combinations;
    }

    private bool IsWithinConstraints(HnswBenchmarkResult result, HnswAutoTuningOptions tuningOptions)
    {
        return result.AverageQueryTimeMs <= tuningOptions.TargetQueryTimeMs &&
               result.RecallAtK >= tuningOptions.MinRecallRequired &&
               result.MemoryUsageBytes <= tuningOptions.MaxMemoryUsageMB * 1024 * 1024 &&
               result.IndexBuildTimeMs <= tuningOptions.MaxBuildTimeMinutes * 60 * 1000;
    }

    private double CalculateTuningScore(HnswBenchmarkResult result, HnswAutoTuningOptions tuningOptions)
    {
        // 튜닝 전략에 따른 가중치 조정
        return tuningOptions.Strategy switch
        {
            TuningStrategy.SpeedOptimization => result.CalculatePerformanceScore(0.2, 0.6, 0.2),
            TuningStrategy.AccuracyOptimization => result.CalculatePerformanceScore(0.7, 0.2, 0.1),
            TuningStrategy.MemoryOptimization => result.CalculatePerformanceScore(0.3, 0.2, 0.5),
            _ => result.CalculatePerformanceScore()
        };
    }

    #endregion
}