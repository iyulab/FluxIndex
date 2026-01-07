using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Models;
using FluxIndex.Core.Services;
using CoreServiceExtensions = FluxIndex.Core.Application.Services.MetadataAugmentationServiceExtensions;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Services;
using FluxIndex.SDK.Extensions;
using FluxIndex.SDK.AI.Local;
using FluxIndex.Storage.SQLite;
using FluxIndex.Storage.SQLite.Graph;
using FluxIndex.Storage.SQLite.Cache;
using FluxIndex.Storage.PostgreSQL.Graph;
using FluxIndex.Storage.PostgreSQL.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace FluxIndex.SDK;

/// <summary>
/// FluxIndexContext 빌더 - Fluent API로 Retriever와 Indexer 구성
/// AI Provider-agnostic 설계: 외부 AI SDK 없이 LMSupply 기본 사용
/// 소비자 앱에서 IEmbeddingService 구현체 제공 가능
/// </summary>
public class FluxIndexContextBuilder
{
    private readonly IServiceCollection _services;
    private readonly FluxIndexOptions _options;
    private readonly RetrieverOptions _retrieverOptions;
    private readonly IndexerOptions _indexerOptions;
    private bool _suppressStartupMessages = false;
    private bool _disableDefaultTextCompletion = false;
    private bool _disableDefaultReranker = false;

    public FluxIndexContextBuilder()
    {
        _services = new ServiceCollection();
        _options = new FluxIndexOptions();
        _retrieverOptions = new RetrieverOptions();
        _indexerOptions = new IndexerOptions();

        // 기본 서비스 등록
        _services.AddLogging();
        _services.AddMemoryCache();

        // ✅ Default to LMSupply for better developer experience
        // This allows developers to use FluxIndex without requiring external API keys
        // LMSupply provides real embeddings using local ONNX models
        _options.Embedding.Provider = "LMSupply";
        _options.Embedding.ModelName = "default"; // bge-small-en-v1.5
    }

    /// <summary>
    /// PostgreSQL 사용 - Fullstack RAG (Vector + Graph + SemanticCache 모두 활성화)
    /// 개별 구성요소는 이후 오버라이드 가능: UsePostgreSQLGraph(), UsePostgreSQLSemanticCache() 등
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
    /// 개별 구성요소는 이후 오버라이드 가능: UseSQLiteGraph(), UseSQLiteSemanticCache() 등
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
    /// LMSupply 임베딩 사용 (로컬 ONNX 기반, 외부 API 불필요)
    /// Available models: default (bge-small), fast (MiniLM), quality (bge-base),
    /// large (nomic-embed), multilingual (e5-base), or HuggingFace model ID
    /// </summary>
    /// <param name="modelId">Model alias or HuggingFace ID (default: "default")</param>
    public FluxIndexContextBuilder UseLMSupplyEmbedding(string modelId = "default")
    {
        _options.Embedding.Provider = "LMSupply";
        _options.Embedding.ModelName = modelId;
        return this;
    }

    /// <summary>
    /// 다국어 LMSupply 임베딩 사용 (multilingual-e5-base)
    /// 한국어, 영어, 중국어, 일본어 등 다양한 언어 지원
    /// </summary>
    public FluxIndexContextBuilder UseLMSupplyMultilingual()
    {
        _options.Embedding.Provider = "LMSupply";
        _options.Embedding.ModelName = "multilingual";
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
        if (embeddingService == null)
            throw new ArgumentNullException(nameof(embeddingService));

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
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        _options.Embedding.Provider = "Custom";
        _services.AddSingleton(factory);

        return this;
    }

    /// <summary>
    /// Redis 캐시 사용
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

    #region Component Disable Options

    /// <summary>
    /// 그래프 저장소 비활성화 (Vector + SemanticCache만 사용)
    /// </summary>
    public FluxIndexContextBuilder WithoutGraph()
    {
        _options.GraphStore.Provider = "None";
        return this;
    }

    /// <summary>
    /// 시맨틱 캐시 비활성화 (Vector + Graph만 사용)
    /// </summary>
    public FluxIndexContextBuilder WithoutSemanticCache()
    {
        _options.SemanticCache.Provider = "None";
        return this;
    }

