using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 벡터 인덱스 벤치마킹 서비스 인터페이스
/// </summary>
public interface IVectorIndexBenchmark
{
    /// <summary>
    /// HNSW 인덱스 성능 벤치마크 실행
    /// </summary>
    /// <param name="options">벤치마크 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>벤치마크 결과</returns>
    Task<HnswBenchmarkResult> BenchmarkHnswIndexAsync(
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 HNSW 매개변수 조합 벤치마크
    /// </summary>
    /// <param name="parameterCombinations">매개변수 조합 목록</param>
    /// <param name="options">벤치마크 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>벤치마크 결과 목록</returns>
    Task<IReadOnlyList<HnswBenchmarkResult>> BenchmarkParameterCombinationsAsync(
        IReadOnlyList<HnswParameters> parameterCombinations,
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 자동 매개변수 튜닝 실행
    /// </summary>
    /// <param name="tuningOptions">튜닝 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>최적 매개변수</returns>
    Task<HnswParameters> AutoTuneParametersAsync(
        HnswAutoTuningOptions tuningOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 인덱스 성능 메트릭 수집
    /// </summary>
    /// <param name="indexName">인덱스 이름</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>성능 메트릭</returns>
    Task<IndexPerformanceMetrics> CollectPerformanceMetricsAsync(
        string indexName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// HNSW 매개변수
/// </summary>
public class HnswParameters
{
    /// <summary>
    /// 연결 수 (m)
    /// </summary>
    public int M { get; set; } = 16;

    /// <summary>
    /// 구축 시 후보 리스트 크기 (ef_construction)
    /// </summary>
    public int EfConstruction { get; set; } = 64;

    /// <summary>
    /// 검색 시 후보 리스트 크기 (ef_search)
    /// </summary>
    public int EfSearch { get; set; } = 40;

    /// <summary>
    /// 인덱스 빌드 메모리 (MB)
    /// </summary>
    public int MaintenanceWorkMemMB { get; set; } = 1024;

    /// <summary>
    /// 병렬 워커 수
    /// </summary>
    public int MaxParallelWorkers { get; set; } = 4;

    /// <summary>
    /// 매개변수 조합의 고유 식별자
    /// </summary>
    public string GetIdentifier() => $"m{M}_ef{EfConstruction}_search{EfSearch}";

    /// <summary>
    /// 기본 매개변수 생성
    /// </summary>
    public static HnswParameters Default => new();

    /// <summary>
    /// 고성능 매개변수 생성 (더 많은 연결, 더 큰 ef_construction)
    /// </summary>
    public static HnswParameters HighPerformance => new()
    {
        M = 32,
        EfConstruction = 128,
        EfSearch = 80,
        MaintenanceWorkMemMB = 2048,
        MaxParallelWorkers = 8
    };

    /// <summary>
    /// 메모리 효율 매개변수 생성 (적은 연결, 작은 ef_construction)
    /// </summary>
    public static HnswParameters MemoryEfficient => new()
    {
        M = 8,
        EfConstruction = 32,
        EfSearch = 20,
        MaintenanceWorkMemMB = 512,
        MaxParallelWorkers = 2
    };

    /// <summary>
    /// 균형 매개변수 생성 (성능과 메모리 균형)
    /// </summary>
    public static HnswParameters Balanced => new()
    {
        M = 16,
        EfConstruction = 80,
        EfSearch = 50,
        MaintenanceWorkMemMB = 1536,
        MaxParallelWorkers = 6
    };
}

/// <summary>
/// HNSW 벤치마크 옵션
/// </summary>
public class HnswBenchmarkOptions
{
    /// <summary>
    /// 테스트용 벡터 수
    /// </summary>
    public int TestVectorCount { get; set; } = 10000;

    /// <summary>
    /// 벡터 차원 수
    /// </summary>
    public int VectorDimensions { get; set; } = 384;

    /// <summary>
    /// 검색 쿼리 수
    /// </summary>
    public int QueryCount { get; set; } = 100;

    /// <summary>
    /// K (검색할 근사 이웃 수)
    /// </summary>
    public int TopK { get; set; } = 10;

    /// <summary>
    /// 워밍업 쿼리 수
    /// </summary>
    public int WarmupQueries { get; set; } = 10;

    /// <summary>
    /// 인덱스 재생성 여부
    /// </summary>
    public bool RecreateIndex { get; set; } = true;

    /// <summary>
    /// 벤치마크 반복 횟수
    /// </summary>
    public int Iterations { get; set; } = 3;

    /// <summary>
    /// 메모리 사용량 모니터링 여부
    /// </summary>
    public bool MonitorMemoryUsage { get; set; } = true;

    /// <summary>
    /// 정확도 측정을 위한 브루트포스 검색 수행 여부
    /// </summary>
    public bool MeasureAccuracy { get; set; } = true;

    /// <summary>
    /// 테스트 데이터셋 시드 (재현 가능한 결과를 위해)
    /// </summary>
    public int RandomSeed { get; set; } = 42;
}

/// <summary>
/// HNSW 자동 튜닝 옵션
/// </summary>
public class HnswAutoTuningOptions
{
    /// <summary>
    /// 목표 검색 시간 (밀리초)
    /// </summary>
    public double TargetQueryTimeMs { get; set; } = 50.0;

    /// <summary>
    /// 최소 recall 요구사항
    /// </summary>
    public double MinRecallRequired { get; set; } = 0.90;

    /// <summary>
    /// 최대 메모리 사용량 (MB)
    /// </summary>
    public long MaxMemoryUsageMB { get; set; } = 4096;

    /// <summary>
    /// 최대 인덱스 빌드 시간 (분)
    /// </summary>
    public double MaxBuildTimeMinutes { get; set; } = 30.0;

    /// <summary>
    /// 튜닝 전략
    /// </summary>
    public TuningStrategy Strategy { get; set; } = TuningStrategy.BalancedOptimization;

    /// <summary>
    /// 벤치마크 옵션
    /// </summary>
    public HnswBenchmarkOptions BenchmarkOptions { get; set; } = new();

    /// <summary>
    /// 최대 튜닝 시도 횟수
    /// </summary>
    public int MaxTuningAttempts { get; set; } = 20;
}

/// <summary>
/// 튜닝 전략
/// </summary>
public enum TuningStrategy
{
    /// <summary>
    /// 속도 우선 최적화
    /// </summary>
    SpeedOptimization,

    /// <summary>
    /// 정확도 우선 최적화
    /// </summary>
    AccuracyOptimization,

    /// <summary>
    /// 메모리 효율 최적화
    /// </summary>
    MemoryOptimization,

    /// <summary>
    /// 균형 최적화 (기본값)
    /// </summary>
    BalancedOptimization
}

/// <summary>
/// HNSW 벤치마크 결과
/// </summary>
public class HnswBenchmarkResult
{
    /// <summary>
    /// 사용된 매개변수
    /// </summary>
    public HnswParameters Parameters { get; set; } = new();

    /// <summary>
    /// 인덱스 생성 시간 (밀리초)
    /// </summary>
    public double IndexBuildTimeMs { get; set; }

    /// <summary>
    /// 평균 검색 시간 (밀리초)
    /// </summary>
    public double AverageQueryTimeMs { get; set; }

    /// <summary>
    /// P95 검색 시간 (밀리초)
    /// </summary>
    public double P95QueryTimeMs { get; set; }

    /// <summary>
    /// P99 검색 시간 (밀리초)
    /// </summary>
    public double P99QueryTimeMs { get; set; }

    /// <summary>
    /// Recall@K
    /// </summary>
    public double RecallAtK { get; set; }

    /// <summary>
    /// 인덱스 크기 (바이트)
    /// </summary>
    public long IndexSizeBytes { get; set; }

    /// <summary>
    /// 메모리 사용량 (바이트)
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// 초당 쿼리 처리 수 (QPS)
    /// </summary>
    public double QueriesPerSecond => 1000.0 / AverageQueryTimeMs;

    /// <summary>
    /// 성능 점수 (높을수록 좋음)
    /// </summary>
    public double PerformanceScore { get; set; }

    /// <summary>
    /// 벤치마크 실행 시간
    /// </summary>
    public DateTime BenchmarkTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 벤치마크 메타데이터
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool IsSuccessful { get; set; } = true;

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 성능 점수 계산 (recall, 속도, 메모리 효율성 종합)
    /// </summary>
    public double CalculatePerformanceScore(double recallWeight = 0.4, double speedWeight = 0.4, double memoryWeight = 0.2)
    {
        if (!IsSuccessful) return 0.0;

        // Recall 점수 (0-1)
        var recallScore = RecallAtK;

        // 속도 점수 (빠를수록 높은 점수, 최대 100ms 기준)
        var speedScore = Math.Max(0, Math.Min(1.0, (100.0 - AverageQueryTimeMs) / 100.0));

        // 메모리 효율성 점수 (작을수록 높은 점수, 최대 100MB 기준)
        var memoryScore = Math.Max(0, Math.Min(1.0, (100 * 1024 * 1024 - MemoryUsageBytes) / (100.0 * 1024 * 1024)));

        PerformanceScore = (recallScore * recallWeight) + (speedScore * speedWeight) + (memoryScore * memoryWeight);
        return PerformanceScore;
    }

    /// <summary>
    /// 벤치마크 결과 요약 생성
    /// </summary>
    public string GenerateSummary()
    {
        if (!IsSuccessful)
            return $"❌ 벤치마크 실패: {ErrorMessage}";

        return $@"📊 HNSW 벤치마크 결과 ({Parameters.GetIdentifier()})
🎯 성능 점수: {PerformanceScore:F3}
📈 Recall@{10}: {RecallAtK:P2}
⚡ 평균 쿼리 시간: {AverageQueryTimeMs:F2}ms
🚀 QPS: {QueriesPerSecond:F0}
🏗️ 인덱스 빌드 시간: {IndexBuildTimeMs / 1000:F1}초
💾 인덱스 크기: {IndexSizeBytes / (1024 * 1024):F1}MB
🧠 메모리 사용량: {MemoryUsageBytes / (1024 * 1024):F1}MB";
    }
}

/// <summary>
/// 인덱스 성능 메트릭
/// </summary>
public class IndexPerformanceMetrics
{
    /// <summary>
    /// 인덱스 이름
    /// </summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>
    /// 인덱스 타입
    /// </summary>
    public string IndexType { get; set; } = string.Empty;

    /// <summary>
    /// 인덱스 크기 (바이트)
    /// </summary>
    public long IndexSize { get; set; }

    /// <summary>
    /// 튜플 수
    /// </summary>
    public long TupleCount { get; set; }

    /// <summary>
    /// 인덱스 스캔 수
    /// </summary>
    public long IndexScans { get; set; }

    /// <summary>
    /// 인덱스에서 읽은 튜플 수
    /// </summary>
    public long TuplesRead { get; set; }

    /// <summary>
    /// 인덱스에서 가져온 튜플 수
    /// </summary>
    public long TuplesFetched { get; set; }

    /// <summary>
    /// 마지막 분석 시간
    /// </summary>
    public DateTime LastAnalyzed { get; set; }

    /// <summary>
    /// 마지막 자동 분석 시간
    /// </summary>
    public DateTime LastAutoAnalyzed { get; set; }

    /// <summary>
    /// 수집 시간
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 추가 메트릭
    /// </summary>
    public Dictionary<string, object> AdditionalMetrics { get; set; } = new();

    /// <summary>
    /// 인덱스 효율성 점수 (0-1)
    /// </summary>
    public double GetEfficiencyScore()
    {
        if (IndexScans == 0 || TuplesRead == 0) return 0.0;

        // 스캔 당 평균 튜플 읽기 수가 적을수록 효율적
        var avgTuplesPerScan = (double)TuplesRead / IndexScans;

        // 가져온 튜플 대비 읽은 튜플 비율 (높을수록 효율적)
        var fetchRatio = TuplesFetched > 0 ? (double)TuplesFetched / TuplesRead : 0.0;

        return Math.Min(1.0, fetchRatio * (1.0 / Math.Log10(Math.Max(10, avgTuplesPerScan))));
    }
}