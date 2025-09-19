using FluxIndex.Application.Interfaces;
using FluxIndex.Application.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Services;
using FluxIndex.SDK.Extensions;
using FluxIndex.Cache.Redis.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace FluxIndex.SDK;

/// <summary>
/// FluxIndexClient 빌더 - Fluent API로 Retriever와 Indexer 구성
/// </summary>
public class FluxIndexClientBuilder
{
    private readonly IServiceCollection _services;
    private readonly FluxIndexOptions _options;
    private readonly RetrieverOptions _retrieverOptions;
    private readonly IndexerOptions _indexerOptions;

    public FluxIndexClientBuilder()
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
    public FluxIndexClientBuilder UsePostgreSQL(string connectionString)
    {
        _options.VectorStore.Provider = "PostgreSQL";
        _options.VectorStore.ConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// SQLite 벡터 저장소 사용 (로컬 개발용)
    /// </summary>
    public FluxIndexClientBuilder UseSQLite(string databasePath = "fluxindex.db")
    {
        _options.VectorStore.Provider = "SQLite";
        _options.VectorStore.ConnectionString = $"Data Source={databasePath}";
        return this;
    }

    /// <summary>
    /// SQLite 인메모리 벡터 저장소 사용 (테스트용)
    /// </summary>
    public FluxIndexClientBuilder UseSQLiteInMemory()
    {
        _options.VectorStore.Provider = "SQLite";
        _options.VectorStore.ConnectionString = "Data Source=:memory:";
        return this;
    }

    /// <summary>
    /// OpenAI 임베딩 서비스 사용
    /// </summary>
    public FluxIndexClientBuilder UseOpenAI(string apiKey, string model = "text-embedding-3-small")
    {
        _options.Embedding.Provider = "OpenAI";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = model;
        return this;
    }

    /// <summary>
    /// Azure OpenAI 임베딩 서비스 사용
    /// </summary>
    public FluxIndexClientBuilder UseAzureOpenAI(string endpoint, string apiKey, string deploymentName)
    {
        _options.Embedding.Provider = "AzureOpenAI";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = deploymentName;
        _options.Embedding.ProviderSpecificOptions["Endpoint"] = endpoint;
        return this;
    }

    /// <summary>
    /// Redis 캐시 사용
    /// </summary>
    public FluxIndexClientBuilder UseRedisCache(string connectionString)
    {
        _options.Cache.CacheProvider = "Redis";
        _options.Cache.RedisConnectionString = connectionString;
        _options.Cache.EnableSearchCache = true;
        return this;
    }

    /// <summary>
    /// 메모리 캐시 사용
    /// </summary>
    public FluxIndexClientBuilder UseMemoryCache(int maxCacheSize = 1000)
    {
        _options.Cache.CacheProvider = "Memory";
        _options.Cache.MaxCacheSize = maxCacheSize;
        _options.Cache.EnableSearchCache = true;
        return this;
    }

    /// <summary>
    /// 청킹 옵션 설정
    /// </summary>
    public FluxIndexClientBuilder WithChunking(string strategy = "Auto", int chunkSize = 512, int chunkOverlap = 64)
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
    public FluxIndexClientBuilder WithSearchOptions(int defaultMaxResults = 10, float defaultMinScore = 0.5f)
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
    public FluxIndexClientBuilder WithCacheDuration(TimeSpan duration)
    {
        _options.Cache.CacheTTL = duration;
        _retrieverOptions.CacheDuration = duration;
        return this;
    }

    /// <summary>
    /// 병렬 처리 옵션 설정
    /// </summary>
    public FluxIndexClientBuilder WithParallelProcessing(bool enabled = true, int maxParallelism = 4)
    {
        _indexerOptions.ParallelEmbedding = enabled;
        _indexerOptions.MaxParallelEmbedding = maxParallelism;
        return this;
    }

    /// <summary>
    /// 로깅 구성
    /// </summary>
    public FluxIndexClientBuilder WithLogging(Action<ILoggingBuilder> configure)
    {
        _services.AddLogging(configure);
        return this;
    }

    /// <summary>
    /// 시맨틱 캐싱 활성화 - Redis 벡터 캐시를 통한 쿼리 유사도 기반 캐싱
    /// </summary>
    public FluxIndexClientBuilder WithSemanticCaching(string redisConnectionString, Action<FluxIndex.Cache.Redis.Configuration.RedisSemanticCacheOptions>? configure = null)
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

    /// <summary>
    /// 개발용 시맨틱 캐싱 활성화 - 로컬 Redis 및 최적화된 설정
    /// </summary>
    public FluxIndexClientBuilder WithSemanticCachingForDevelopment(string redisConnectionString = "localhost:6379")
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

    /// <summary>
    /// 운영용 시맨틱 캐싱 활성화 - 고성능 및 최적화 설정
    /// </summary>
    public FluxIndexClientBuilder WithSemanticCachingForProduction(string redisConnectionString)
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

    /// <summary>
    /// 고급 서비스 구성 - 확장 패키지에서 사용
    /// </summary>
    public FluxIndexClientBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure?.Invoke(_services);
        return this;
    }

    /// <summary>
    /// FluxIndexClient 빌드
    /// </summary>
    public IFluxIndexClient Build()
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
        
        // Build service provider
        var serviceProvider = _services.BuildServiceProvider();
        
        // Create Retriever and Indexer
        var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
        var documentRepository = serviceProvider.GetRequiredService<IDocumentRepository>();
        var embeddingService = serviceProvider.GetRequiredService<IEmbeddingService>();
        var chunkingService = serviceProvider.GetRequiredService<IChunkingService>();
        var cacheService = serviceProvider.GetService<ICacheService>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        
        var rankFusionService = serviceProvider.GetService<IRankFusionService>() ?? new RankFusionService();
        
        var retriever = new Retriever(
            vectorStore,
            documentRepository,
            embeddingService,
            _retrieverOptions,
            cacheService,
            rankFusionService,
            loggerFactory.CreateLogger<Retriever>()
        );
        
        var indexer = new Indexer(
            vectorStore,
            documentRepository,
            embeddingService,
            chunkingService,
            _indexerOptions,
            loggerFactory.CreateLogger<Indexer>()
        );
        
        // Create and return client
        return new FluxIndexClient(
            retriever,
            indexer,
            loggerFactory.CreateLogger<FluxIndexClient>()
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
                _services.AddOpenAIEmbedding(options =>
                {
                    options.ApiKey = _options.Embedding.ApiKey;
                    options.ModelName = _options.Embedding.ModelName;
                });
                break;
            case "azureopenai":
                _services.AddAzureOpenAIEmbedding(options =>
                {
                    options.ApiKey = _options.Embedding.ApiKey;
                    options.DeploymentName = _options.Embedding.ModelName;
                    options.Endpoint = _options.Embedding.ProviderSpecificOptions["Endpoint"]?.ToString() ?? "";
                });
                break;
            default:
                // Default to mock embedding for testing
                _services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
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
            new SimpleChunkingService(
                _indexerOptions.ChunkSize,
                _indexerOptions.ChunkOverlap
            )
        );
    }
}