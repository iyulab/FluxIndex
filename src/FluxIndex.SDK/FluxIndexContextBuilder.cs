using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Constants;
using FluxIndex.Core.Models;
using FluxIndex.Core.Services;
using CoreServiceExtensions = FluxIndex.Core.Application.Services.MetadataAugmentationServiceExtensions;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxIndex.SDK;

/// <summary>
/// FluxIndexContext 빌더 - Fluent API로 Retriever와 Indexer 구성
/// AI Provider-agnostic 설계: IEmbeddingService, ITextCompletionService, IReranker는 외부 주입 필요
/// 소비 앱에서 Core의 추상 클래스(EmbeddingServiceBase 등)를 확장하여 AI Provider 구현
///
/// Storage providers are NOT bundled. Consumers must reference FluxIndex.Storage.* packages
/// directly and call storage-specific extension methods (e.g., builder.AddSQLiteStorage()).
/// </summary>
public class FluxIndexContextBuilder
{
    private readonly IServiceCollection _services;
    private readonly FluxIndexOptions _options;
    private readonly RetrieverOptions _retrieverOptions;
    private readonly IndexerOptions _indexerOptions;
    private readonly List<Action<IServiceCollection>> _storageRegistrations = new();
    private bool _suppressStartupMessages;

    /// <summary>
    /// The DI service collection. Storage packages use this to register their services
    /// via extension methods on FluxIndexContextBuilder.
    /// </summary>
    public IServiceCollection Services => _services;

    /// <summary>
    /// The FluxIndex options. Storage packages read these to configure their services.
    /// </summary>
    public FluxIndexOptions Options => _options;

    public FluxIndexContextBuilder()
    {
        _services = new ServiceCollection();
        _options = new FluxIndexOptions();
        _retrieverOptions = new RetrieverOptions();
        _indexerOptions = new IndexerOptions();

        // 기본 서비스 등록
        _services.AddLogging();
        _services.AddMemoryCache();

        // ✅ Default to InMemory embedding (for testing)
        // For production, configure a real embedding service via ConfigureServices()
        // LMSupply: .ConfigureServices(s => s.AddLMSupplyEmbedding()) - 소비 앱에서 직접 래퍼 구현
        _options.Embedding.Provider = "InMemory";
    }

    /// <summary>
    /// PostgreSQL 사용 - Fullstack RAG (Vector + Graph + SemanticCache 모두 활성화)
    /// 개별 구성요소는 이후 오버라이드 가능.
    /// NOTE: Requires FluxIndex.Storage.PostgreSQL package reference.
    /// </summary>
    public FluxIndexContextBuilder UsePostgreSQL(string connectionString)
    {
        // Vector Store
        _options.VectorStore.Provider = "PostgreSQL";
        _options.VectorStore.ConnectionString = connectionString;

        // Graph Store (동일 연결 사용)
        _options.GraphStore.Provider = "PostgreSQL";
        _options.GraphStore.UseVectorStoreConnection = true;

        // Semantic Cache (동일 연결 사용)
        _options.SemanticCache.Provider = "PostgreSQL";
        _options.SemanticCache.UseVectorStoreConnection = true;

        return this;
    }

    /// <summary>
    /// SQLite 사용 - Fullstack RAG (Vector + Graph + SemanticCache 모두 활성화)
    /// 개별 구성요소는 이후 오버라이드 가능.
    /// NOTE: Requires FluxIndex.Storage.SQLite package reference.
    /// </summary>
    public FluxIndexContextBuilder UseSQLite(string databasePath = "fluxindex.db")
    {
        // Vector Store
        _options.VectorStore.Provider = "SQLite";
        _options.VectorStore.ConnectionString = $"Data Source={databasePath}";

        // Graph Store (동일 연결 사용)
        _options.GraphStore.Provider = "SQLite";
        _options.GraphStore.UseVectorStoreConnection = true;

        // Semantic Cache (동일 연결 사용)
        _options.SemanticCache.Provider = "SQLite";
        _options.SemanticCache.UseVectorStoreConnection = true;

        return this;
    }

