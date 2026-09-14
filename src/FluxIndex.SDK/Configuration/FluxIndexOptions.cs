using System;
using System.Collections.Generic;
using FluxIndex.Core.Constants;

namespace FluxIndex.SDK.Configuration;

/// <summary>
/// FluxIndex 설정 옵션 — <c>FluxIndexContextBuilder.Options</c> 로 노출되는 빌더의 공개 설정 트리.
/// </summary>
/// <remarks>
/// 블록마다 «누가 읽는가»가 다르다. <see cref="VectorStore"/> · <see cref="Embedding"/> · <see cref="Cache"/> ·
/// <see cref="GraphStore"/> · <see cref="SemanticCache"/> · <see cref="KeywordSearch"/> 은
/// 빌더가 서비스 등록에 쓴다(각 타입 문서에 읽히지 않는 개별 속성이 표시돼 있다). 청킹과 검색 기본값은 이 트리가 아니라
/// <c>IndexerOptions</c>/<c>RetrieverOptions</c> 에 있다(<c>WithChunking(...)</c>/<c>WithSearchOptions(...)</c>).
/// 검증: <c>FluxIndex.SDK.Tests/OptionsReachabilityRosterTests</c>.
/// </remarks>
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
    /// 키워드(sparse) 검색 인덱스 저장소 설정 — 하이브리드 검색의 키워드 레그.
    /// </summary>
    public KeywordSearchStoreOptions KeywordSearch { get; set; } = new();

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
/// 키워드(sparse) 검색 인덱스 저장소 옵션.
/// </summary>
/// <remarks>
/// <para>
/// The keyword leg is the only storage component that used to have no options of its own: it was
/// registered from inside the vector store's provider block, so it could only ever live wherever
/// the vectors lived. That makes the recommended split deployment — vectors in Qdrant, metadata in
/// PostgreSQL — unable to express a keyword leg at all, and a consumer that wants one has to
/// duplicate the library's own registration code.
/// </para>
/// <para>
/// Defaults preserve the previous behavior exactly: with <see cref="Provider"/> unset the keyword
/// leg follows <see cref="VectorStoreOptions.Provider"/> on the vector store's connection, which is
/// what the vector-gated registration did.
/// </para>
/// </remarks>
public class KeywordSearchStoreOptions
{
    /// <summary>
    /// 키워드 인덱스 프로바이더 ("PostgreSQL", "SQLite"). 비워 두면 벡터 저장소의
    /// <see cref="VectorStoreOptions.Provider"/> 를 따른다 (기존 동작).
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 연결 문자열. <see cref="UseVectorStoreConnection"/> 가 true 이면 무시된다.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 벡터 저장소와 동일한 연결 문자열 사용 여부. 기본 true — 벡터와 키워드가 같은 데이터베이스에
    /// 있는 통상 구성에서 연결 문자열을 한 번만 적게 한다.
    /// </summary>
    public bool UseVectorStoreConnection { get; set; } = true;

    /// <summary>
    /// Build() 시 키워드 인덱스 스키마를 자동 생성할지 여부. 비워 두면(null) 각 백엔드가 종전에
    /// 쓰던 기준을 그대로 따른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullable on purpose. The backends did not agree before this option existed — PostgreSQL
    /// gated keyword provisioning on <see cref="VectorStoreOptions.EnableAutoMigration"/> while
    /// SQLite always provisioned — so a non-nullable <c>true</c> default would have turned DDL back
    /// on for a caller who had switched it off on the vector store, which is the one case the flag
    /// exists to prevent (managed PostgreSQL where the connecting role cannot create extensions).
    /// Leaving it null keeps each backend on its previous rule; setting it decides for both.
    /// </para>
    /// <para>
    /// Opting out delays the DDL rather than preventing it — the backends still create their tables
    /// lazily on first use. Same caveat as the vector store's flag.
    /// </para>
    /// </remarks>
    public bool? EnableAutoMigration { get; set; }

    /// <summary>
    /// 이 키워드 레그가 실제로 어느 프로바이더에 등록되는지 해석한다 — <see cref="Provider"/> 가
    /// 비어 있으면 벡터 저장소를 따른다. 반환값은 소문자로 정규화된 이름이다.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in each storage package so the two backends cannot drift apart on
    /// what "unset" means (LAYERING section 4 — one implementation, not a copy per consumer).
    /// </remarks>
    public string ResolveProviderName(VectorStoreOptions vectorStore)
    {
        ArgumentNullException.ThrowIfNull(vectorStore);

        var name = string.IsNullOrWhiteSpace(Provider) ? vectorStore.Provider : Provider;
        return name?.ToLowerInvariant() ?? string.Empty;
    }
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
