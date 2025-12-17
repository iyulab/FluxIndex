namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Unified cache store interface for polyglot persistence.
/// Combines basic caching, semantic caching, embedding caching, and hot data management.
/// Implementations: Redis (primary), PostgreSQL (fallback), In-Memory (L1).
/// </summary>
public interface ICacheStore : ICacheService
{
    #region Embedding Cache

    /// <summary>
    /// Caches an embedding vector for a content hash.
    /// </summary>
    Task SetEmbeddingAsync(
        string contentHash,
        float[] embedding,
        EmbeddingCacheMetadata? metadata = null,
        TimeSpan? expiry = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a cached embedding by content hash.
    /// </summary>
    Task<EmbeddingCacheEntry?> GetEmbeddingAsync(
        string contentHash,
        CancellationToken ct = default);

    /// <summary>
    /// Batch retrieves embeddings for multiple content hashes.
    /// </summary>
    Task<IReadOnlyDictionary<string, EmbeddingCacheEntry>> GetEmbeddingsBatchAsync(
        IEnumerable<string> contentHashes,
        CancellationToken ct = default);

    /// <summary>
    /// Batch caches embeddings.
    /// </summary>
    Task SetEmbeddingsBatchAsync(
        IEnumerable<(string ContentHash, float[] Embedding, EmbeddingCacheMetadata? Metadata)> embeddings,
        TimeSpan? expiry = null,
        CancellationToken ct = default);

    /// <summary>
    /// Removes an embedding from cache.
    /// </summary>
    Task<bool> RemoveEmbeddingAsync(string contentHash, CancellationToken ct = default);

    #endregion

    #region Hot Data Cache

    /// <summary>
    /// Marks a chunk as frequently accessed (hot).
    /// </summary>
    Task MarkChunkAsHotAsync(
        string chunkId,
        HotDataPriority priority = HotDataPriority.Medium,
        CancellationToken ct = default);

    /// <summary>
    /// Gets cached hot chunk data.
    /// </summary>
    Task<HotChunkData?> GetHotChunkAsync(
        string chunkId,
        CancellationToken ct = default);

    /// <summary>
    /// Batch retrieves hot chunk data.
    /// </summary>
    Task<IReadOnlyDictionary<string, HotChunkData>> GetHotChunksBatchAsync(
        IEnumerable<string> chunkIds,
        CancellationToken ct = default);

    /// <summary>
    /// Caches chunk data with hot status.
    /// </summary>
    Task SetHotChunkAsync(
        HotChunkData chunkData,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the most frequently accessed chunks.
    /// </summary>
    Task<IReadOnlyList<HotChunkData>> GetTopHotChunksAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Evicts cold (infrequently accessed) chunks from hot cache.
    /// </summary>
    Task<int> EvictColdChunksAsync(
        TimeSpan accessThreshold,
        CancellationToken ct = default);

    #endregion

    #region Query Result Cache

    /// <summary>
    /// Caches search query results with semantic indexing.
    /// </summary>
    Task SetQueryResultAsync(
        string queryHash,
        float[] queryEmbedding,
        QueryResultCacheEntry entry,
        TimeSpan? expiry = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves exact match query results.
    /// </summary>
    Task<QueryResultCacheEntry?> GetQueryResultAsync(
        string queryHash,
        CancellationToken ct = default);

    /// <summary>
    /// Finds semantically similar cached query results.
    /// </summary>
    Task<IReadOnlyList<SimilarQueryResult>> FindSimilarQueryResultsAsync(
        float[] queryEmbedding,
        float similarityThreshold = 0.85f,
        int maxResults = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates query results for specific documents.
    /// </summary>
    Task<int> InvalidateQueryResultsForDocumentsAsync(
        IEnumerable<string> documentIds,
        CancellationToken ct = default);

    #endregion

    #region Entity Cache

    /// <summary>
    /// Caches entity data for quick lookup.
    /// </summary>
    Task SetEntityAsync(
        string entityId,
        CachedEntityData entityData,
        TimeSpan? expiry = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves cached entity data.
    /// </summary>
    Task<CachedEntityData?> GetEntityAsync(
        string entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Batch retrieves cached entities.
    /// </summary>
    Task<IReadOnlyDictionary<string, CachedEntityData>> GetEntitiesBatchAsync(
        IEnumerable<string> entityIds,
        CancellationToken ct = default);

    #endregion

    #region Statistics and Maintenance

    /// <summary>
    /// Gets comprehensive cache store statistics.
    /// </summary>
    Task<CacheStoreStatistics> GetCacheStoreStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// Performs cache store maintenance (cleanup, optimization).
    /// </summary>
    Task<CacheMaintenanceResult> PerformMaintenanceAsync(
        CacheMaintenanceOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Warms up cache with frequently accessed data.
    /// </summary>
    Task<CacheWarmupResult> WarmupAsync(
        CacheWarmupOptions options,
        CancellationToken ct = default);

    #endregion
}

#region Supporting Types

/// <summary>
/// Cached embedding entry with metadata.
/// </summary>
public record EmbeddingCacheEntry
{
    /// <summary>Content hash (key)</summary>
    public required string ContentHash { get; init; }

    /// <summary>Embedding vector</summary>
    public required float[] Embedding { get; init; }

    /// <summary>Metadata about the embedding</summary>
    public EmbeddingCacheMetadata? Metadata { get; init; }

    /// <summary>When cached</summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When expires</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Access count</summary>
    public int AccessCount { get; init; }
}

/// <summary>
/// Metadata for cached embeddings.
/// </summary>
public record EmbeddingCacheMetadata
{
    /// <summary>Model used to generate embedding</summary>
    public string? ModelId { get; init; }

    /// <summary>Embedding dimension</summary>
    public int Dimension { get; init; }

    /// <summary>Original content length</summary>
    public int ContentLength { get; init; }

    /// <summary>Embedding type (content, contextual, hypothetical, etc.)</summary>
    public EmbeddingType Type { get; init; } = EmbeddingType.Content;

    /// <summary>Source chunk/document ID</summary>
    public string? SourceId { get; init; }

    /// <summary>Generation timestamp</summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Types of embeddings for multi-representation indexing.
/// </summary>
public enum EmbeddingType
{
    /// <summary>Direct content embedding</summary>
    Content,

    /// <summary>Contextual embedding (with surrounding context)</summary>
    Contextual,

    /// <summary>Hypothetical document embedding (HyDE)</summary>
    Hypothetical,

    /// <summary>Entity-based embedding</summary>
    Entity,

    /// <summary>Summary embedding</summary>
    Summary,

    /// <summary>Query embedding</summary>
    Query
}

/// <summary>
/// Priority level for hot data caching.
/// </summary>
public enum HotDataPriority
{
    /// <summary>Low priority - may be evicted first</summary>
    Low = 0,

    /// <summary>Medium priority - standard caching</summary>
    Medium = 1,

    /// <summary>High priority - kept longer</summary>
    High = 2,

    /// <summary>Critical - never automatically evicted</summary>
    Critical = 3
}

/// <summary>
/// Hot chunk data for frequently accessed chunks.
/// </summary>
public record HotChunkData
{
    /// <summary>Chunk ID</summary>
    public required string ChunkId { get; init; }

    /// <summary>Document ID</summary>
    public required string DocumentId { get; init; }

    /// <summary>Chunk content</summary>
    public required string Content { get; init; }

    /// <summary>Pre-computed embeddings (multiple representations)</summary>
    public IReadOnlyDictionary<EmbeddingType, float[]> Embeddings { get; init; } = new Dictionary<EmbeddingType, float[]>();

    /// <summary>Chunk metadata</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <summary>Priority level</summary>
    public HotDataPriority Priority { get; init; } = HotDataPriority.Medium;

    /// <summary>Access count since caching</summary>
    public int AccessCount { get; init; }

    /// <summary>Last access time</summary>
    public DateTimeOffset LastAccessedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When cached</summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Cached query result entry.
/// </summary>
public record QueryResultCacheEntry
{
    /// <summary>Original query text</summary>
    public required string Query { get; init; }

    /// <summary>Query hash (key)</summary>
    public required string QueryHash { get; init; }

    /// <summary>Query embedding for similarity search</summary>
    public float[]? QueryEmbedding { get; init; }

    /// <summary>Cached chunk IDs in ranked order</summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];

    /// <summary>Relevance scores for each chunk</summary>
    public IReadOnlyList<float> Scores { get; init; } = [];

    /// <summary>Search parameters used</summary>
    public IReadOnlyDictionary<string, object> SearchParameters { get; init; } = new Dictionary<string, object>();

    /// <summary>Total result count (before pagination)</summary>
    public int TotalCount { get; init; }

    /// <summary>When cached</summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When expires</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Access count</summary>
    public int AccessCount { get; init; }

    /// <summary>Search latency in milliseconds</summary>
    public double SearchLatencyMs { get; init; }
}

/// <summary>
/// Similar query result from semantic cache lookup.
/// </summary>
public record SimilarQueryResult
{
    /// <summary>The cached query entry</summary>
    public required QueryResultCacheEntry Entry { get; init; }

    /// <summary>Similarity score to the input query</summary>
    public float SimilarityScore { get; init; }
}

/// <summary>
/// Cached entity data.
/// </summary>
public record CachedEntityData
{
    /// <summary>Entity ID</summary>
    public required string EntityId { get; init; }

    /// <summary>Entity name</summary>
    public required string Name { get; init; }

    /// <summary>Entity type</summary>
    public NamedEntityType Type { get; init; }

    /// <summary>Entity embedding</summary>
    public float[]? Embedding { get; init; }

    /// <summary>Related chunk IDs</summary>
    public IReadOnlyList<string> ChunkIds { get; init; } = [];

    /// <summary>Related entity IDs</summary>
    public IReadOnlyList<string> RelatedEntityIds { get; init; } = [];

    /// <summary>Importance score</summary>
    public double ImportanceScore { get; init; }

    /// <summary>When cached</summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Access count</summary>
    public int AccessCount { get; init; }
}

/// <summary>
/// Comprehensive cache store statistics.
/// </summary>
public record CacheStoreStatistics
{
    /// <summary>Embedding cache statistics</summary>
    public CacheLayerStats EmbeddingCache { get; init; } = new();

    /// <summary>Hot data cache statistics</summary>
    public CacheLayerStats HotDataCache { get; init; } = new();

    /// <summary>Query result cache statistics</summary>
    public CacheLayerStats QueryResultCache { get; init; } = new();

    /// <summary>Entity cache statistics</summary>
    public CacheLayerStats EntityCache { get; init; } = new();

    /// <summary>Total memory usage in bytes</summary>
    public long TotalMemoryUsageBytes { get; init; }

    /// <summary>Overall hit ratio</summary>
    public double OverallHitRatio { get; init; }

    /// <summary>When statistics were collected</summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Statistics for a single cache layer.
/// </summary>
public record CacheLayerStats
{
    /// <summary>Number of entries</summary>
    public long EntryCount { get; init; }

    /// <summary>Number of hits</summary>
    public long Hits { get; init; }

    /// <summary>Number of misses</summary>
    public long Misses { get; init; }

    /// <summary>Hit ratio</summary>
    public double HitRatio => (Hits + Misses) > 0 ? (double)Hits / (Hits + Misses) : 0.0;

    /// <summary>Memory usage in bytes</summary>
    public long MemoryUsageBytes { get; init; }

    /// <summary>Average entry size in bytes</summary>
    public long AverageEntrySizeBytes { get; init; }

    /// <summary>Eviction count</summary>
    public long EvictionCount { get; init; }
}

/// <summary>
/// Options for cache maintenance operations.
/// </summary>
public record CacheMaintenanceOptions
{
    /// <summary>Remove expired entries</summary>
    public bool RemoveExpired { get; init; } = true;

    /// <summary>Evict entries below access threshold</summary>
    public bool EvictColdData { get; init; } = true;

    /// <summary>Access threshold for cold data eviction</summary>
    public TimeSpan ColdDataThreshold { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Target memory usage percentage (0-100)</summary>
    public int TargetMemoryUsagePercent { get; init; } = 80;

    /// <summary>Compact data structures</summary>
    public bool CompactStorage { get; init; } = false;

    /// <summary>Update statistics</summary>
    public bool UpdateStatistics { get; init; } = true;
}

/// <summary>
/// Result of cache maintenance operation.
/// </summary>
public record CacheMaintenanceResult
{
    /// <summary>Entries removed</summary>
    public int EntriesRemoved { get; init; }

    /// <summary>Memory freed in bytes</summary>
    public long MemoryFreedBytes { get; init; }

    /// <summary>Duration in milliseconds</summary>
    public double DurationMs { get; init; }

    /// <summary>Success status</summary>
    public bool Success { get; init; }

    /// <summary>Messages/warnings</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];
}

/// <summary>
/// Options for cache warmup.
/// </summary>
public record CacheWarmupOptions
{
    /// <summary>Warm up top N hot chunks</summary>
    public int TopHotChunksCount { get; init; } = 1000;

    /// <summary>Warm up frequently queried embeddings</summary>
    public bool WarmupEmbeddings { get; init; } = true;

    /// <summary>Warm up entity cache</summary>
    public bool WarmupEntities { get; init; } = true;

    /// <summary>Maximum warmup duration</summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Result of cache warmup operation.
/// </summary>
public record CacheWarmupResult
{
    /// <summary>Chunks warmed up</summary>
    public int ChunksWarmedUp { get; init; }

    /// <summary>Embeddings warmed up</summary>
    public int EmbeddingsWarmedUp { get; init; }

    /// <summary>Entities warmed up</summary>
    public int EntitiesWarmedUp { get; init; }

    /// <summary>Duration in milliseconds</summary>
    public double DurationMs { get; init; }

    /// <summary>Success status</summary>
    public bool Success { get; init; }
}

#endregion