    /// <summary>
    /// SQLite 인메모리 사용 - Fullstack RAG (테스트용, 모든 기능 활성화)
    /// NOTE: Requires FluxIndex.Storage.SQLite package reference.
    /// </summary>
    public FluxIndexContextBuilder UseSQLiteInMemory()
    {
        // Vector Store
        _options.VectorStore.Provider = "SQLite";
        // Shared cache mode for in-memory database to allow multiple connections
        _options.VectorStore.ConnectionString = "Data Source=:memory:;Mode=Memory;Cache=Shared";

        // Graph Store (동일 연결 사용)
        _options.GraphStore.Provider = "SQLite";
        _options.GraphStore.UseVectorStoreConnection = true;

        // Semantic Cache (동일 연결 사용)
        _options.SemanticCache.Provider = "SQLite";
        _options.SemanticCache.UseVectorStoreConnection = true;

        return this;
    }

    /// <summary>
    /// 인메모리 임베딩 서비스 사용 (테스트용)
    /// </summary>
    public FluxIndexContextBuilder UseInMemoryEmbedding()
    {
        _options.Embedding.Provider = "InMemory";
        return this;
    }

    /// <summary>
    /// DI 기반 임베딩 서비스 사용 (Interface Provider Pattern)
    /// 소비자가 제공한 IEmbeddingService 구현체를 직접 등록
    /// OpenAI, Azure, Anthropic 등 외부 AI 서비스 사용 시 이 메서드 사용
    /// </summary>
    /// <example>
    /// // OpenAI 사용 예시 (소비자 앱에서 구현)
    /// var openAIService = new MyOpenAIEmbeddingService(apiKey);
    /// builder.UseEmbeddingService(openAIService);
    /// </example>
    public FluxIndexContextBuilder UseEmbeddingService(FluxIndex.Core.Application.Interfaces.IEmbeddingService embeddingService)
    {
        ArgumentNullException.ThrowIfNull(embeddingService);

        // Provider를 "Custom"으로 설정하여 ConfigureEmbeddingService에서 자동 등록 방지
        _options.Embedding.Provider = "Custom";

        // 소비자가 제공한 IEmbeddingService 인스턴스를 직접 등록
        _services.AddSingleton<FluxIndex.Core.Application.Interfaces.IEmbeddingService>(embeddingService);

        return this;
    }

    /// <summary>
    /// DI 기반 임베딩 서비스 팩토리 등록
    /// 서비스 프로바이더를 통해 IEmbeddingService 인스턴스 생성
    /// </summary>
    public FluxIndexContextBuilder UseEmbeddingService(Func<IServiceProvider, FluxIndex.Core.Application.Interfaces.IEmbeddingService> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _options.Embedding.Provider = "Custom";
        _services.AddSingleton(factory);

        return this;
    }

    /// <summary>
    /// Redis 캐시 사용.
    /// NOTE: Requires FluxIndex.Cache.Redis package reference.
    /// </summary>
    public FluxIndexContextBuilder UseRedisCache(string connectionString)
    {
        _options.Cache.CacheProvider = "Redis";
        _options.Cache.RedisConnectionString = connectionString;
        _options.Cache.EnableSearchCache = true;
        return this;
    }

    /// <summary>
    /// 메모리 캐시 사용
    /// </summary>
    public FluxIndexContextBuilder UseMemoryCache(int maxCacheSize = 1000)
    {
        _options.Cache.CacheProvider = "Memory";
        _options.Cache.MaxCacheSize = maxCacheSize;
        _options.Cache.EnableSearchCache = true;
        return this;
    }

    #region Local/Full Storage Modes

    /// <summary>
    /// Local 모드: SQLite가 모든 역할 수행 (기본값).
    /// Vector + Graph + RDB + SemanticCache 모두 SQLite에서 처리.
    /// 개발/테스트 환경에 적합.
    /// NOTE: Requires FluxIndex.Storage.SQLite package reference.
    /// </summary>
    public FluxIndexContextBuilder UseLocalStorage(string databasePath = "fluxindex.db")
    {
        // Vector Store
        _options.VectorStore.Provider = "SQLite";
        _options.VectorStore.ConnectionString = $"Data Source={databasePath}";

        // Graph Store (동일 연결 사용)
        _options.GraphStore.Provider = "SQLite";
        _options.GraphStore.UseVectorStoreConnection = true;

        // Semantic Cache (동일 연결 사용)
        _options.SemanticCache.Provider = "SQLite";
        _options.SemanticCache.UseVectorStoreConnection = true;

        return this;
    }

