using System;
using System.Collections.Generic;
using FluxIndex.Core.Constants;

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
    /// 그래프 저장소 설정 (청크 계층 구조 및 관계)
    /// </summary>
    public GraphStoreOptions GraphStore { get; set; } = new();

    /// <summary>
    /// 시맨틱 캐시 설정 (쿼리 유사도 기반 결과 캐싱)
    /// </summary>
    public SemanticCacheOptions SemanticCache { get; set; } = new();

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

    /// <summary>
    /// Whether the storage package auto-initializes the vector store schema on Build()
    /// (creates required extensions/tables). Default: true — symmetric with SQLite, which always
    /// initializes on Build(). Set to false to manage the schema externally (EF migrations, an
    /// ops-owned schema) or on managed PostgreSQL where the connecting role lacks CREATE EXTENSION
    /// privilege and the vector extension is not already installed — the one case auto-init throws.
    /// Currently honored by the PostgreSQL storage provider.
    /// </summary>
    public bool EnableAutoMigration { get; set; } = true;
    public Dictionary<string, object> ProviderSpecificOptions { get; set; } = new();

    // Qdrant-specific options
    public string QdrantHost { get; set; } = "localhost";
    public int QdrantGrpcPort { get; set; } = 6334;
    public int QdrantHttpPort { get; set; } = 6333;
    public string QdrantCollectionName { get; set; } = "fluxindex_chunks";
    public int QdrantVectorSize { get; set; } = EmbeddingDefaults.DefaultVectorDimension;
    public string? QdrantApiKey { get; set; }
    public bool QdrantUseHttps { get; set; }

    /// <summary>
    /// Qdrant collection naming strategy name. Default: "ModelFingerprint" (recommended).
    /// - "ModelFingerprint": {baseName}_{fingerprint} - auto-adapts to embedding model identity
    /// - "DimensionSuffix": {baseName}_{dimension} - legacy, cannot distinguish same-dimension models
    /// - "Fixed": exact name specified - requires explicit VectorSize
    /// </summary>
    public string QdrantNamingStrategy { get; set; } = "ModelFingerprint";
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
    public bool PreserveFormatting { get; set; }
}

/// <summary>
/// 검색 설정
/// </summary>
public class SearchConfiguration
{
    public int DefaultMaxResults { get; set; } = 10;
    public float DefaultMinScore { get; set; }
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
/// 그래프 저장소 옵션 (청크 계층 구조 및 관계)
/// </summary>
public class GraphStoreOptions
{
    /// <summary>
    /// 그래프 저장소 프로바이더 ("None", "SQLite", "PostgreSQL", "Neo4j")
    /// </summary>
    public string Provider { get; set; } = "None";

    /// <summary>
    /// 연결 문자열 (벡터 저장소와 동일한 연결 사용 시 비워둠)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 자동 마이그레이션 활성화
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// 재귀 쿼리 최대 깊이 (순환 방지)
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 100;

    /// <summary>
    /// 벡터 저장소와 동일한 연결 문자열 사용 여부
    /// </summary>
    public bool UseVectorStoreConnection { get; set; } = true;

    // Neo4j-specific options
    public string? Neo4jUri { get; set; }
    public string? Neo4jUsername { get; set; }
    public string? Neo4jPassword { get; set; }
    public string? Neo4jDatabase { get; set; }
}

/// <summary>
/// 시맨틱 캐시 옵션 (쿼리 유사도 기반 결과 캐싱)
/// </summary>
public class SemanticCacheOptions
{
    /// <summary>
    /// 시맨틱 캐시 프로바이더 ("None", "SQLite", "PostgreSQL", "Redis")
    /// </summary>
    public string Provider { get; set; } = "None";

    /// <summary>
    /// 연결 문자열 (벡터 저장소와 동일한 연결 사용 시 비워둠)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 자동 마이그레이션 활성화
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// 유사도 임계값 (이 값 이상의 유사도일 때 캐시 히트)
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.85f;

    /// <summary>
    /// 기본 캐시 만료 시간
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 최대 캐시 항목 수
    /// </summary>
    public int MaxEntries { get; set; } = 10000;

    /// <summary>
    /// 임베딩 차원 수 (pgvector 인덱스용)
    /// </summary>
    public int EmbeddingDimensions { get; set; } = EmbeddingDefaults.DefaultVectorDimension;

    /// <summary>
    /// 자동 정리 활성화
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 정리 간격
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 벡터 저장소와 동일한 연결 문자열 사용 여부
    /// </summary>
    public bool UseVectorStoreConnection { get; set; } = true;

    /// <summary>
    /// PostgreSQL: UNLOGGED 테이블 사용 (빠른 쓰기, 크래시 시 데이터 손실 가능)
    /// </summary>
    public bool UseUnloggedTable { get; set; } = true;
}

/// <summary>
/// 품질 모니터링 옵션
/// </summary>
public class QualityMonitoringOptions
{
    public bool EnableMonitoring { get; set; }
    public bool EnableRealTimeAlerts { get; set; }
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
    public bool Enabled { get; set; }

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
    public bool Enabled { get; set; }

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
    public bool Enabled { get; set; }

    /// <summary>
    /// LLM usage threshold for context generation (0.0-1.0).
    /// Higher values use LLM more frequently.
    /// </summary>
    public double LlmThreshold { get; set; } = 0.7;

    /// <summary>
    /// Generate both contextual and standard embeddings for hybrid search
    /// </summary>
    public bool GenerateDualEmbeddings { get; set; }

    /// <summary>
    /// Enable prompt caching for reduced LLM costs (if provider supports)
    /// </summary>
    public bool EnablePromptCaching { get; set; } = true;

    /// <summary>
    /// Maximum context summary length (tokens)
    /// </summary>
    public int MaxContextLength { get; set; } = 100;
}