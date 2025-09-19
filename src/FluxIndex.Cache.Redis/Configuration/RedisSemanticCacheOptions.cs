using System;
using System.Collections.Generic;

namespace FluxIndex.Cache.Redis.Configuration;

/// <summary>
/// Redis 시맨틱 캐시 구성 옵션
/// </summary>
public class RedisSemanticCacheOptions
{
    /// <summary>
    /// Redis 연결 문자열
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// 캐시 키 접두사
    /// </summary>
    public string KeyPrefix { get; set; } = "fluxindex:semantic:";

    /// <summary>
    /// 기본 유사도 임계값 (0.0 ~ 1.0)
    /// </summary>
    public float DefaultSimilarityThreshold { get; set; } = 0.95f;

    /// <summary>
    /// 기본 캐시 TTL (생존 시간)
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 최대 캐시 엔트리 수 (0은 무제한)
    /// </summary>
    public long MaxCacheEntries { get; set; } = 10000;

    /// <summary>
    /// 캐시 정리 임계값 (전체 캐시의 몇 퍼센트에서 정리할지)
    /// </summary>
    public double CleanupThreshold { get; set; } = 0.8;

    /// <summary>
    /// 캐시 정리 시 제거할 엔트리 비율
    /// </summary>
    public double CleanupRatio { get; set; } = 0.2;

    /// <summary>
    /// 병렬 처리 시 최대 동시 작업 수
    /// </summary>
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 통계 수집 간격
    /// </summary>
    public TimeSpan StatisticsInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 벡터 압축 사용 여부
    /// </summary>
    public bool EnableVectorCompression { get; set; } = true;

    /// <summary>
    /// 쿼리 정규화 사용 여부
    /// </summary>
    public bool EnableQueryNormalization { get; set; } = true;

    /// <summary>
    /// 도메인별 가중치 맵
    /// </summary>
    public Dictionary<string, float> DomainWeights { get; set; } = new();

    /// <summary>
    /// 캐시 워밍업 시 사용할 인기 쿼리 목록
    /// </summary>
    public List<string> WarmupQueries { get; set; } = new();

    /// <summary>
    /// Redis 데이터베이스 번호
    /// </summary>
    public int DatabaseNumber { get; set; } = 0;

    /// <summary>
    /// 연결 타임아웃 (초)
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 명령 타임아웃 (초)
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 재시도 횟수
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// 재시도 간 대기 시간
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 캐시 압축 및 정리 자동 실행 여부
    /// </summary>
    public bool EnableAutoCompaction { get; set; } = true;

    /// <summary>
    /// 자동 압축 실행 간격
    /// </summary>
    public TimeSpan AutoCompactionInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// 메트릭 수집 활성화 여부
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 상세 로깅 활성화 여부
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}