    /// <summary>
    /// Best-in-class 프리셋: PostgreSQL(RDB/Cache) + Qdrant(Vector) + Neo4j(Graph).
    /// 대규모 프로덕션 환경에 적합한 최고 성능 조합.
    /// NOTE: Requires FluxIndex.Storage.PostgreSQL, FluxIndex.Storage.Qdrant,
    /// and FluxIndex.Storage.Neo4j package references.
    /// Use ConfigureServices() to register the actual storage implementations.
    /// </summary>
    public FluxIndexContextBuilder UseBestInClass(
        string postgresConnectionString,
        string qdrantHost, int qdrantPort, string qdrantCollection, int vectorSize,
        string neo4jUri, string neo4jUsername, string neo4jPassword)
    {
        // PostgreSQL for RDB and Cache
        _options.VectorStore.Provider = "PostgreSQL";
        _options.VectorStore.ConnectionString = postgresConnectionString;
        _options.SemanticCache.Provider = "PostgreSQL";
        _options.SemanticCache.UseVectorStoreConnection = true;

        // Qdrant for Vector (takes priority over PostgreSQL)
        _options.VectorStore.Provider = "Qdrant";
        _options.VectorStore.QdrantHost = qdrantHost;
        _options.VectorStore.QdrantGrpcPort = qdrantPort;
        _options.VectorStore.QdrantCollectionName = qdrantCollection;
        _options.VectorStore.QdrantVectorSize = vectorSize;
        _options.VectorStore.QdrantNamingStrategy = "Fixed";

        // Neo4j for Graph (specialized graph DB)
        _options.GraphStore.Provider = "Neo4j";
        _options.GraphStore.Neo4jUri = neo4jUri;
        _options.GraphStore.Neo4jUsername = neo4jUsername;
        _options.GraphStore.Neo4jPassword = neo4jPassword;

        return this;
    }

    #endregion

    #region Neo4j Graph Store

    /// <summary>
    /// Neo4j 그래프 저장소 추가.
    /// 기본 저장소(SQLite/PostgreSQL)의 Graph를 Neo4j로 대체.
    /// NOTE: Requires FluxIndex.Storage.Neo4j package reference.
    /// </summary>
    public FluxIndexContextBuilder UseNeo4j(string uri, string username, string password, string? database = null)
    {
        _options.GraphStore.Provider = "Neo4j";
        _options.GraphStore.Neo4jUri = uri;
        _options.GraphStore.Neo4jUsername = username;
        _options.GraphStore.Neo4jPassword = password;
        _options.GraphStore.Neo4jDatabase = database;
        return this;
    }

    #endregion

    #region Vector Store (Qdrant)

    /// <summary>
    /// Qdrant 벡터 저장소 사용 (동적 차원 적응, 권장)
    /// 고성능 벡터 DB로 대규모 임베딩 저장 및 검색
    /// 컬렉션 이름에 차원이 자동 추가됨 (예: "my_chunks" → "my_chunks_384")
    /// NOTE: Requires FluxIndex.Storage.Qdrant package reference.
    /// </summary>
    /// <param name="host">Qdrant 서버 호스트</param>
    /// <param name="grpcPort">gRPC 포트</param>
    /// <param name="baseCollectionName">기본 컬렉션 이름 (차원 suffix 자동 추가)</param>
    public FluxIndexContextBuilder UseQdrant(string host = "localhost", int grpcPort = 6334, string baseCollectionName = "fluxindex_chunks")
    {
        _options.VectorStore.Provider = "Qdrant";
        _options.VectorStore.QdrantHost = host;
        _options.VectorStore.QdrantGrpcPort = grpcPort;
        _options.VectorStore.QdrantCollectionName = baseCollectionName;
        _options.VectorStore.QdrantNamingStrategy = "DimensionSuffix";
        return this;
    }