    /// <summary>
    /// Vector Store만 사용 (Graph + SemanticCache 비활성화)
    /// </summary>
    public FluxIndexContextBuilder VectorOnly()
    {
        _options.GraphStore.Provider = "None";
        _options.SemanticCache.Provider = "None";
        return this;
    }

    /// <summary>
    /// 기본 TextCompletion 서비스 비활성화.
    /// LMSupply TextCompletion은 HyDE, 메타데이터 enrichment에 사용됨.
    /// 비활성화하면 이러한 기능을 사용할 수 없음.
    /// </summary>
    public FluxIndexContextBuilder WithoutTextCompletion()
    {
        _disableDefaultTextCompletion = true;
        return this;
    }

    /// <summary>
    /// 기본 Reranker 서비스 비활성화.
    /// LMSupply Reranker는 검색 결과의 semantic reranking에 사용됨.
    /// 비활성화하면 기본 점수 기반 정렬만 사용됨.
    /// </summary>
    public FluxIndexContextBuilder WithoutReranker()
    {
        _disableDefaultReranker = true;
        return this;
    }

    /// <summary>
    /// 최소 AI 구성 (Embedding만 사용, TextCompletion/Reranker 비활성화).
    /// 리소스가 제한된 환경이나 기본 RAG만 필요한 경우 사용.
    /// </summary>
    public FluxIndexContextBuilder MinimalAI()
    {
        _disableDefaultTextCompletion = true;
        _disableDefaultReranker = true;
        return this;
    }

    #endregion

    #region Graph Store Configuration

    /// <summary>
    /// SQLite 그래프 저장소 사용 (벡터 저장소와 동일한 연결)
    /// 청크 계층 구조 및 관계를 SQLite에 저장
    /// </summary>
    public FluxIndexContextBuilder UseSQLiteGraph()
    {
        _options.GraphStore.Provider = "SQLite";
        _options.GraphStore.UseVectorStoreConnection = true;
        return this;
    }

    /// <summary>
    /// SQLite 그래프 저장소 사용 (별도 연결 문자열)
    /// </summary>
    public FluxIndexContextBuilder UseSQLiteGraph(string connectionString)
    {
        _options.GraphStore.Provider = "SQLite";
        _options.GraphStore.ConnectionString = connectionString;
        _options.GraphStore.UseVectorStoreConnection = false;
        return this;
    }

    /// <summary>
    /// PostgreSQL 그래프 저장소 사용 (벡터 저장소와 동일한 연결)
    /// JSONB 및 재귀 CTE를 활용한 그래프 저장
    /// </summary>
    public FluxIndexContextBuilder UsePostgreSQLGraph()
    {
        _options.GraphStore.Provider = "PostgreSQL";
        _options.GraphStore.UseVectorStoreConnection = true;
        return this;
    }

    /// <summary>
    /// PostgreSQL 그래프 저장소 사용 (별도 연결 문자열)
    /// </summary>
    public FluxIndexContextBuilder UsePostgreSQLGraph(string connectionString)
    {
        _options.GraphStore.Provider = "PostgreSQL";
        _options.GraphStore.ConnectionString = connectionString;
        _options.GraphStore.UseVectorStoreConnection = false;
        return this;
    }

    #endregion

    #region Semantic Cache Configuration

    /// <summary>
    /// SQLite 시맨틱 캐시 사용 (벡터 저장소와 동일한 연결)
    /// 쿼리 유사도 기반 결과 캐싱
    /// </summary>
    public FluxIndexContextBuilder UseSQLiteSemanticCache(float similarityThreshold = 0.85f)
    {
        _options.SemanticCache.Provider = "SQLite";
        _options.SemanticCache.UseVectorStoreConnection = true;
        _options.SemanticCache.SimilarityThreshold = similarityThreshold;
        return this;
    }

    /// <summary>
    /// SQLite 시맨틱 캐시 사용 (별도 연결 문자열)
    /// </summary>
    public FluxIndexContextBuilder UseSQLiteSemanticCache(string connectionString, float similarityThreshold = 0.85f)
    {
        _options.SemanticCache.Provider = "SQLite";
        _options.SemanticCache.ConnectionString = connectionString;
        _options.SemanticCache.UseVectorStoreConnection = false;
        _options.SemanticCache.SimilarityThreshold = similarityThreshold;
        return this;
    }

