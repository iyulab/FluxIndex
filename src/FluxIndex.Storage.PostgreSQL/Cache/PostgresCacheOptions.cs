using FluxIndex.Core.Constants;

namespace FluxIndex.Storage.PostgreSQL.Cache;

/// <summary>
/// PostgreSQL 시맨틱 캐시 옵션
/// </summary>
public class PostgresCacheOptions
{
    /// <summary>
    /// PostgreSQL 연결 문자열
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 시작 시 자동 마이그레이션 (기본: true)
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// 명령 타임아웃 (초, 기본: 30)
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// 기본 캐시 만료 시간 (기본: 1시간)
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 유사도 검색 임계값 (기본: 0.85)
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.85f;

    /// <summary>
    /// 최대 캐시 항목 수 (기본: 50000)
    /// </summary>
    public int MaxEntries { get; set; } = 50000;

    /// <summary>
    /// 자동 정리 활성화 (기본: true)
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 자동 정리 주기 (기본: 5분)
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// UNLOGGED 테이블 사용 (더 빠른 쓰기, 크래시 시 데이터 손실 가능)
    /// 캐시에 적합 - 재구축 가능한 데이터이므로 성능 우선
    /// </summary>
    public bool UseUnloggedTable { get; set; } = true;

    /// <summary>
    /// pgvector 확장 사용 (벡터 유사도 검색)
    /// </summary>
    public bool UsePgVector { get; set; } = true;

    /// <summary>
    /// 임베딩 차원 (기본: 1536)
    /// </summary>
    public int EmbeddingDimensions { get; set; } = EmbeddingDefaults.DefaultVectorDimension;
}