    /// <summary>
    /// Qdrant 벡터 저장소 사용 (고정 차원, 레거시 호환)
    /// 명시적 벡터 차원 설정이 필요한 경우 사용
    /// NOTE: Requires FluxIndex.Storage.Qdrant package reference.
    /// </summary>
    /// <param name="host">Qdrant 서버 호스트</param>
    /// <param name="grpcPort">gRPC 포트</param>
    /// <param name="collectionName">컬렉션 이름 (고정)</param>
    /// <param name="vectorSize">벡터 차원 (고정)</param>
    public FluxIndexContextBuilder UseQdrantFixed(string host = "localhost", int grpcPort = 6334, string collectionName = "fluxindex_chunks", int vectorSize = EmbeddingDefaults.DefaultVectorDimension)
    {
        _options.VectorStore.Provider = "Qdrant";
        _options.VectorStore.QdrantHost = host;
        _options.VectorStore.QdrantGrpcPort = grpcPort;
        _options.VectorStore.QdrantCollectionName = collectionName;
        _options.VectorStore.QdrantVectorSize = vectorSize;
        _options.VectorStore.QdrantNamingStrategy = "Fixed";
        return this;
    }

    /// <summary>
    /// Qdrant Cloud 벡터 저장소 사용 (동적 차원 적응, 권장)
    /// NOTE: Requires FluxIndex.Storage.Qdrant package reference.
    /// </summary>
    public FluxIndexContextBuilder UseQdrantCloud(string cloudHost, string apiKey, string baseCollectionName = "fluxindex_chunks")
    {
        _options.VectorStore.Provider = "Qdrant";
        _options.VectorStore.QdrantHost = cloudHost;
        _options.VectorStore.QdrantApiKey = apiKey;
        _options.VectorStore.QdrantUseHttps = true;
        _options.VectorStore.QdrantCollectionName = baseCollectionName;
        _options.VectorStore.QdrantNamingStrategy = "DimensionSuffix";
        return this;
    }

    /// <summary>
    /// Qdrant Cloud 벡터 저장소 사용 (고정 차원, 레거시 호환)
    /// NOTE: Requires FluxIndex.Storage.Qdrant package reference.
    /// </summary>
    public FluxIndexContextBuilder UseQdrantCloudFixed(string cloudHost, string apiKey, string collectionName = "fluxindex_chunks", int vectorSize = EmbeddingDefaults.DefaultVectorDimension)
    {
        _options.VectorStore.Provider = "Qdrant";
        _options.VectorStore.QdrantHost = cloudHost;
        _options.VectorStore.QdrantApiKey = apiKey;
        _options.VectorStore.QdrantUseHttps = true;
        _options.VectorStore.QdrantCollectionName = collectionName;
        _options.VectorStore.QdrantVectorSize = vectorSize;
        _options.VectorStore.QdrantNamingStrategy = "Fixed";
        return this;
    }

    #endregion

    #region Semantic Cache Options

    /// <summary>
    /// 시맨틱 캐시 고급 설정.
    /// SemanticCache는 UseLocalStorage/UsePostgreSQL/UseBestInClass에서 자동 활성화됨.
    /// 이 메서드는 추가 설정이 필요한 경우에만 사용.
    /// </summary>
    public FluxIndexContextBuilder WithSemanticCacheOptions(Action<Configuration.SemanticCacheOptions> configure)
    {
        configure?.Invoke(_options.SemanticCache);
        return this;
    }

    #endregion

    /// <summary>
    /// 품질 모니터링 시스템 활성화
    /// </summary>
    public FluxIndexContextBuilder WithQualityMonitoring(bool enableRealTimeAlerts = true)
    {
        _services.AddSingleton<IQualityMonitoringService, QualityMonitoringService>();
        _options.QualityMonitoring.EnableMonitoring = true;
        _options.QualityMonitoring.EnableRealTimeAlerts = enableRealTimeAlerts;
        return this;
    }

    /// <summary>
    /// 청킹 옵션 설정
    /// </summary>
    public FluxIndexContextBuilder WithChunking(string strategy = "Auto", int chunkSize = 512, int chunkOverlap = 64)
    {
        _options.Indexing.ChunkingDefaults.Strategy = strategy;
        _options.Indexing.ChunkingDefaults.MaxChunkSize = chunkSize;
        _options.Indexing.ChunkingDefaults.OverlapSize = chunkOverlap;

        _indexerOptions.ChunkSize = chunkSize;
        _indexerOptions.ChunkOverlap = chunkOverlap;
        _indexerOptions.ChunkingStrategy = Enum.Parse<ChunkingStrategy>(strategy, true);

        return this;
    }