    /// <summary>
    /// PostgreSQL 시맨틱 캐시 사용 (벡터 저장소와 동일한 연결)
    /// pgvector 활용 HNSW 인덱스로 빠른 유사도 검색
    /// </summary>
    public FluxIndexContextBuilder UsePostgreSQLSemanticCache(float similarityThreshold = 0.85f)
    {
        _options.SemanticCache.Provider = "PostgreSQL";
        _options.SemanticCache.UseVectorStoreConnection = true;
        _options.SemanticCache.SimilarityThreshold = similarityThreshold;
        return this;
    }

    /// <summary>
    /// PostgreSQL 시맨틱 캐시 사용 (별도 연결 문자열)
    /// </summary>
    public FluxIndexContextBuilder UsePostgreSQLSemanticCache(string connectionString, float similarityThreshold = 0.85f)
    {
        _options.SemanticCache.Provider = "PostgreSQL";
        _options.SemanticCache.ConnectionString = connectionString;
        _options.SemanticCache.UseVectorStoreConnection = false;
        _options.SemanticCache.SimilarityThreshold = similarityThreshold;
        return this;
    }

    /// <summary>
    /// 시맨틱 캐시 고급 설정
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
        // Configure services based on options
        ConfigureVectorStore();
        ConfigureEmbeddingService();
        ConfigureDefaultAIServices();  // ✅ 기본 AI 서비스 (TextCompletion, Reranker)
        ConfigureCacheService();
        ConfigureChunkingService();
        ConfigureGraphStore();
        ConfigureSemanticCache();

        // Register core services
        _services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
        _services.AddSingleton(_retrieverOptions);
        _services.AddSingleton(_indexerOptions);

        // Register in-memory chunk hierarchy repository for SDK
        _services.AddScoped<IChunkHierarchyRepository, InMemoryChunkHierarchyRepository>();

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

