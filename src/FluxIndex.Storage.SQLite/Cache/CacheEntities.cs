using System.Text.Json;

namespace FluxIndex.Storage.SQLite.Cache;

/// <summary>
/// SQLite용 시맨틱 캐시 엔티티
/// </summary>
public class SemanticCacheEntity
{
    public string Id { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string EmbeddingJson { get; set; } = "[]";

    /// <summary>The cached results in the form <c>SemanticCacheJson</c> writes; a row it cannot read is a miss.</summary>
    public string ResultsJson { get; set; } = "[]";

    /// <summary>The <c>SearchMetadata</c> stored with the results, in the form <c>SemanticCacheJson</c> writes.</summary>
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public int HitCount { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    public float[] GetEmbedding() =>
        JsonSerializer.Deserialize<float[]>(EmbeddingJson) ?? Array.Empty<float>();

    public void SetEmbedding(float[] embedding) =>
        EmbeddingJson = JsonSerializer.Serialize(embedding);
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
