namespace FluxIndex.Cache.Redis.Configuration;

/// <summary>
/// Options for RedisCacheStore - unified cache for polyglot persistence.
/// </summary>
public class RedisCacheStoreOptions
{
    /// <summary>
    /// Redis connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Key prefix for all cache entries.
    /// </summary>
    public string KeyPrefix { get; set; } = "fluxindex:cache:";

    /// <summary>
    /// Redis database number.
    /// </summary>
    public int DatabaseNumber { get; set; }

    /// <summary>
    /// Default TTL for general cache entries.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// TTL for embedding cache entries.
    /// </summary>
    public TimeSpan EmbeddingCacheTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// TTL for hot data cache entries.
    /// </summary>
    public TimeSpan HotDataTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// TTL for query result cache entries.
    /// </summary>
    public TimeSpan QueryResultTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// TTL for entity cache entries.
    /// </summary>
    public TimeSpan EntityCacheTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Maximum keys to scan for similarity search.
    /// </summary>
    public int MaxSimilaritySearchKeys { get; set; } = 1000;

    /// <summary>
    /// Maximum parallel operations.
    /// </summary>
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Command timeout in seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Enable detailed logging.
    /// </summary>
    public bool EnableDetailedLogging { get; set; }
}
