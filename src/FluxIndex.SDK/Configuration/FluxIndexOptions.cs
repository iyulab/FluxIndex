using System;
using System.Collections.Generic;

namespace FluxIndex.SDK.Configuration;

/// <summary>
/// FluxIndex 설정 옵션
/// </summary>
public class FluxIndexOptions
{
    /// <summary>
    /// 벡터 저장소 설정
    /// </summary>
    public VectorStoreOptions VectorStore { get; set; } = new();
    
    /// <summary>
    /// 임베딩 서비스 설정
    /// </summary>
    public EmbeddingOptions Embedding { get; set; } = new();
    
    /// <summary>
    /// 인덱싱 설정
    /// </summary>
    public IndexingConfiguration Indexing { get; set; } = new();
    
    /// <summary>
    /// 검색 설정
    /// </summary>
    public SearchConfiguration Search { get; set; } = new();
    
    /// <summary>
    /// 캐싱 설정
    /// </summary>
    public CacheOptions Cache { get; set; } = new();

    /// <summary>
    /// 품질 모니터링 설정
    /// </summary>
    public QualityMonitoringOptions QualityMonitoring { get; set; } = new();
}

/// <summary>
/// 벡터 저장소 옵션
/// </summary>
public class VectorStoreOptions
{
    public string Provider { get; set; } = "PostgreSQL";
    public string ConnectionString { get; set; } = string.Empty;
    public int MaxConnections { get; set; } = 10;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableAutoMigration { get; set; } = false;
    public Dictionary<string, object> ProviderSpecificOptions { get; set; } = new();
}

/// <summary>
/// 임베딩 옵션
/// </summary>
public class EmbeddingOptions
{
    public string Provider { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "text-embedding-3-small";
    public int BatchSize { get; set; } = 100;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public bool EnableCache { get; set; } = true;
    public Dictionary<string, object> ProviderSpecificOptions { get; set; } = new();
}

/// <summary>
/// 인덱싱 설정
/// </summary>
public class IndexingConfiguration
{
    public int MaxParallelDocuments { get; set; } = 5;
    public int ChunkBatchSize { get; set; } = 50;
    public bool EnableProgressReporting { get; set; } = true;
    public TimeSpan ProgressReportInterval { get; set; } = TimeSpan.FromSeconds(1);
    public bool ValidateEmbeddings { get; set; } = true;
    public ChunkingDefaults ChunkingDefaults { get; set; } = new();
}

/// <summary>
/// 청킹 기본값
/// </summary>
public class ChunkingDefaults
{
    public string Strategy { get; set; } = "Auto";
    public int MaxChunkSize { get; set; } = 512;
    public int OverlapSize { get; set; } = 64;
    public bool PreserveFormatting { get; set; } = false;
}

/// <summary>
/// 검색 설정
/// </summary>
public class SearchConfiguration
{
    public int DefaultMaxResults { get; set; } = 10;
    public float DefaultMinScore { get; set; } = 0.0f;
    public float DefaultVectorWeight { get; set; } = 0.7f;
    public float DefaultKeywordWeight { get; set; } = 0.3f;
    public bool EnableHighlighting { get; set; } = true;
    public bool EnableFaceting { get; set; } = true;
    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// 캐싱 옵션
/// </summary>
public class CacheOptions
{
    public bool EnableEmbeddingCache { get; set; } = true;
    public bool EnableSearchCache { get; set; } = true;
    public int MaxCacheSize { get; set; } = 1000;
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan CacheTTL { get; set; } = TimeSpan.FromHours(1);
    public string CacheProvider { get; set; } = "Memory";
    public string RedisConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// 품질 모니터링 옵션
/// </summary>
public class QualityMonitoringOptions
{
    public bool EnableMonitoring { get; set; } = false;
    public bool EnableRealTimeAlerts { get; set; } = false;
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan AlertCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxMetricsHistory { get; set; } = 1440; // 24 hours at 1 minute intervals
}