    /// <summary>
    /// 검색 옵션 설정
    /// </summary>
    public FluxIndexContextBuilder WithSearchOptions(int defaultMaxResults = 10, float defaultMinScore = 0.5f)
    {
        _options.Search.DefaultMaxResults = defaultMaxResults;
        _options.Search.DefaultMinScore = defaultMinScore;

        _retrieverOptions.DefaultMaxResults = defaultMaxResults;
        _retrieverOptions.DefaultMinScore = defaultMinScore;

        return this;
    }

    /// <summary>
    /// 캐시 기간 설정
    /// </summary>
    public FluxIndexContextBuilder WithCacheDuration(TimeSpan duration)
    {
        _options.Cache.CacheTTL = duration;
        _retrieverOptions.CacheDuration = duration;
        return this;
    }

    /// <summary>
    /// 병렬 처리 옵션 설정
    /// </summary>
    public FluxIndexContextBuilder WithParallelProcessing(bool enabled = true, int maxParallelism = 4)
    {
        _indexerOptions.ParallelEmbedding = enabled;
        _indexerOptions.MaxParallelEmbedding = maxParallelism;
        return this;
    }

    /// <summary>
    /// 로깅 구성
    /// </summary>
    public FluxIndexContextBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        _services.AddLogging(configure);
        return this;
    }

    /// <summary>
    /// RAG 품질 평가 시스템 활성화 (소비자가 IRAGEvaluationService 구현체 제공 필요)
    /// </summary>
    public FluxIndexContextBuilder WithEvaluationSystem(string? datasetBasePath = null)
    {
        // 평가 시스템 인프라만 등록 (AI 구현체는 소비자 제공)
        return this;
    }

    /// <summary>
    /// 개발용 평가 시스템 (로컬 데이터셋 포함)
    /// </summary>
    public FluxIndexContextBuilder WithEvaluationSystemForDevelopment()
    {
        var datasetPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxIndex", "datasets");
        return WithEvaluationSystem(datasetPath);
    }

    /// <summary>
    /// 운영용 평가 시스템 (고성능 설정)
    /// </summary>
    public FluxIndexContextBuilder WithEvaluationSystemForProduction(string datasetBasePath)
    {
        WithEvaluationSystem(datasetBasePath);
        return this;
    }

    /// <summary>
    /// Contextual Embedding Pipeline activation (Anthropic's Contextual Retrieval approach).
    /// Prepends LLM-generated context to chunks before embedding, improving retrieval by up to 67%.
    /// Research shows combining with BM25 reduces retrieval failures by 49%.
    /// </summary>
    /// <param name="llmThreshold">LLM usage threshold based on ContextDependency (default 0.7)</param>
    /// <param name="generateDualEmbeddings">Generate both contextual and standard embeddings for hybrid retrieval</param>
    /// <returns>Builder instance for chaining</returns>
    public FluxIndexContextBuilder WithContextualEmbedding(
        double llmThreshold = 0.7,
        bool generateDualEmbeddings = false)
    {
        CoreServiceExtensions.AddContextualEmbedding(_services, options =>
        {
            options.LlmThreshold = llmThreshold;
            options.GenerateDualEmbeddings = generateDualEmbeddings;
        });

        return this;
    }

    /// <summary>
    /// Contextual Embedding with advanced configuration.
    /// </summary>
    /// <param name="configure">Configuration action for ContextualEmbeddingOptions</param>
    /// <returns>Builder instance for chaining</returns>
    public FluxIndexContextBuilder WithContextualEmbedding(
        Action<FluxIndex.Core.Application.Services.ContextualEmbeddingOptions> configure)
    {
        CoreServiceExtensions.AddContextualEmbedding(_services, configure);
        return this;
    }


    /// <summary>
    /// Enable Late Chunking for contextual embeddings.
    /// Late Chunking generates embeddings considering surrounding context,
    /// improving retrieval quality for documents with high contextual dependencies.
    /// </summary>
    /// <param name="contextMode">How to integrate document context (default: SurroundingContext)</param>
    /// <param name="documentContextWeight">Weight for document context in weighted combination (default: 0.3)</param>
    /// <returns>Builder instance for chaining</returns>
    public FluxIndexContextBuilder UseLateChunking(
        FluxIndex.Core.Application.Services.ContextIntegrationMode contextMode = FluxIndex.Core.Application.Services.ContextIntegrationMode.SurroundingContext,
        double documentContextWeight = 0.3)
    {
        CoreServiceExtensions.AddLateChunking(_services, contextMode, documentContextWeight);
        return this;
    }

    /// <summary>
    /// Enable Late Chunking with advanced configuration.
    /// </summary>
    /// <param name="configure">Configuration action for LateChunkingOptions</param>
    /// <returns>Builder instance for chaining</returns>
    public FluxIndexContextBuilder UseLateChunking(
        Action<FluxIndex.Core.Application.Services.LateChunkingOptions> configure)
    {
        CoreServiceExtensions.AddLateChunking(_services, configure);
        return this;
    }

    /// <summary>
    /// 고급 서비스 구성 - 확장 패키지에서 사용
    /// </summary>
    public FluxIndexContextBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure?.Invoke(_services);
        return this;
    }

    /// <summary>
    /// Register storage services. Storage packages should call this to register their
    /// IVectorStore, IGraphStore, ISemanticCacheService, and ICacheService implementations.
    /// Multiple registrations are invoked in order during Build().
    /// </summary>
    /// <param name="registration">Action that registers storage services into the DI container.</param>
    /// <returns>Builder instance for chaining</returns>
    public FluxIndexContextBuilder RegisterStorageServices(Action<IServiceCollection> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _storageRegistrations.Add(registration);
        return this;
    }

    /// <summary>
    /// Suppress startup messages (AI service guidance).
    /// Use this in production environments or when console output is not desired.
    /// </summary>
    public FluxIndexContextBuilder SuppressStartupMessages()
    {
        _suppressStartupMessages = true;
        return this;
    }

    /// <summary>
    /// FluxIndexContext 빌드
    /// </summary>
    public IFluxIndexContext Build()
    {
        // Invoke storage registrations from storage packages
        foreach (var registration in _storageRegistrations)
        {
            registration(_services);
        }

        // Configure embedding service
        ConfigureEmbeddingService();

        // Configure chunking service
        ConfigureChunkingService();

        // Fallback: if no IVectorStore was registered by storage packages, use InMemory
        if (!_services.Any(d => d.ServiceType == typeof(IVectorStore)))
        {
            _services.AddSingleton<IVectorStore, InMemoryVectorStore>();
        }

        // Fallback: if no ICacheService was registered and Memory cache requested
        if (_options.Cache.CacheProvider?.Equals("Memory", StringComparison.OrdinalIgnoreCase) == true
            && !_services.Any(d => d.ServiceType == typeof(ICacheService)))
        {
            _services.AddSingleton<ICacheService, InMemoryCacheService>();
        }

        // Register core services
        _services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
        _services.AddSingleton(_retrieverOptions);
        _services.AddSingleton(_indexerOptions);

        // Register in-memory chunk hierarchy repository for SDK (fallback if storage didn't register one)
        if (!_services.Any(d => d.ServiceType == typeof(IChunkHierarchyRepository)))
        {
            _services.AddScoped<IChunkHierarchyRepository, InMemoryChunkHierarchyRepository>();
        }

        // Register hybrid search services
        _services.AddScoped<ISparseRetriever, BM25SparseRetriever>();
        _services.AddScoped<IHybridSearchService, HybridSearchService>();
        _services.AddScoped<IRankFusionService, RankFusionService>();

        // Register Small-to-Big services
        _services.AddScoped<ISmallToBigRetriever, SmallToBigRetriever>();
        _services.AddMemoryCache(); // For query complexity caching

        // Register Adaptive Search services
        _services.AddScoped<IQueryComplexityAnalyzer, QueryComplexityAnalyzer>();
        _services.AddScoped<IAdaptiveSearchService, AdaptiveSearchService>();

        // Register Graph Traversal service for local graph search support
        CoreServiceExtensions.AddGraphTraversal(_services);

        // Register Retriever and Indexer as services (needed for Extensions)
        _services.AddScoped<Retriever>(serviceProvider =>
        {
            var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
            var documentRepository = serviceProvider.GetRequiredService<IDocumentRepository>();
            var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();
            var cacheService = serviceProvider.GetService<ICacheService>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var rankFusionService = serviceProvider.GetService<IRankFusionService>();
            var vectorQuantizer = serviceProvider.GetService<IVectorQuantizer>();

            // Auto-detection services (optional - null if not registered)
            var hybridSearchService = serviceProvider.GetService<IHybridSearchService>();
            var graphRAGService = serviceProvider.GetService<IGraphRAGService>();

            return new Retriever(
                vectorStore,
                documentRepository,
                embeddingService,
                _retrieverOptions,
                cacheService,
                rankFusionService,
                vectorQuantizer,
                loggerFactory.CreateLogger<Retriever>(),
                hybridSearchService,
                graphRAGService
            );
        });

        _services.AddScoped<Indexer>(serviceProvider =>
        {
            var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
            var documentRepository = serviceProvider.GetRequiredService<IDocumentRepository>();
            var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();
            var chunkingService = serviceProvider.GetRequiredService<IChunkingService>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            // Get optional IMetadataExtractor if registered
            var metadataExtractor = serviceProvider.GetService<FluxIndex.Core.Interfaces.IMetadataExtractor>();

            // Auto-detection services (optional - null if not registered)
            var graphRAGService = serviceProvider.GetService<IGraphRAGService>();
            var hybridSearchService = serviceProvider.GetService<IHybridSearchService>();

            return new Indexer(
                vectorStore,
                documentRepository,
                embeddingService,
                chunkingService,
                _indexerOptions,
                loggerFactory.CreateLogger<Indexer>(),
                metadataExtractor,
                graphRAGService,
                hybridSearchService
            );
        });

        // Build service provider
        var serviceProvider = _services.BuildServiceProvider();

        // Initialize database if storage package registered an initializer
        var initializers = serviceProvider.GetServices<IStorageInitializer>();
        foreach (var initializer in initializers)
        {
            initializer.InitializeSync(serviceProvider);
        }

        // Display AI service guidance (shows LMSupply options for missing services)
        if (!_suppressStartupMessages)
        {
            StartupMessageService.DisplayAIServiceGuidance(
                serviceProvider,
                _options.Embedding.Provider,
                _options.VectorStore.Provider);
        }

        // Get Retriever and Indexer from DI
        var retriever = serviceProvider.GetRequiredService<Retriever>();
        var indexer = serviceProvider.GetRequiredService<Indexer>();

        // Get additional services
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var hybridSearchService = serviceProvider.GetService<IHybridSearchService>();
        var semanticCacheService = serviceProvider.GetService<ISemanticCacheService>();
        var smallToBigRetriever = serviceProvider.GetService<ISmallToBigRetriever>();
        var qualityMonitoringService = serviceProvider.GetService<IQualityMonitoringService>();
        var adaptiveSearchService = serviceProvider.GetService<IAdaptiveSearchService>();

        // Create and return context
        return new FluxIndexContext(
            retriever,
            indexer,
            serviceProvider,
            loggerFactory.CreateLogger<FluxIndexContext>(),
            semanticCacheService,
            hybridSearchService,
            smallToBigRetriever,
            qualityMonitoringService,
            adaptiveSearchService
        );
    }

    private void ConfigureEmbeddingService()
    {
        switch (_options.Embedding.Provider?.ToLowerInvariant())
        {
            case "inmemory":
                // In-memory embedding service for testing (generates random embeddings)
                _services.AddSingleton<IEmbeddingService, InMemoryEmbeddingService>();
                break;
            case "custom":
                // Custom embedding service already registered via UseEmbeddingService()
                // Do nothing - service is already in DI container
                break;
            default:
                // ✅ Default: InMemory for basic testing
                // For production, use ConfigureServices to register a real embedding service:
                // - LMSupply: 소비 앱에서 EmbeddingServiceBase 확장하여 래퍼 구현
                // - OpenAI/Azure: EmbeddingServiceBase 확장하여 구현 후 ConfigureServices로 등록
                _services.AddSingleton<IEmbeddingService, InMemoryEmbeddingService>();
                break;
        }
    }

    private void ConfigureChunkingService()
    {
        _services.AddSingleton<IChunkingService>(sp =>
            new SDK.Services.SimpleChunkingService(
                _indexerOptions.ChunkSize,
                _indexerOptions.ChunkOverlap
            )
        );
    }
}
