using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Interface for semantic caching that stores query results based on semantic similarity
/// rather than exact string matching
/// </summary>
public interface ISemanticCache
{
    /// <summary>
    /// Retrieves cached results for semantically similar queries
    /// </summary>
    /// <param name="query">The search query</param>
    /// <param name="similarityThreshold">Minimum similarity threshold (0.0 to 1.0)</param>
    /// <param name="maxResults">Maximum number of cached results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached results if found, null otherwise</returns>
    Task<CacheResult?> GetAsync(
        string query, 
        float similarityThreshold = 0.85f,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores query results in the semantic cache
    /// </summary>
    /// <param name="query">The original search query</param>
    /// <param name="results">The search results to cache</param>
    /// <param name="metadata">Optional metadata about the search</param>
    /// <param name="expiry">Cache expiration time (null for default)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetAsync(
        string query,
        IEnumerable<object> results,
        CacheMetadata? metadata = null,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if there are semantically similar queries in the cache
    /// </summary>
    /// <param name="query">Query to check</param>
    /// <param name="threshold">Similarity threshold</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if similar queries exist</returns>
    Task<bool> HasSimilarQueryAsync(
        string query, 
        float threshold = 0.85f, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the most similar queries in the cache
    /// </summary>
    /// <param name="query">Query to find similarities for</param>
    /// <param name="threshold">Minimum similarity threshold</param>
    /// <param name="maxSimilar">Maximum number of similar queries to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of similar cached queries with their similarity scores</returns>
    Task<IEnumerable<SimilarQuery>> FindSimilarQueriesAsync(
        string query,
        float threshold = 0.85f,
        int maxSimilar = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cache entries that match the given pattern or criteria
    /// </summary>
    /// <param name="pattern">Pattern to match queries against</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of invalidated entries</returns>
    Task<int> InvalidateAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cache entries
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics and performance metrics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cache statistics</returns>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the cache by removing expired entries and optimizing storage
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Optimization results</returns>
    Task<CacheOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a cached query result with semantic information
/// </summary>
public class CacheResult
{
    /// <summary>
    /// The original cached query
    /// </summary>
    public string OriginalQuery { get; set; } = string.Empty;

    /// <summary>
    /// Semantic similarity score between the requested query and cached query
    /// </summary>
    public float SimilarityScore { get; set; }

    /// <summary>
    /// The cached search results
    /// </summary>
    public IEnumerable<object> Results { get; set; } = Enumerable.Empty<object>();

    /// <summary>
    /// When this cache entry was created
    /// </summary>
    public DateTime CachedAt { get; set; }

    /// <summary>
    /// When this cache entry expires
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Number of times this cache entry has been accessed
    /// </summary>
    public int HitCount { get; set; }

    /// <summary>
    /// Last time this cache entry was accessed
    /// </summary>
    public DateTime LastAccessedAt { get; set; }

    /// <summary>
    /// Metadata associated with this cache entry
    /// </summary>
    public CacheMetadata? Metadata { get; set; }

    /// <summary>
    /// Whether this cache entry has expired
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    /// <summary>
    /// Age of this cache entry
    /// </summary>
    public TimeSpan Age => DateTime.UtcNow - CachedAt;
}

/// <summary>
/// Metadata about a cached query
/// </summary>
public class CacheMetadata
{
    /// <summary>
    /// Type of search that generated these results
    /// </summary>
    public string? SearchType { get; set; }

    /// <summary>
    /// Search parameters used
    /// </summary>
    public Dictionary<string, object>? SearchParameters { get; set; }

    /// <summary>
    /// Number of results in the cache
    /// </summary>
    public int ResultCount { get; set; }

    /// <summary>
    /// Total search latency in milliseconds
    /// </summary>
    public double SearchLatencyMs { get; set; }

    /// <summary>
    /// Quality metrics for the search results
    /// </summary>
    public Dictionary<string, double>? QualityMetrics { get; set; }

    /// <summary>
    /// Tags for cache organization and invalidation
    /// </summary>
    public HashSet<string>? Tags { get; set; }

    /// <summary>
    /// Custom properties for extensibility
    /// </summary>
    public Dictionary<string, object>? CustomProperties { get; set; }
}

/// <summary>
/// Represents a similar query found in the cache
/// </summary>
public class SimilarQuery
{
    /// <summary>
    /// The similar query text
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Similarity score to the requested query
    /// </summary>
    public float SimilarityScore { get; set; }

    /// <summary>
    /// When this query was cached
    /// </summary>
    public DateTime CachedAt { get; set; }

    /// <summary>
    /// Number of results in this cache entry
    /// </summary>
    public int ResultCount { get; set; }

    /// <summary>
    /// Cache entry identifier
    /// </summary>
    public string CacheKey { get; set; } = string.Empty;
}

/// <summary>
/// Cache performance statistics
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of cached queries
    /// </summary>
    public long TotalQueries { get; set; }

    /// <summary>
    /// Number of cache hits
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Number of cache misses
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Cache hit ratio (hits / (hits + misses))
    /// </summary>
    public double HitRatio => (CacheHits + CacheMisses) > 0 ? 
        (double)CacheHits / (CacheHits + CacheMisses) : 0.0;

    /// <summary>
    /// Total memory used by the cache in bytes
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// Average query similarity score for cache hits
    /// </summary>
    public double AverageSimilarityScore { get; set; }

    /// <summary>
    /// Average time saved by cache hits in milliseconds
    /// </summary>
    public double AverageTimeSavedMs { get; set; }

    /// <summary>
    /// Number of expired entries
    /// </summary>
    public long ExpiredEntries { get; set; }

    /// <summary>
    /// When these statistics were collected
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Cache efficiency metrics
    /// </summary>
    public Dictionary<string, double> EfficiencyMetrics { get; set; } = new();
}

/// <summary>
/// Result of cache optimization operation
/// </summary>
public class CacheOptimizationResult
{
    /// <summary>
    /// Number of entries removed during optimization
    /// </summary>
    public int RemovedEntries { get; set; }

    /// <summary>
    /// Amount of memory freed in bytes
    /// </summary>
    public long MemoryFreedBytes { get; set; }

    /// <summary>
    /// Time taken for optimization in milliseconds
    /// </summary>
    public double OptimizationTimeMs { get; set; }

    /// <summary>
    /// Performance improvements achieved
    /// </summary>
    public Dictionary<string, double> Improvements { get; set; } = new();

    /// <summary>
    /// Whether optimization was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Any errors or warnings during optimization
    /// </summary>
    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// Configuration options for semantic caching
/// </summary>
public class SemanticCacheOptions
{
    /// <summary>
    /// Default cache expiration time
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum number of entries to keep in cache
    /// </summary>
    public int MaxCacheSize { get; set; } = 10000;

    /// <summary>
    /// Maximum memory usage in MB
    /// </summary>
    public int MaxMemoryMB { get; set; } = 512;

    /// <summary>
    /// Default similarity threshold for cache hits
    /// </summary>
    public float DefaultSimilarityThreshold { get; set; } = 0.85f;

    /// <summary>
    /// Minimum query length to cache
    /// </summary>
    public int MinQueryLength { get; set; } = 5;

    /// <summary>
    /// Maximum query length to cache
    /// </summary>
    public int MaxQueryLength { get; set; } = 1000;

    /// <summary>
    /// Enable automatic cache optimization
    /// </summary>
    public bool EnableAutoOptimization { get; set; } = true;

    /// <summary>
    /// Interval for automatic optimization
    /// </summary>
    public TimeSpan AutoOptimizationInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Enable detailed performance tracking
    /// </summary>
    public bool EnablePerformanceTracking { get; set; } = true;

    /// <summary>
    /// Batch size for similarity searches
    /// </summary>
    public int SimilaritySearchBatchSize { get; set; } = 100;

    /// <summary>
    /// Enable compression for cached results
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Compression threshold in bytes
    /// </summary>
    public int CompressionThreshold { get; set; } = 1024;
}

/// <summary>
/// Cache eviction policies
/// </summary>
public enum CacheEvictionPolicy
{
    /// <summary>
    /// Least Recently Used - evict oldest accessed entries
    /// </summary>
    LRU,

    /// <summary>
    /// Least Frequently Used - evict least accessed entries
    /// </summary>
    LFU,

    /// <summary>
    /// Time-based - evict based on expiration time
    /// </summary>
    TTL,

    /// <summary>
    /// Similarity-based - evict entries with lowest average similarity
    /// </summary>
    SimilarityBased,

    /// <summary>
    /// Custom eviction logic
    /// </summary>
    Custom
}