            return new Retriever(
                vectorStore,
                documentRepository,
                embeddingService,
                _retrieverOptions,
                cacheService,
                rankFusionService,
                vectorQuantizer,
                loggerFactory.CreateLogger<Retriever>()
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

            return new Indexer(
                vectorStore,
                documentRepository,
                embeddingService,
                chunkingService,
                _indexerOptions,
                loggerFactory.CreateLogger<Indexer>(),
                metadataExtractor
            );
        });

        // Build service provider
        var serviceProvider = _services.BuildServiceProvider();

        // Initialize database if using SQLite (console app support)
        if (_options.VectorStore.Provider?.ToLower() == "sqlite")
        {
            InitializeDatabaseSync(serviceProvider);
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

    /// <summary>
    /// SQLite 데이터베이스 초기화 (콘솔 앱 지원을 위해 동기 초기화)
    /// IHostedService는 IHost를 사용하는 앱에서만 자동 실행되므로,
    /// 콘솔 앱에서는 명시적으로 데이터베이스를 초기화해야 함
    /// </summary>
    private void InitializeDatabaseSync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<SQLiteDbContext>();

        // 데이터베이스 생성 및 마이그레이션
        context.Database.EnsureCreated();

        // 추가 초기화 (필요시)
        var options = scope.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<FluxIndex.Storage.SQLite.SQLiteOptions>>();

        if (options?.Value != null && !options.Value.UseInMemory)
        {
            // WAL 모드 활성화 (성능 향상)
            RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(context.Database, "PRAGMA journal_mode=WAL");

            // 동기화 모드 설정 (성능과 안정성 균형)
            RelationalDatabaseFacadeExtensions.ExecuteSqlRaw(context.Database, "PRAGMA synchronous=NORMAL");
        }
    }

    private void ConfigureVectorStore()
    {
        switch (_options.VectorStore.Provider?.ToLower())
        {
            case "postgresql":
                _services.AddPostgreSQLVectorStore(_options.VectorStore.ConnectionString);
                break;
            case "sqlite":
                _services.AddSQLiteVectorStore(options =>
                {
                    // Parse connection string to extract database path
                    // Format: "Data Source=path/to/db.db" or "Data Source=:memory:" or "Data Source=:memory:;Mode=Memory;Cache=Shared"
                    var connStr = _options.VectorStore.ConnectionString;
                    var isInMemory = connStr.Contains(":memory:");

                    if (isInMemory)
                    {
                        options.UseInMemory = true;
                        // Store full connection string in DatabasePath for shared cache support
                        options.DatabasePath = connStr;
                    }
                    else
                    {
                        // Extract path from "Data Source=..." format
                        var dataSourcePrefix = "Data Source=";
                        var startIndex = connStr.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
                        if (startIndex >= 0)
                        {
                            var path = connStr.Substring(startIndex + dataSourcePrefix.Length).Trim();
                            // Remove any trailing semicolons or parameters
                            var semicolonIndex = path.IndexOf(';');
                            if (semicolonIndex >= 0)
                            {
                                path = path.Substring(0, semicolonIndex).Trim();
                            }
                            options.DatabasePath = path;
                        }
                        else
                        {
                            // Fallback: use connection string as-is (shouldn't happen)
                            options.DatabasePath = connStr;
                        }
                        options.UseInMemory = false;
                    }

                    options.AutoMigrate = true;
                });
                break;
            default:
                // Default to in-memory for testing
                _services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                break;
        }
    }

    private void ConfigureEmbeddingService()
    {
        switch (_options.Embedding.Provider?.ToLower())
        {
            case "LMSupply":
            case "localembedder": // Legacy support
                // ✅ Default: Local ONNX-based embeddings (no API key required)
                FluxIndex.SDK.AI.Local.ServiceCollectionExtensions.AddLMSupplyEmbedding(_services, options =>
                {
                    options.ModelId = !string.IsNullOrEmpty(_options.Embedding.ModelName)
                        ? _options.Embedding.ModelName
                        : "default";
                });
                break;
            case "inmemory":
                // In-memory embedding service for testing (generates random embeddings)
                _services.AddSingleton<IEmbeddingService, InMemoryEmbeddingService>();
                break;
            case "custom":
                // Custom embedding service already registered via UseEmbeddingService()
                // Do nothing - service is already in DI container
                break;
            default:
                // ✅ Fallback to LMSupply if no provider specified
                FluxIndex.SDK.AI.Local.ServiceCollectionExtensions.AddLMSupplyEmbedding(_services);
                break;
        }
    }

    /// <summary>
    /// 기본 AI 서비스 구성 (TextCompletion, Reranker)
    /// 최소구성원칙: 기본 설정만으로 production-quality 결과 제공
    /// - TextCompletion: HyDE query expansion (+20-30% recall)
    /// - Reranker: Semantic reranking (+15-25% precision)
    /// </summary>
    private void ConfigureDefaultAIServices()
    {
        // ✅ TextCompletion: 사용자가 명시적으로 비활성화하지 않았고, 아직 등록되지 않은 경우 기본 등록
        if (!_disableDefaultTextCompletion)
        {
            var hasTextCompletion = _services.Any(d => d.ServiceType == typeof(ITextCompletionService));
            if (!hasTextCompletion)
            {
                // LMSupply TextCompletion (로컬 ONNX, API 키 불필요)
                FluxIndex.SDK.AI.Local.ServiceCollectionExtensions.AddLMSupplyTextCompletion(_services);
            }
        }

        // ✅ Reranker: 사용자가 명시적으로 비활성화하지 않았고, 아직 등록되지 않은 경우 기본 등록
        if (!_disableDefaultReranker)
        {
            var hasReranker = _services.Any(d => d.ServiceType == typeof(IReranker));
            if (!hasReranker)
            {
                // LMSupply Resilient Reranker (모델 실패 시 알고리즘 기반 fallback)
                FluxIndex.SDK.AI.Local.ServiceCollectionExtensions.AddResilientLMSupplyReranker(_services);
            }
        }
    }

    private void ConfigureCacheService()
    {
        switch (_options.Cache.CacheProvider?.ToLower())
        {
            case "redis":
                _services.AddRedisCache(options =>
                {
                    options.ConnectionString = _options.Cache.RedisConnectionString;
                });
                break;
            case "memory":
                _services.AddSingleton<ICacheService, InMemoryCacheService>();
                break;
            default:
                // No cache
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

    private void ConfigureGraphStore()
    {
        var provider = _options.GraphStore.Provider?.ToLower();
        if (string.IsNullOrEmpty(provider) || provider == "none")
        {
            // 기본: InMemory 사용 (이미 Build()에서 등록)
            return;
        }

        var connectionString = _options.GraphStore.UseVectorStoreConnection
            ? _options.VectorStore.ConnectionString
            : _options.GraphStore.ConnectionString;

        switch (provider)
        {
            case "sqlite":
                _services.AddSQLiteGraphStore(options =>
                {
                    // Parse connection string
                    var connStr = connectionString;
                    var isInMemory = connStr.Contains(":memory:");

                    if (isInMemory)
                    {
                        options.UseInMemory = true;
                    }
                    else
                    {
                        var dataSourcePrefix = "Data Source=";
                        var startIndex = connStr.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
                        if (startIndex >= 0)
                        {
                            var path = connStr.Substring(startIndex + dataSourcePrefix.Length).Trim();
                            var semicolonIndex = path.IndexOf(';');
                            if (semicolonIndex >= 0)
                                path = path.Substring(0, semicolonIndex).Trim();
                            options.GraphDatabasePath = path;
                        }
                        else
                        {
                            options.GraphDatabasePath = connStr;
                        }
                        options.UseInMemory = false;
                    }

                    options.AutoMigrate = _options.GraphStore.AutoMigrate;
                });
                break;

            case "postgresql":
                _services.AddPostgreSQLGraphStore(options =>
                {
                    options.ConnectionString = connectionString;
                    options.AutoMigrate = _options.GraphStore.AutoMigrate;
                    options.MaxRecursionDepth = _options.GraphStore.MaxRecursionDepth;
                });
                break;
        }
    }

    private void ConfigureSemanticCache()
    {
        var provider = _options.SemanticCache.Provider?.ToLower();
        if (string.IsNullOrEmpty(provider) || provider == "none")
        {
            // 시맨틱 캐시 미사용
            return;
        }

        var connectionString = _options.SemanticCache.UseVectorStoreConnection
            ? _options.VectorStore.ConnectionString
            : _options.SemanticCache.ConnectionString;

        switch (provider)
        {
            case "sqlite":
                _services.AddSQLiteSemanticCache(options =>
                {
                    // Parse connection string
                    var connStr = connectionString;
                    var isInMemory = connStr.Contains(":memory:");

                    if (isInMemory)
                    {
                        options.UseInMemory = true;
                        options.DatabasePath = connStr;
                    }
                    else
                    {
                        var dataSourcePrefix = "Data Source=";
                        var startIndex = connStr.IndexOf(dataSourcePrefix, StringComparison.OrdinalIgnoreCase);
                        if (startIndex >= 0)
                        {
                            var path = connStr.Substring(startIndex + dataSourcePrefix.Length).Trim();
                            var semicolonIndex = path.IndexOf(';');
                            if (semicolonIndex >= 0)
                                path = path.Substring(0, semicolonIndex).Trim();
                            options.DatabasePath = path;
                        }
                        else
                        {
                            options.DatabasePath = connStr;
                        }
                        options.UseInMemory = false;
                    }

                    options.AutoMigrate = _options.SemanticCache.AutoMigrate;
                    options.DefaultExpiry = _options.SemanticCache.DefaultExpiry;
                    options.MaxEntries = _options.SemanticCache.MaxEntries;
                    options.EnableAutoCleanup = _options.SemanticCache.EnableAutoCleanup;
                    options.CleanupInterval = _options.SemanticCache.CleanupInterval;
                });
                break;

            case "postgresql":
                _services.AddPostgreSQLSemanticCache(options =>
                {
                    options.ConnectionString = connectionString;
                    options.AutoMigrate = _options.SemanticCache.AutoMigrate;
                    options.DefaultExpiry = _options.SemanticCache.DefaultExpiry;
                    options.MaxEntries = _options.SemanticCache.MaxEntries;
                    options.EmbeddingDimensions = _options.SemanticCache.EmbeddingDimensions;
                    options.EnableAutoCleanup = _options.SemanticCache.EnableAutoCleanup;
                    options.CleanupInterval = _options.SemanticCache.CleanupInterval;
                    options.UseUnloggedTable = _options.SemanticCache.UseUnloggedTable;
                });
                break;

            case "redis":
                // Redis는 기존 ICacheService 인프라 활용
                // 여기서는 ISemanticCache가 아닌 Redis 캐시이므로 별도 처리 필요
                // 향후 Redis 시맨틱 캐시 구현 시 추가
                break;
        }
    }
}
