using System;
using System.Collections.Generic;

namespace FluxIndex.Domain.Models;

/// <summary>
/// HNSW 매개변수
/// </summary>
public class HnswParameters
{
    /// <summary>
    /// M - 그래프의 연결 수
    /// </summary>
    public int M { get; set; } = 16;

    /// <summary>
    /// efConstruction - 구성 시 탐색할 후보 수
    /// </summary>
    public int EfConstruction { get; set; } = 200;

    /// <summary>
    /// ef - 검색 시 탐색할 후보 수
    /// </summary>
    public int Ef { get; set; } = 100;

    /// <summary>
    /// EfSearch - 검색 시 탐색할 후보 수 (Ef와 동일)
    /// </summary>
    public int EfSearch
    {
        get => Ef;
        set => Ef = value;
    }

    /// <summary>
    /// 거리 함수
    /// </summary>
    public string DistanceFunction { get; set; } = "cosine";

    /// <summary>
    /// 최대 레이어 수
    /// </summary>
    public int MaxLayers { get; set; } = 5;

    /// <summary>
    /// 레벨 생성 팩터
    /// </summary>
    public double LevelGenerationFactor { get; set; } = 1.0 / Math.Log(2);

    /// <summary>
    /// 매개변수 조합의 고유 식별자
    /// </summary>
    public string GetIdentifier() => $"m{M}_ef{EfConstruction}_search{Ef}";

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
        EfConstruction = 400,
        Ef = 200,
        MaxLayers = 6
    };

    /// <summary>
    /// 메모리 효율 매개변수 생성 (적은 연결, 작은 ef_construction)
    /// </summary>
    public static HnswParameters MemoryEfficient => new()
    {
        M = 8,
        EfConstruction = 100,
        Ef = 50,
        MaxLayers = 4
    };

    /// <summary>
    /// 균형 매개변수 생성 (성능과 메모리 균형)
    /// </summary>
    public static HnswParameters Balanced => new()
    {
        M = 16,
        EfConstruction = 200,
        Ef = 100,
        MaxLayers = 5
    };
}

/// <summary>
/// HNSW 벤치마크 옵션
/// </summary>
public class HnswBenchmarkOptions
{
    /// <summary>
    /// 테스트할 매개변수 세트
    /// </summary>
    public List<HnswParameters> ParameterSets { get; set; } = new();

    /// <summary>
    /// 테스트 쿼리 수
    /// </summary>
    public int TestQueryCount { get; set; } = 100;

    /// <summary>
    /// 검색 쿼리 수 (TestQueryCount와 동일)
    /// </summary>
    public int QueryCount
    {
        get => TestQueryCount;
        set => TestQueryCount = value;
    }

    /// <summary>
    /// 정확도 기준 (k 값)
    /// </summary>
    public int AccuracyK { get; set; } = 10;

    /// <summary>
    /// 최대 테스트 시간 (밀리초)
    /// </summary>
    public int MaxTestTimeMs { get; set; } = 30000;

    /// <summary>
    /// 워밍업 쿼리 수
    /// </summary>
    public int WarmupQueries { get; set; } = 10;

    /// <summary>
    /// 벡터 차원 수
    /// </summary>
    public int VectorDimensions { get; set; } = 384;

    /// <summary>
    /// 테스트용 벡터 수
    /// </summary>
    public int TestVectorCount { get; set; } = 10000;

    /// <summary>
    /// K (검색할 근사 이웃 수)
    /// </summary>
    public int TopK { get; set; } = 10;

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
/// HNSW 벤치마크 결과
/// </summary>
public class HnswBenchmarkResult
{
    /// <summary>
    /// 테스트된 매개변수
    /// </summary>
    public HnswParameters Parameters { get; init; } = new();

    /// <summary>
    /// 평균 쿼리 시간 (밀리초)
    /// </summary>
    public double AverageQueryTimeMs { get; init; }

    /// <summary>
    /// 정확도 (Recall@K)
    /// </summary>
    public double Accuracy { get; init; }

    /// <summary>
    /// Recall@K (Accuracy와 동일)
    /// </summary>
    public double RecallAtK => Accuracy;

