namespace FluxIndex.Stack.Shared.DTOs.Analytics;

/// <summary>
/// System statistics DTO for dashboard.
/// </summary>
public class SystemStatsDto
{
    public int TotalDocuments { get; init; }
    public int TotalChunks { get; init; }
    public int TotalCollections { get; init; }
    public long TotalStorageBytes { get; init; }
    public int IndexedDocuments { get; init; }
    public int PendingDocuments { get; init; }
    public int FailedDocuments { get; init; }
}

/// <summary>
/// Search analytics DTO.
/// </summary>
public class SearchAnalyticsDto
{
    public int TotalSearches { get; init; }
    public double AverageExecutionTimeMs { get; init; }
    public double AverageResultCount { get; init; }
    public List<TopQueryDto> TopQueries { get; init; } = new();
    public List<SearchTrendDto> DailyTrends { get; init; } = new();
}

/// <summary>
/// Top query DTO for analytics.
/// </summary>
public class TopQueryDto
{
    public string Query { get; init; } = string.Empty;
    public int Count { get; init; }
    public double AverageExecutionTimeMs { get; init; }
}

/// <summary>
/// Daily search trend DTO.
/// </summary>
public class SearchTrendDto
{
    public DateTime Date { get; init; }
    public int SearchCount { get; init; }
    public double AverageExecutionTimeMs { get; init; }
}

/// <summary>
/// Document analytics DTO.
/// </summary>
public class DocumentAnalyticsDto
{
    public List<DocumentTypeStatsDto> BySourceType { get; init; } = new();
    public List<DocumentStatusStatsDto> ByStatus { get; init; } = new();
    public List<DocumentTrendDto> DailyUploads { get; init; } = new();
}

/// <summary>
/// Document type statistics.
/// </summary>
public class DocumentTypeStatsDto
{
    public string SourceType { get; init; } = string.Empty;
    public int Count { get; init; }
    public long TotalSizeBytes { get; init; }
}

/// <summary>
/// Document status statistics.
/// </summary>
public class DocumentStatusStatsDto
{
    public string Status { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// Daily document upload trend.
/// </summary>
public class DocumentTrendDto
{
    public DateTime Date { get; init; }
    public int UploadCount { get; init; }
    public int IndexedCount { get; init; }
}

/// <summary>
/// Semantic cache statistics DTO for monitoring cache performance.
/// </summary>
public class SemanticCacheStatsDto
{
    /// <summary>
    /// Total number of cached entries.
    /// </summary>
    public long TotalEntries { get; init; }

    /// <summary>
    /// Number of cache hits.
    /// </summary>
    public long CacheHits { get; init; }

    /// <summary>
    /// Number of cache misses.
    /// </summary>
    public long CacheMisses { get; init; }

    /// <summary>
    /// Cache hit rate (0.0 - 1.0).
    /// </summary>
    public float HitRate { get; init; }

    /// <summary>
    /// Average response time in milliseconds.
    /// </summary>
    public float AverageResponseTimeMs { get; init; }

    /// <summary>
    /// Cache size in bytes.
    /// </summary>
    public long CacheSizeBytes { get; init; }

    /// <summary>
    /// Number of expired entries.
    /// </summary>
    public long ExpiredEntries { get; init; }

    /// <summary>
    /// Average similarity score for cache hits.
    /// </summary>
    public float AverageSimilarityScore { get; init; }

    /// <summary>
    /// Top performing queries with hit counts.
    /// </summary>
    public List<CacheQueryPerformanceDto> TopPerformingQueries { get; init; } = new();

    /// <summary>
    /// Timestamp when statistics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; }
}

/// <summary>
/// Query performance metrics for semantic cache.
/// </summary>
public class CacheQueryPerformanceDto
{
    /// <summary>
    /// The query string.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Number of cache hits for this query.
    /// </summary>
    public int HitCount { get; init; }

    /// <summary>
    /// Average similarity score when matched.
    /// </summary>
    public float AverageSimilarity { get; init; }

    /// <summary>
    /// Average response time in milliseconds.
    /// </summary>
    public float AverageResponseTimeMs { get; init; }

    /// <summary>
    /// Last time this query was accessed.
    /// </summary>
    public DateTime LastUsedAt { get; init; }
}
