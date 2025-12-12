using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Models;
using FluxIndex.Core.Services;
using CoreServiceExtensions = FluxIndex.Core.Application.Services.MetadataAugmentationServiceExtensions;
using FluxIndex.SDK.Configuration;
using FluxIndex.SDK.Services;
using FluxIndex.SDK.Extensions;
using FluxIndex.AI.LocalEmbedder;
using FluxIndex.AI.OpenAI;
using FluxIndex.Storage.SQLite;
// using FluxIndex.Cache.Redis.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;

// Note: We're using FluxIndex.Core.Application.Interfaces.IEmbeddingService internally
// Fully qualified names used where ambiguity exists

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

        // ✅ Default to LocalEmbedder for better developer experience
        // This allows developers to use FluxIndex without requiring external API keys
        // LocalEmbedder provides real embeddings using local ONNX models
        _options.Embedding.Provider = "LocalEmbedder";
        _options.Embedding.ModelName = "all-MiniLM-L6-v2"; // Default LocalEmbedder model
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
        // Shared cache mode for in-memory database to allow multiple connections
        _options.VectorStore.ConnectionString = "Data Source=:memory:;Mode=Memory;Cache=Shared";
        return this;
    }

    /// <summary>
    /// OpenAI 임베딩 서비스 사용
    /// </summary>
    /// <param name="apiKey">OpenAI API key</param>
    /// <param name="embeddingModel">Embedding model (e.g., "text-embedding-3-small")</param>
    /// <param name="completionModel">Optional text completion model (e.g., "gpt-5-nano" recommended for cost-efficiency). If provided, ITextCompletionService will be registered.</param>
    public FluxIndexContextBuilder UseOpenAI(
        string apiKey,
        string embeddingModel = "text-embedding-3-small",
        string? completionModel = null)
    {
        _options.Embedding.Provider = "OpenAI";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = embeddingModel;

        // Register text completion service if completion model is specified
        if (!string.IsNullOrEmpty(completionModel))
        {
            _services.AddOpenAITextCompletion(options =>
            {
                options.ApiKey = apiKey;
                options.ModelName = completionModel;
            });
        }

        return this;
    }

    /// <summary>
    /// Azure OpenAI 임베딩 서비스 사용
    /// </summary>
    /// <param name="endpoint">Azure OpenAI endpoint URL</param>
    /// <param name="apiKey">Azure OpenAI API key</param>
    /// <param name="embeddingDeployment">Embedding deployment name</param>
    /// <param name="completionDeployment">Optional text completion deployment name (GPT-5 series recommended). If provided, ITextCompletionService will be registered.</param>
    public FluxIndexContextBuilder UseAzureOpenAI(
        string endpoint,
        string apiKey,
        string embeddingDeployment,
        string? completionDeployment = null)
    {
        _options.Embedding.Provider = "AzureOpenAI";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = embeddingDeployment;
        _options.Embedding.ProviderSpecificOptions["Endpoint"] = endpoint;

        // Register text completion service if completion deployment is specified
        if (!string.IsNullOrEmpty(completionDeployment))
        {
            _services.AddAzureOpenAITextCompletion(options =>
            {
                options.Endpoint = endpoint;
                options.ApiKey = apiKey;
                options.ModelName = completionDeployment;
            });
        }

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
    /// LocalEmbedder 사용 (로컬 ONNX 기반, 외부 API 불필요)
    /// Available models: all-MiniLM-L6-v2 (default), all-mpnet-base-v2, bge-small-en-v1.5, multilingual-e5-small
    /// </summary>
    /// <param name="modelId">Model identifier (default: "all-MiniLM-L6-v2")</param>
    public FluxIndexContextBuilder UseLocalEmbedder(string modelId = "all-MiniLM-L6-v2")
    {
        _options.Embedding.Provider = "LocalEmbedder";
        _options.Embedding.ModelName = modelId;
        return this;
    }

    /// <summary>
    /// 다국어 LocalEmbedder 사용 (multilingual-e5-small)
    /// 한국어, 영어, 중국어, 일본어 등 다양한 언어 지원
    /// </summary>
    public FluxIndexContextBuilder UseLocalEmbedderMultilingual()
    {
        _options.Embedding.Provider = "LocalEmbedder";
        _options.Embedding.ModelName = "multilingual-e5-small";
        return this;
    }

    /// <summary>
    /// GPUStack 임베딩 서비스 사용 (OpenAI-compatible self-hosted inference)
    /// </summary>
    /// <param name="endpoint">GPUStack endpoint (e.g., "http://localhost:80")</param>
    /// <param name="apiKey">GPUStack API key</param>
    /// <param name="modelName">Embedding model name (e.g., "BAAI/bge-m3")</param>
    /// <param name="dimensions">Optional embedding dimensions</param>
    public FluxIndexContextBuilder UseGPUStack(
        string endpoint,
        string apiKey,
        string modelName,
        int? dimensions = null)
    {
        _options.Embedding.Provider = "GPUStack";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = modelName;
        _options.Embedding.ProviderSpecificOptions["Endpoint"] = endpoint;
        if (dimensions.HasValue)
        {
            _options.Embedding.ProviderSpecificOptions["Dimensions"] = dimensions.Value;
        }
        return this;
    }

    /// <summary>
    /// OpenAI-compatible 임베딩 서비스 사용 (Ollama, LM Studio, vLLM 등)
    /// </summary>
    /// <param name="endpoint">API endpoint URL</param>
    /// <param name="apiKey">API key (may be optional for some providers)</param>
    /// <param name="modelName">Embedding model name</param>
    /// <param name="dimensions">Optional embedding dimensions</param>
    public FluxIndexContextBuilder UseOpenAICompatible(
        string endpoint,
        string apiKey,
        string modelName,
        int? dimensions = null)
    {
        _options.Embedding.Provider = "OpenAICompatible";
        _options.Embedding.ApiKey = apiKey;
        _options.Embedding.ModelName = modelName;
        _options.Embedding.ProviderSpecificOptions["Endpoint"] = endpoint;
        if (dimensions.HasValue)
        {
            _options.Embedding.ProviderSpecificOptions["Dimensions"] = dimensions.Value;
        }
        return this;
    }

    /// <summary>
    /// DI 기반 임베딩 서비스 사용 (Interface Provider Pattern)
    /// 소비자가 제공한 IEmbeddingService 구현체를 직접 등록
    /// </summary>
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
    /// AI 공급자 자동 선택 (provider/model 형식 지원)
    /// 예: "openai/gpt-5-nano", "anthropic/claude-sonnet-4-5", "azure/deployment-name"
    /// </summary>
    public FluxIndexContextBuilder UseAIProvider(string modelSpec, string apiKey, Dictionary<string, object>? options = null)
    {
        var (provider, modelName) = ParseModelSpec(modelSpec);

        return provider.ToLowerInvariant() switch
        {
            "openai" => UseOpenAI(apiKey, modelName),

            "anthropic" => throw new NotImplementedException(
                "Anthropic embedding support is not yet implemented. " +
                "Currently, only OpenAI and Azure OpenAI are supported for embeddings."),

            "azure" => UseAzureProviderWithOptions(apiKey, modelName, options),

            "google" => throw new NotImplementedException(
                "Google (Gemini) embedding support is not yet implemented. " +
                "Currently, only OpenAI and Azure OpenAI are supported for embeddings."),

            _ => throw new ArgumentException($"Unknown AI provider: {provider}. Supported providers: openai, azure")
        };
    }

    /// <summary>
    /// Azure provider helper method
    /// </summary>
    private FluxIndexContextBuilder UseAzureProviderWithOptions(string apiKey, string modelName, Dictionary<string, object>? options)
    {
        var endpoint = options?.TryGetValue("endpoint", out var ep) == true ? ep?.ToString() : null;
        if (string.IsNullOrEmpty(endpoint))
            throw new ArgumentException("Azure endpoint is required. Provide it in options dictionary with key 'endpoint'.");
        return UseAzureOpenAI(endpoint!, apiKey, modelName);
    }

    /// <summary>
    /// 모델 스펙 파싱: "provider/model-name" → (provider, modelName)
    /// </summary>
    private (string provider, string modelName) ParseModelSpec(string modelSpec)
    {
        if (string.IsNullOrWhiteSpace(modelSpec))
            throw new ArgumentException("Model specification cannot be empty", nameof(modelSpec));

        var parts = modelSpec.Split('/', 2);
        if (parts.Length != 2)
            throw new ArgumentException(
                $"Invalid model specification format: '{modelSpec}'. " +
                "Expected format: 'provider/model-name' (e.g., 'openai/gpt-5-nano', 'anthropic/claude-sonnet-4-5')",
                nameof(modelSpec));

        var provider = parts[0].Trim();
        var modelName = parts[1].Trim();

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException(
                $"Invalid model specification: '{modelSpec}'. Both provider and model name must be non-empty.",
                nameof(modelSpec));

        return (provider, modelName);
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
    /// AI 메타데이터 추출 활성화 (OpenAI 기반)
    /// </summary>
    public FluxIndexContextBuilder WithOpenAIMetadataExtractor(
        string apiKey,
        string? endpoint = null,
        MetadataSchema schema = MetadataSchema.General,
        MetadataExtractionStrategy strategy = MetadataExtractionStrategy.Smart,
        float minConfidence = 0.6f)
    {
        // Register OpenAI metadata extraction services
        _services.AddOpenAIMetadataExtractor(options =>
        {
            options.ApiKey = apiKey;
            if (!string.IsNullOrEmpty(endpoint))
            {
                options.Endpoint = endpoint;
            }
        });

        // Configure IndexingOptions with AI metadata settings
        _indexerOptions.CustomOptions["EnableAIMetadataExtraction"] = true;
        _indexerOptions.CustomOptions["MetadataSchema"] = schema.ToString();
        _indexerOptions.CustomOptions["MetadataExtractionStrategy"] = strategy.ToString();
        _indexerOptions.CustomOptions["MinMetadataConfidence"] = minConfidence;

        return this;
    }

    /// <summary>
    /// AI 메타데이터 추출 활성화 (커스텀 프롬프트 사용)
    /// </summary>
    public FluxIndexContextBuilder WithCustomMetadataExtractor(
        string apiKey,
        string customPrompt,
        string? endpoint = null,
        MetadataExtractionStrategy strategy = MetadataExtractionStrategy.Smart,
        float minConfidence = 0.6f)
    {
        // Register OpenAI metadata extraction services
        _services.AddOpenAIMetadataExtractor(options =>
        {
            options.ApiKey = apiKey;
            if (!string.IsNullOrEmpty(endpoint))
            {
                options.Endpoint = endpoint;
            }
        });

        // Configure IndexingOptions with custom metadata settings
        _indexerOptions.CustomOptions["EnableAIMetadataExtraction"] = true;
        _indexerOptions.CustomOptions["MetadataSchema"] = MetadataSchema.Custom.ToString();
        _indexerOptions.CustomOptions["MetadataExtractionStrategy"] = strategy.ToString();
        _indexerOptions.CustomOptions["MinMetadataConfidence"] = minConfidence;
        _indexerOptions.CustomOptions["CustomMetadataPrompt"] = customPrompt;

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
            case "localembedder":
                // ✅ Default: Local ONNX-based embeddings (no API key required)
                FluxIndex.AI.LocalEmbedder.ServiceCollectionExtensions.AddLocalEmbedder(_services, options =>
                {
                    options.ModelId = !string.IsNullOrEmpty(_options.Embedding.ModelName)
                        ? _options.Embedding.ModelName
                        : "all-MiniLM-L6-v2";
                });
                break;
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
            case "gpustack":
                FluxIndex.AI.OpenAI.ServiceCollectionExtensions.AddGPUStackEmbedding(_services,
                    endpoint: _options.Embedding.ProviderSpecificOptions.TryGetValue("Endpoint", out var gpuEndpoint) ? gpuEndpoint?.ToString() ?? "" : "",
                    apiKey: _options.Embedding.ApiKey,
                    modelName: _options.Embedding.ModelName,
                    dimensions: _options.Embedding.ProviderSpecificOptions.TryGetValue("Dimensions", out var gpuDim) && gpuDim is int gpuDimVal ? gpuDimVal : null);
                break;
            case "openaicompatible":
                FluxIndex.AI.OpenAI.ServiceCollectionExtensions.AddOpenAICompatibleEmbedding(_services,
                    endpoint: _options.Embedding.ProviderSpecificOptions.TryGetValue("Endpoint", out var compatEndpoint) ? compatEndpoint?.ToString() ?? "" : "",
                    apiKey: _options.Embedding.ApiKey,
                    modelName: _options.Embedding.ModelName,
                    dimensions: _options.Embedding.ProviderSpecificOptions.TryGetValue("Dimensions", out var compatDim) && compatDim is int compatDimVal ? compatDimVal : null);
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
                // ✅ Fallback to LocalEmbedder if no provider specified
                FluxIndex.AI.LocalEmbedder.ServiceCollectionExtensions.AddLocalEmbedder(_services);
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