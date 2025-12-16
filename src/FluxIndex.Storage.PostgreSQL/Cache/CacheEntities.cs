using Pgvector;

namespace FluxIndex.Storage.PostgreSQL.Cache;

/// <summary>
/// PostgreSQL용 시맨틱 캐시 엔티티 (pgvector 활용)
/// </summary>
public class SemanticCacheEntity
{
    public string Id { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 쿼리 임베딩 (pgvector)
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>
    /// 캐시된 결과 (JSONB)
    /// </summary>
    public List<object>? Results { get; set; }

    /// <summary>
    /// 메타데이터 (JSONB)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public int HitCount { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 캐시 통계 저장용 엔티티
/// </summary>
public class CacheStatsEntity
{
    public int Id { get; set; } = 1; // 싱글톤 레코드
    public long TotalHits { get; set; }
    public long TotalMisses { get; set; }
    public long TotalEvictions { get; set; }
    public long TotalEntries { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
