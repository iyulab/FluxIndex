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

    /// <summary>
    /// Advanced RAG Enhancement settings.
    /// Includes Late Chunking, Multi-Hypothetical HyDE, and Contextual Retrieval.
    /// </summary>
    public RAGEnhancementOptions RAGEnhancement { get; set; } = new();
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

/// <summary>
/// RAG Enhancement mode for intelligent feature selection.
/// </summary>
public enum RAGEnhancementMode
{
    /// <summary>
    /// Disabled - No RAG enhancements applied (legacy behavior)
    /// </summary>
    Disabled,

    /// <summary>
    /// Auto (Default) - Intelligently selects optimal enhancements based on:
    /// - Document characteristics (length, structure, language)
    /// - Available resources (LLM availability, compute capacity)
    /// - Query complexity and context requirements
    /// </summary>
    Auto,

    /// <summary>
    /// Custom - Manual configuration of individual enhancement features
    /// </summary>
    Custom
}

/// <summary>
/// Advanced RAG Enhancement Options with intelligent defaults.
///
/// Minimal Configuration (recommended):
///   "RAGEnhancement": { "Mode": "Auto" }
///
/// This enables intelligent selection of:
/// - Late Chunking for long documents (>2000 chars)
/// - Contextual headers for chunks with high context dependency
/// - Multi-HyDE for complex/ambiguous queries (when LLM available)
/// </summary>
public class RAGEnhancementOptions
{
    /// <summary>
    /// Enhancement mode. Default: Auto (intelligent feature selection)
    /// - Disabled: No enhancements (legacy behavior)
    /// - Auto: Intelligent selection based on content/query characteristics
    /// - Custom: Manual configuration via sub-options
    /// </summary>
    public RAGEnhancementMode Mode { get; set; } = RAGEnhancementMode.Auto;

    /// <summary>
    /// Late Chunking configuration. Only used when Mode=Custom.
    /// In Auto mode, Late Chunking activates for documents >2000 chars.
    /// </summary>
    public LateChunkingConfiguration LateChunking { get; set; } = new();

    /// <summary>
    /// Multi-HyDE configuration. Only used when Mode=Custom.
    /// In Auto mode, Multi-HyDE activates for complex queries when LLM is available.
    /// </summary>
    public MultiHyDEConfiguration MultiHyDE { get; set; } = new();

    /// <summary>
    /// Contextual Retrieval configuration. Only used when Mode=Custom.
    /// In Auto mode, contextual headers are added based on chunk context dependency.
    /// </summary>
    public ContextualRetrievalConfiguration ContextualRetrieval { get; set; } = new();

    /// <summary>
    /// Check if any enhancement should be applied (Mode is not Disabled)
    /// </summary>
    public bool IsEnabled => Mode != RAGEnhancementMode.Disabled;

    /// <summary>
    /// Check if running in Auto mode (intelligent selection)
    /// </summary>
    public bool IsAutoMode => Mode == RAGEnhancementMode.Auto;
}

/// <summary>
/// Late Chunking configuration
/// </summary>
public class LateChunkingConfiguration
{
    /// <summary>
    /// Enable Late Chunking embedding approach
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Maximum document length for full document embedding.
    /// Documents longer than this use sliding window approach.
    /// </summary>
    public int MaxDocumentLength { get; set; } = 8000;

    /// <summary>
    /// Context integration mode: PrependSummary, WeightedCombination, or SurroundingContext
    /// </summary>
    public string ContextIntegrationMode { get; set; } = "SurroundingContext";

    /// <summary>
    /// Weight for document context in weighted combination (0.0-1.0)
    /// </summary>
    public double DocumentContextWeight { get; set; } = 0.3;

    /// <summary>
    /// Size of surrounding context to include (characters)
    /// </summary>
    public int SurroundingContextSize { get; set; } = 500;
}

/// <summary>
/// Multi-Hypothetical HyDE configuration
/// </summary>
public class MultiHyDEConfiguration
{
    /// <summary>
    /// Enable Multi-Hypothetical HyDE for query transformation
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Number of hypothetical documents to generate (1-10).
    /// Recommended: 3-5 for multi-hypothetical mode.
    /// </summary>
    public int DocumentCount { get; set; } = 5;

    /// <summary>
    /// Temperature step for diversity in multi-document generation.
    /// Applied as: base_temperature + (index * TemperatureStep)
    /// </summary>
    public float TemperatureStep { get; set; } = 0.1f;

    /// <summary>
    /// Enable parallel document generation (recommended for performance)
    /// </summary>
    public bool EnableParallelGeneration { get; set; } = true;

    /// <summary>
    /// Custom perspectives for document generation.
    /// If empty, default perspectives are used:
    /// ["expert technical", "beginner-friendly", "practical", "theoretical", "troubleshooting"]
    /// </summary>
    public List<string> Perspectives { get; set; } = new();
}

/// <summary>
/// Contextual Retrieval configuration (Anthropic approach)
/// </summary>
public class ContextualRetrievalConfiguration
{
    /// <summary>
    /// Enable Contextual Retrieval enrichment
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// LLM usage threshold for context generation (0.0-1.0).
    /// Higher values use LLM more frequently.
    /// </summary>
    public double LlmThreshold { get; set; } = 0.7;

    /// <summary>
    /// Generate both contextual and standard embeddings for hybrid search
    /// </summary>
    public bool GenerateDualEmbeddings { get; set; } = false;

    /// <summary>
    /// Enable prompt caching for reduced LLM costs (if provider supports)
    /// </summary>
    public bool EnablePromptCaching { get; set; } = true;

    /// <summary>
    /// Maximum context summary length (tokens)
    /// </summary>
    public int MaxContextLength { get; set; } = 100;
}