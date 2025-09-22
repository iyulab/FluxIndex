using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.ValueObjects;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Services;
using FluxIndex.SDK.Extensions;
using FluxIndex.AI.OpenAI;
using FluxIndex.Storage.SQLite;
// using FluxIndex.Cache.Redis.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;

namespace FluxIndex.SDK;

/// <summary>
/// FluxIndexContext 빌더 - Fluent API로 Retriever와 Indexer 구성
/// </summary>
public class FluxIndexContextBuilder
{
    private readonly IServiceCollection _services;
    private readonly FluxIndexOptions _options;
    private readonly RetrieverOptions _retrieverOptions;
    private readonly IndexerOptions _indexerOptions;

    public FluxIndexContextBuilder()
    {
        _services = new ServiceCollection();
        _options = new FluxIndexOptions();
        _retrieverOptions = new RetrieverOptions();
        _indexerOptions = new IndexerOptions();
        
        // 기본 서비스 등록
        _services.AddLogging();
        _services.AddMemoryCache();
    }

    /// <summary>
    /// PostgreSQL 벡터 저장소 사용
    /// </summary>
    public FluxIndexContextBuilder UsePostgreSQL(string connectionString)
    {
        _options.VectorStore.Provider = "PostgreSQL";
        _options.VectorStore.ConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// SQLite 벡터 저장소 사용 (로컬 개발용)
    /// </summary>
    public FluxIndexContextBuilder UseSQLite(string databasePath = "fluxindex.db")
    {
        _options.VectorStore.Provider = "SQLite";
        _options.VectorStore.ConnectionString = $"Data Source={databasePath}";
        return this;
    }

    /// <summary>
    /// SQLite 인메모리 벡터 저장소 사용 (테스트용)
    /// </summary>
    public FluxIndexContextBuilder UseSQLiteInMemory()
    {
        _options.VectorStore.Provider = "SQLite";
        _options.VectorStore.ConnectionString = "Data Source=:memory:";
        return this;
    }

    /// <summary>
    /// OpenAI 임베딩 서비스 사용
    /// </summary>
    public FluxIndexContextBuilder UseOpenAI(string apiKey, string model = "text-embedding-3-small")
    {
        _options.Embedding.Provider = "OpenAI";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = model;
        return this;
    }

    /// <summary>
    /// Azure OpenAI 임베딩 서비스 사용
    /// </summary>
    public FluxIndexContextBuilder UseAzureOpenAI(string endpoint, string apiKey, string deploymentName)
    {
        _options.Embedding.Provider = "AzureOpenAI";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = deploymentName;
        _options.Embedding.ProviderSpecificOptions["Endpoint"] = endpoint;
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
    /// 시맨틱 캐싱 활성화 - Redis 벡터 캐시를 통한 쿼리 유사도 기반 캐싱
    /// </summary>
    /*
    public FluxIndexContextBuilder WithSemanticCaching(string redisConnectionString, Action<FluxIndex.Cache.Redis.Configuration.RedisSemanticCacheOptions>? configure = null)
    {
        // Redis 시맨틱 캐시 등록
        if (configure != null)
        {
            _services.AddRedisSemanticCache(options =>
            {
                options.ConnectionString = redisConnectionString;
                configure(options);
            });
        }
        else
        {
            _services.AddRedisSemanticCache(redisConnectionString);
        }

        return this;
    }
    */

    /// <summary>
    /// 개발용 시맨틱 캐싱 활성화 - 로컬 Redis 및 최적화된 설정
    /// </summary>
    /*
    public FluxIndexContextBuilder WithSemanticCachingForDevelopment(string redisConnectionString = "localhost:6379")
    {
        return WithSemanticCaching(redisConnectionString, options =>
        {
            options.DefaultTtl = TimeSpan.FromMinutes(30);
            options.MaxCacheEntries = 1000;
            options.EnableMetrics = true;
            options.EnableAutoCompaction = false;
            options.EnableDetailedLogging = true;
        });
    }
    */

    /// <summary>
    /// 운영용 시맨틱 캐싱 활성화 - 고성능 및 최적화 설정
    /// </summary>
    /*
    public FluxIndexContextBuilder WithSemanticCachingForProduction(string redisConnectionString)
    {
        return WithSemanticCaching(redisConnectionString, options =>
        {
            options.DefaultTtl = TimeSpan.FromHours(24);
            options.MaxCacheEntries = 50000;
            options.EnableMetrics = true;
            options.EnableVectorCompression = true;
            options.EnableAutoCompaction = true;
            options.AutoCompactionInterval = TimeSpan.FromHours(6);
            options.EnableDetailedLogging = false;
        });
    }
    */

    /// <summary>
    /// RAG 품질 평가 시스템 활성화 (소비자가 IRAGEvaluationService 구현체 제공 필요)
    /// </summary>
    public FluxIndexContextBuilder WithEvaluationSystem(string? datasetBasePath = null)
    {
        // 평가 시스템 인프라만 등록 (AI 구현체는 소비자 제공)
        // _services.AddScoped<IGoldenDatasetManager>(sp =>
        //     new GoldenDatasetManager(sp.GetRequiredService<ILogger<GoldenDatasetManager>>(), datasetBasePath));
        // _services.AddScoped<IQualityGateService, QualityGateService>();
        // _services.AddScoped<IEvaluationJobManager, EvaluationJobManager>();

        // 소비자가 IRAGEvaluationService 구현체를 직접 주입해야 함

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

        // 운영용 추가 설정 (EvaluationConfiguration 미구현으로 주석 처리)
        // _services.Configure<EvaluationConfiguration>(config =>
        // {
        //     config.Timeout = TimeSpan.FromMinutes(10);
        //     config.EnableFaithfulnessEvaluation = true;
        //     config.EnableAnswerRelevancyEvaluation = true;
        //     config.EnableContextEvaluation = true;
        // });

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
    /// FluxIndexContext 빌드
    /// </summary>
    public IFluxIndexContext Build()
    {
        // Configure services based on options
        ConfigureVectorStore();
        ConfigureEmbeddingService();
        ConfigureCacheService();
        ConfigureChunkingService();
        
        // Register core services
        _services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
        _services.AddSingleton(_retrieverOptions);
        _services.AddSingleton(_indexerOptions);

        // Register in-memory chunk hierarchy repository for SDK
        _services.AddScoped<IChunkHierarchyRepository, InMemoryChunkHierarchyRepository>();

        // Register hybrid search services (구현체 추후 추가)
        // _services.AddScoped<ISparseRetriever, BM25SparseRetriever>();
        // _services.AddScoped<IHybridSearchService, HybridSearchService>();

        // Register Small-to-Big services (구현체 추후 추가)
        // _services.AddScoped<ISmallToBigRetriever, SmallToBigRetriever>();
        _services.AddMemoryCache(); // For query complexity caching

        // Register Retriever and Indexer as services (needed for Extensions)
        _services.AddScoped<Retriever>(serviceProvider =>
        {
            var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
            var documentRepository = serviceProvider.GetRequiredService<IDocumentRepository>();
            var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();
            var cacheService = serviceProvider.GetService<ICacheService>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var rankFusionService = serviceProvider.GetService<IRankFusionService>();

            return new Retriever(
                vectorStore,
                documentRepository,
                embeddingService,
                _retrieverOptions,
                cacheService,
                rankFusionService,
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

            return new Indexer(
                vectorStore,
                documentRepository,
                embeddingService,
                chunkingService,
                _indexerOptions,
                loggerFactory.CreateLogger<Indexer>()
            );
        });

        // Build service provider
        var serviceProvider = _services.BuildServiceProvider();

        // Get Retriever and Indexer from DI
        var retriever = serviceProvider.GetRequiredService<Retriever>();
        var indexer = serviceProvider.GetRequiredService<Indexer>();

        // Get additional services
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var hybridSearchService = serviceProvider.GetService<IHybridSearchService>();
        var semanticCacheService = serviceProvider.GetService<ISemanticCacheService>();
        var smallToBigRetriever = serviceProvider.GetService<ISmallToBigRetriever>();
        
        // Create and return context
        return new FluxIndexContext(
            retriever,
            indexer,
            serviceProvider,
            loggerFactory.CreateLogger<FluxIndexContext>(),
            semanticCacheService,
            hybridSearchService,
            smallToBigRetriever
        );
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
                    options.ConnectionString = _options.VectorStore.ConnectionString;
                    options.UseInMemory = _options.VectorStore.ConnectionString.Contains(":memory:");
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
            case "openai":
                FluxIndex.AI.OpenAI.ServiceCollectionExtensions.AddOpenAIEmbedding(_services, options =>
                {
                    options.ApiKey = _options.Embedding.ApiKey;
                    options.ModelName = _options.Embedding.ModelName;
                });
                break;
            case "azureopenai":
                FluxIndex.AI.OpenAI.ServiceCollectionExtensions.AddAzureOpenAIEmbedding(_services, options =>
                {
                    options.ApiKey = _options.Embedding.ApiKey;
                    options.ModelName = _options.Embedding.ModelName;
                    options.Endpoint = _options.Embedding.ProviderSpecificOptions.TryGetValue("Endpoint", out var endpoint) ? endpoint?.ToString() : "";
                });
                break;
            default:
                // No default implementation - consumer must provide IEmbeddingService
                throw new InvalidOperationException("IEmbeddingService must be configured. Use UseOpenAI() or provide custom implementation.");
                break;
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
}