    /// <summary>
    /// 초당 쿼리 수 (QPS)
    /// </summary>
    public double QueriesPerSecond { get; init; }

    /// <summary>
    /// 인덱스 구축 시간 (밀리초)
    /// </summary>
    public double IndexBuildTimeMs { get; init; }

    /// <summary>
    /// 메모리 사용량 (바이트)
    /// </summary>
    public long MemoryUsageBytes { get; init; }

    /// <summary>
    /// 테스트 시간
    /// </summary>
    public DateTime TestTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 성공 여부 (Success와 동일)
    /// </summary>
    public bool IsSuccessful => Success;

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 상세 메트릭
    /// </summary>
    public Dictionary<string, object> DetailedMetrics { get; init; } = new();

    /// <summary>
    /// 성능 점수 (높을수록 좋음)
    /// </summary>
    public double PerformanceScore { get; set; }

    /// <summary>
    /// 성능 점수 계산 (recall, 속도, 메모리 효율성 종합)
    /// </summary>
    public double CalculatePerformanceScore(double recallWeight = 0.4, double speedWeight = 0.4, double memoryWeight = 0.2)
    {
        if (!Success) return 0.0;

        var recallScore = RecallAtK;
        var speedScore = Math.Max(0, Math.Min(1.0, (100.0 - AverageQueryTimeMs) / 100.0));
        var memoryScore = Math.Max(0, Math.Min(1.0, (100 * 1024 * 1024 - MemoryUsageBytes) / (100.0 * 1024 * 1024)));

        PerformanceScore = (recallScore * recallWeight) + (speedScore * speedWeight) + (memoryScore * memoryWeight);
        return PerformanceScore;
    }
}

/// <summary>
/// HNSW 자동 튜닝 옵션
/// </summary>
public class HnswAutoTuningOptions
{
    /// <summary>
    /// 튜닝 전략
    /// </summary>
    public TuningStrategy Strategy { get; set; } = TuningStrategy.BalancedOptimization;

    /// <summary>
    /// 목표 쿼리 시간 (밀리초)
    /// </summary>
    public double TargetQueryTimeMs { get; set; } = 50.0;

    /// <summary>
    /// 최소 요구 정확도
    /// </summary>
    public double MinRecallRequired { get; set; } = 0.90;

    /// <summary>
    /// 최대 메모리 사용량 (바이트)
    /// </summary>
    public long MaxMemoryUsageBytes { get; set; } = 1024 * 1024 * 1024; // 1GB

    /// <summary>
    /// 최대 튜닝 시간 (밀리초)
    /// </summary>
    public int MaxTuningTimeMs { get; set; } = 300000; // 5분

    /// <summary>
    /// 튜닝 반복 수
    /// </summary>
    public int MaxIterations { get; set; } = 20;

    /// <summary>
    /// 최대 튜닝 시도 횟수
    /// </summary>
    public int MaxTuningAttempts { get; set; } = 20;

    /// <summary>
    /// 벤치마크 옵션
    /// </summary>
    public HnswBenchmarkOptions BenchmarkOptions { get; set; } = new();

    /// <summary>
    /// 최대 메모리 사용량 (MB)
    /// </summary>
    public long MaxMemoryUsageMB { get; set; } = 4096;

    /// <summary>
    /// 최대 인덱스 빌드 시간 (분)
    /// </summary>
    public double MaxBuildTimeMinutes { get; set; } = 30.0;
}

/// <summary>
/// 튜닝 전략
/// </summary>
public enum TuningStrategy
{
    /// <summary>
    /// 속도 최적화
    /// </summary>
    SpeedOptimization,

    /// <summary>
    /// 정확도 최적화
    /// </summary>
    AccuracyOptimization,

    /// <summary>
    /// 메모리 최적화
    /// </summary>
    MemoryOptimization,

    /// <summary>
    /// 균형 최적화
    /// </summary>
    BalancedOptimization
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

        var avgTuplesPerScan = (double)TuplesRead / IndexScans;
        var fetchRatio = TuplesFetched > 0 ? (double)TuplesFetched / TuplesRead : 0.0;

        return Math.Min(1.0, fetchRatio * (1.0 / Math.Log10(Math.Max(10, avgTuplesPerScan))));
    }
}