using FileFlux;
using FluxIndex.Cache.Redis.Extensions;
using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Reranking;
using FluxIndex.Core.Interfaces;
using FluxIndex.Core.Services;
using CoreQualityThresholds = FluxIndex.Core.Domain.Models.QualityThresholds;
using FluxIndex.SDK.Extensions.FluxImprover;
using FluxIndex.SDK.Extensions.FluxImprover.Services;
using FluxIndex.SDK;
using FluxIndex.Storage.Neo4j;
using FluxIndex.Stack.Application.Interfaces.Repositories;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Application.Services;
using FluxIndex.Stack.Infrastructure.Data;
using FluxIndex.Stack.Infrastructure.Repositories;
using FluxIndex.Stack.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

// Type aliases to resolve collisions with Core interfaces
using CoreTextCompletionService = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using CoreEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;
using ISemanticCacheService = FluxIndex.Core.Application.Interfaces.ISemanticCacheService;
using IQueryTransformationService = FluxIndex.Core.Application.Interfaces.IQueryTransformationService;
using QueryTransformationService = FluxIndex.Core.Application.Services.QueryTransformationService;

// Stack-specific types (avoid ambiguity with Core types)
using StackIDocumentRepository = FluxIndex.Stack.Application.Interfaces.Repositories.IDocumentRepository;
using StackIChunkingService = FluxIndex.Stack.Application.Interfaces.Services.IChunkingService;
using StackSearchService = FluxIndex.Stack.Application.Services.SearchService;
using StackIndexingService = FluxIndex.Stack.Application.Services.IndexingService;

// FileVault types (Extensions.FileVault replaces Stack.Vault)
using FluxIndex.Extensions.FileVault.Extensions;

namespace FluxIndex.Stack.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all infrastructure services to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddRepositories();
        services.AddApplicationServices();
        services.AddFluxIndexSDK(configuration);
        services.AddFileFluxChunking(configuration);
        services.AddLocalRerankerService(configuration);
        services.AddTextCompletionService(configuration);
        services.AddFluxImproverServices(configuration);
        services.AddRedisCache(configuration);
        services.AddAdvancedSearchServices(configuration);
        services.AddNeo4jGraphService(configuration);
        services.AddFileVault(configuration);

        return services;
    }

    /// <summary>
    /// Adds FileFlux intelligent chunking services.
    /// </summary>
    public static IServiceCollection AddFileFluxChunking(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register FileFlux core services
        services.AddFileFlux();

        // Configure FileFlux chunking options
        services.Configure<FileFluxChunkingConfiguration>(options =>
        {
            var section = configuration.GetSection("FileFlux");
            if (section.Exists())
            {
                options.DefaultStrategy = section.GetValue<string>("Strategy") ?? "Auto";
                options.DefaultMaxChunkSize = section.GetValue<int>("MaxChunkSize", 1024);
                options.DefaultOverlapSize = section.GetValue<int>("OverlapSize", 128);
                options.EnableLanguageDetection = section.GetValue<bool>("EnableLanguageDetection", true);
            }
        });

        // Register FileFlux-based chunking service
        services.AddScoped<StackIChunkingService, FileFluxChunkingService>();

        return services;
    }

    /// <summary>
    /// Adds database context with PostgreSQL.
    /// Database name is automatically suffixed with embedding dimension (e.g., fluxindex_384)
    /// to prevent vector dimension mismatch errors when switching embedding models.
    /// </summary>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("PostgreSQL connection string not configured.");

        // Get embedding dimension for dimension-based DB naming
        var embeddingDimension = configuration.GetValue<int>("FluxIndex:Embedding:Dimension", 384);

        // Set dimension for DbContext schema creation
        ServiceDbContext.EmbeddingDimension = embeddingDimension;

        // Modify database name to include dimension suffix
        connectionString = AppendDimensionToDatabaseName(connectionString, embeddingDimension);

        // Build NpgsqlDataSource with required features
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        dataSourceBuilder.EnableDynamicJson(); // Required for Dictionary<string, object> to JSONB
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ServiceDbContext>(options =>
        {
            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.UseVector(); // EF Core level pgvector support
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            });
        });

        return services;
    }

    /// <summary>
    /// Appends embedding dimension to the database name in connection string.
    /// Example: "Database=fluxindex" → "Database=fluxindex_384"
    /// </summary>
    private static string AppendDimensionToDatabaseName(string connectionString, int dimension)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var originalDb = builder.Database ?? "fluxindex";

        // Don't append if dimension suffix already exists
        if (!originalDb.EndsWith($"_{dimension}", StringComparison.Ordinal))
        {
            // Remove any existing dimension suffix (e.g., _1536, _384)
            var baseName = System.Text.RegularExpressions.Regex.Replace(originalDb, @"_\d+$", "");
            builder.Database = $"{baseName}_{dimension}";
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Adds repository implementations.
    /// </summary>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<StackIDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentChunkRepository, DocumentChunkRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IIndexingJobRepository, IndexingJobRepository>();
        services.AddScoped<IIndexingJobLogRepository, IndexingJobLogRepository>();
        services.AddScoped<ISearchHistoryRepository, SearchHistoryRepository>();
        services.AddScoped<IAiProviderSettingsRepository, AiProviderSettingsRepository>();

        // Adaptive embedding system repositories
        services.AddScoped<IEmbeddingModelRepository, EmbeddingModelRepository>();
        services.AddScoped<IChunkEmbeddingRepository, ChunkEmbeddingRepository>();
        services.AddScoped<IReindexingJobRepository, ReindexingJobRepository>();

        // Vault repositories removed - now using Extensions.FileVault
        // See AddFileVault() in AddInfrastructure()

        return services;
    }

    /// <summary>
    /// Adds application service implementations.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<ISearchService, StackSearchService>();
        services.AddScoped<IIndexingService, StackIndexingService>();
        services.AddSingleton<IRuleBasedMetadataExtractor, RuleBasedMetadataExtractor>();
        services.AddScoped<IChunkService, ChunkService>();
        services.AddScoped<IAiProviderSettingsService, AiProviderSettingsService>();
        // ChunkEnrichmentService with explicit dependency injection
        // Requires FluxImproverPipeline and ITextCompletionService to be registered
        services.AddScoped<IChunkEnrichmentService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChunkEnrichmentService>>();
            var pipeline = sp.GetService<FluxImproverPipeline>();
            var textCompletionService = sp.GetService<CoreTextCompletionService>();
            return new ChunkEnrichmentService(logger, pipeline, textCompletionService);
        });

        // RAG Evaluation search provider - bridges Stack search to Core evaluation framework
        services.AddScoped<IEvaluationSearchProvider>(sp =>
        {
            var searchService = sp.GetRequiredService<ISearchService>();
            var chunkRepo = sp.GetRequiredService<IDocumentChunkRepository>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StackEvaluationSearchProvider>>();
            var textCompletionService = sp.GetService<CoreTextCompletionService>();
            return new StackEvaluationSearchProvider(searchService, chunkRepo, logger, textCompletionService);
        });

        // RAG Evaluation framework services
        // CoreRAGEvaluationServiceAdapter - bridges FluxImprover to Core's IRAGEvaluationService
        services.AddScoped<IRAGEvaluationService>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CoreRAGEvaluationServiceAdapter>>();
            var fluxImproverService = sp.GetService<FluxIndex.SDK.Extensions.FluxImprover.Services.RAGEvaluationService>();
            return new CoreRAGEvaluationServiceAdapter(logger, fluxImproverService);
        });

        // GoldenDatasetManager for managing test datasets
        services.AddScoped<IGoldenDatasetManager>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoldenDatasetManager>>();
            var datasetPath = Path.Combine(Directory.GetCurrentDirectory(), "datasets");
            return new GoldenDatasetManager(logger, datasetPath);
        });

        // EvaluationJobManager for running and tracking evaluation jobs
        services.AddScoped<IEvaluationJobManager>(sp =>
        {
            var ragEvalService = sp.GetRequiredService<IRAGEvaluationService>();
            var datasetManager = sp.GetRequiredService<IGoldenDatasetManager>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EvaluationJobManager>>();
            var searchProvider = sp.GetService<IEvaluationSearchProvider>();
            return new EvaluationJobManager(ragEvalService, datasetManager, logger, searchProvider);
        });

        // Quality Gate Service for CI/CD integration
        services.AddScoped<IQualityGateService>(sp =>
        {
            var evaluationService = sp.GetRequiredService<IRAGEvaluationService>();
            var datasetManager = sp.GetRequiredService<IGoldenDatasetManager>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QualityGateService>>();
            var resultCache = sp.GetService<IEvaluationResultCache>();
            return new QualityGateService(evaluationService, datasetManager, logger, resultCache);
        });

        // Late Chunking Embedding Service for contextual embeddings
        services.AddLateChunking(options =>
        {
            options.MaxDocumentLength = 8000;
            options.ContextIntegrationMode = ContextIntegrationMode.SurroundingContext;
            options.DocumentContextWeight = 0.3;
            options.SurroundingContextSize = 500;
        });

        // Adaptive embedding system services
        // Note: ReindexingService must be registered before EmbeddingModelService
        // because EmbeddingModelService depends on IReindexingService
        services.AddScoped<IReindexingService, ReindexingService>();
        services.AddScoped<IEmbeddingModelService, EmbeddingModelService>();

        // Query Transformation Service for Multi-Hypothetical HyDE
        services.Configure<QueryTransformationOptions>(options =>
        {
            options.MaxMultiQueryCount = 5;
            options.EnableCaching = true;
            options.HyDETemperature = 0.7f;
        });
        services.AddScoped<IQueryTransformationService>(sp =>
        {
            var textCompletionService = sp.GetService<CoreTextCompletionService>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QueryTransformationOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QueryTransformationService>>();
            return new QueryTransformationService(textCompletionService, options, logger);
        });

        // Register embedding service factory for dynamic provider creation
        services.AddSingleton<IEmbeddingServiceFactory, EmbeddingServiceFactory>();

        // Register dynamic embedding provider that reads from AiProviderSettings
        // Falls back to LocalEmbedder when no API is configured
        services.AddSingleton<DynamicEmbeddingProvider>();
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            // Use DynamicEmbeddingProvider which reads from Settings DB
            return sp.GetRequiredService<DynamicEmbeddingProvider>();
        });
        services.AddSingleton<IEmbeddingProviderCache>(sp =>
        {
            // IEmbeddingProviderCache is implemented by DynamicEmbeddingProvider
            return sp.GetRequiredService<DynamicEmbeddingProvider>();
        });

        // Also keep SDK-based provider for backward compatibility
        services.AddScoped<FluxIndexEmbeddingProvider>(sp =>
        {
            var embeddingService = sp.GetService<FluxIndex.SDK.Interfaces.IEmbeddingService>();
            if (embeddingService != null)
            {
                return new FluxIndexEmbeddingProvider(embeddingService);
            }
            return null!;
        });

        // Register document content provider
        services.AddSingleton<IDocumentContentProvider, FileSystemContentProvider>();

        return services;
    }

    /// <summary>
    /// Adds LocalReranker cross-encoder based semantic reranking via LMSupply.Reranker.
    /// Configuration section: "LocalReranker" with "Enabled" (bool) and optional "ModelId" (string).
    /// </summary>
    public static IServiceCollection AddLocalRerankerService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("LocalReranker");

        if (!section.Exists() || !section.GetValue<bool>("Enabled", false))
        {
            return services;
        }

        var modelId = section.GetValue<string>("ModelId") ?? "default";

        services.AddSingleton<IReranker>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<LMSupplyRerankerWrapper>();
            var rerankerModel = LMSupply.Reranker.LocalReranker.LoadAsync(modelId).GetAwaiter().GetResult();
            return new LMSupplyRerankerWrapper(rerankerModel, logger);
        });

        return services;
    }

    /// <summary>
    /// Adds dynamic text completion service with database-driven configuration.
    /// </summary>
    public static IServiceCollection AddTextCompletionService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register text completion service factory
        services.AddSingleton<ITextCompletionServiceFactory, TextCompletionServiceFactory>();

        // Register dynamic text completion provider that reads from AiProviderSettings
        services.AddSingleton<DynamicTextCompletionProvider>();
        services.AddSingleton<CoreTextCompletionService>(sp =>
        {
            return sp.GetRequiredService<DynamicTextCompletionProvider>();
        });
        services.AddSingleton<ITextCompletionProviderCache>(sp =>
        {
            return sp.GetRequiredService<DynamicTextCompletionProvider>();
        });

        return services;
    }

    /// <summary>
    /// Adds FluxImprover services for chunk enrichment, QA generation, and RAG evaluation.
    /// Requires ITextCompletionService to be registered.
    /// </summary>
    public static IServiceCollection AddFluxImproverServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("FluxImprover");
        var enableFluxImprover = section.GetValue<bool>("Enabled", true);

        if (!enableFluxImprover)
        {
            return services;
        }

        // Register FluxImprover integration using the one-stop method
        // This registers all FluxImprover services and wrappers
        services.AddFluxIndexFluxImprover();

        // Register FluxImproverPipeline for orchestration
        services.AddFluxImproverPipeline();

        return services;
    }

    /// <summary>
    /// Adds Redis caching support with semantic cache.
    /// </summary>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        if (string.IsNullOrEmpty(redisConnection))
        {
            return services;
        }

        // Register standard Redis distributed cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "FluxIndex:";
        });

        // Register EmbeddingProvider to EmbeddingService adapter for semantic cache
        services.AddSingleton<CoreEmbeddingService>(sp =>
        {
            var embeddingProvider = sp.GetRequiredService<IEmbeddingProvider>();
            return new EmbeddingProviderToEmbeddingServiceAdapter(embeddingProvider);
        });

        // Register Redis semantic cache service
        services.AddRedisSemanticCache(options =>
        {
            var section = configuration.GetSection("SemanticCache");
            options.ConnectionString = redisConnection;
            options.KeyPrefix = section.GetValue<string>("KeyPrefix") ?? "fluxindex:semantic:";
            options.DefaultSimilarityThreshold = section.GetValue<float>("SimilarityThreshold", 0.95f);
            options.DefaultTtl = TimeSpan.FromHours(section.GetValue<int>("TtlHours", 1));
            options.MaxCacheEntries = section.GetValue<long>("MaxEntries", 10000);
            options.MaxParallelism = section.GetValue<int>("MaxParallelism", Environment.ProcessorCount);
            options.DatabaseNumber = section.GetValue<int>("DatabaseNumber", 1);
            options.EnableMetrics = section.GetValue<bool>("EnableMetrics", true);
        });

        return services;
    }

    /// <summary>
    /// Adds advanced search services with dynamic fusion, listwise reranking,
    /// entity extraction, and community-based search.
    /// </summary>
    public static IServiceCollection AddAdvancedSearchServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("AdvancedSearch");
        var enabled = section.GetValue<bool>("Enabled", true);

        if (!enabled)
        {
            return services;
        }

        // Register Core services for advanced search

        // 1. Query Complexity Analyzer for understanding query characteristics
        services.AddSingleton<IQueryComplexityAnalyzer, QueryComplexityAnalyzer>();

        // 2. Dynamic Fusion Service (Query-adaptive alpha tuning)
        services.AddSingleton<IDynamicFusionService>(sp =>
        {
            var queryAnalyzer = sp.GetRequiredService<IQueryComplexityAnalyzer>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DynamicFusionService>>();
            return new DynamicFusionService(queryAnalyzer, logger);
        });

        // 3. Listwise Reranker for advanced reranking
        services.AddSingleton<IListwiseReranker>(sp =>
        {
            var llmService = sp.GetService<CoreTextCompletionService>();
            var embeddingService = sp.GetService<CoreEmbeddingService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ListwiseReranker>>();
            return new ListwiseReranker(llmService, embeddingService, logger);
        });

        // 4. Advanced Entity Extraction Service
        services.AddSingleton<IAdvancedEntityExtractionService>(sp =>
        {
            var llmService = sp.GetService<CoreTextCompletionService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EntityExtractionService>>();
            return new EntityExtractionService(logger, llmService);
        });

        // 5. Leiden Community Service for hierarchical community detection
        services.AddSingleton<ILeidenCommunityService>(sp =>
        {
            var llmService = sp.GetService<CoreTextCompletionService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LeidenCommunityService>>();
            return new LeidenCommunityService(logger, llmService);
        });

        // 6. Advanced Search Service that orchestrates all the above
        services.AddScoped<IAdvancedSearchService, AdvancedSearchService>();

        // Note: IHybridSearchService is registered in AddFluxIndexSDK from context.ServiceProvider
        // This ensures proper dependency chain from the SDK's built services

        // Note: SmartSearchService has been merged into SearchService.Auto mode
        // Use SearchRequest.Mode = SearchMode.Auto (default) for intelligent search optimization

        // 7. Self-RAG Service for iterative search with quality assessment
        services.AddSelfRAGService(options =>
        {
            var selfRagSection = section.GetSection("SelfRAG");
            if (selfRagSection.Exists())
            {
                options.DefaultMaxIterations = selfRagSection.GetValue<int>("MaxIterations", 3);
                options.DefaultQualityThreshold = selfRagSection.GetValue<double>("QualityThreshold", 0.7);
                options.UseLlmForRefinement = selfRagSection.GetValue<bool>("UseLlmForRefinement", true);
            }
        });

        // 8. Corrective RAG Service for document grading and correction
        services.AddCorrectiveRAGService(options =>
        {
            var cragSection = section.GetSection("CorrectiveRAG");
            if (cragSection.Exists())
            {
                options.CorrectThreshold = cragSection.GetValue<double>("CorrectThreshold", 0.7);
                options.AmbiguousThreshold = cragSection.GetValue<double>("AmbiguousThreshold", 0.4);
                options.UseLlmForRefinement = cragSection.GetValue<bool>("UseLlmForRefinement", true);
            }
        });

        // 9. Iterative Retrieval Service for multi-hop reasoning (IRCOT, Self-Ask)
        services.AddIterativeRetrieval(options =>
        {
            var iterativeSection = section.GetSection("IterativeRetrieval");
            if (iterativeSection.Exists())
            {
                options.MaxIterations = iterativeSection.GetValue<int>("MaxIterations", 5);
                options.MaxDocsPerIteration = iterativeSection.GetValue<int>("MaxDocsPerIteration", 5);
                options.UseLlmReasoning = iterativeSection.GetValue<bool>("UseLlmReasoning", true);
                options.ConfidenceThreshold = iterativeSection.GetValue<float>("ConfidenceThreshold", 0.8f);
            }
        });

        // 10. Retrieval Verification Service for CRAG patterns and hallucination detection
        services.AddRetrievalVerification(options =>
        {
            var verificationSection = section.GetSection("RetrievalVerification");
            if (verificationSection.Exists())
            {
                options.AlwaysCheckHallucination = verificationSection.GetValue<bool>("AlwaysCheckHallucination", false);
                options.UseLlmForGrading = verificationSection.GetValue<bool>("UseLlmForGrading", false);
                options.MinGroundingScore = verificationSection.GetValue<double>("MinGroundingScore", 0.5);
            }
        });

        // 11. Agentic Retrieval Router for intelligent strategy selection
        // Routes queries to optimal retrieval strategies based on query analysis
        // Supports: HybridSearch, SelfRAG, CorrectiveRAG, SmallToBig, Iterative, etc.
        services.AddAgenticRetrievalRouter();

        return services;
    }

    /// <summary>
    /// Adds Neo4j graph database services for GraphRAG capabilities.
    /// Provides relationship-aware search and entity traversal.
    /// </summary>
    public static IServiceCollection AddNeo4jGraphService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var neo4jSection = configuration.GetSection("Neo4j");
        var connectionString = configuration.GetConnectionString("Neo4j");

        // Check if Neo4j is configured
        if (string.IsNullOrEmpty(connectionString))
        {
            // Neo4j not configured - Neo4jGraphService will operate in degraded mode
            // Don't register IGraphStore, let Neo4jGraphService handle null gracefully
            services.AddSingleton<INeo4jGraphService>(sp =>
            {
                var embeddingProvider = sp.GetService<IEmbeddingProvider>();
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Neo4jGraphService>>();
                return new Neo4jGraphService(null, embeddingProvider, logger);
            });
            return services;
        }

        // Register Neo4j graph store from Core
        services.AddNeo4jGraphStore(options =>
        {
            options.Uri = connectionString;
            options.Username = neo4jSection.GetValue<string>("Username") ?? "neo4j";
            options.Password = neo4jSection.GetValue<string>("Password") ?? string.Empty;
            options.Database = neo4jSection.GetValue<string>("Database");
            options.CreateIndexesOnStartup = neo4jSection.GetValue<bool>("CreateIndexes", true);
            options.NodeLabelPrefix = neo4jSection.GetValue<string>("NodeLabelPrefix") ?? "FluxIndex_";
            options.Encrypted = neo4jSection.GetValue<bool>("Encrypted", false);
            options.ConnectionTimeoutSeconds = neo4jSection.GetValue<int>("ConnectionTimeoutSeconds", 30);
            options.MaxConnectionPoolSize = neo4jSection.GetValue<int>("MaxConnectionPoolSize", 100);
        });

        // Register Stack-level Neo4j service
        services.AddSingleton<INeo4jGraphService>(sp =>
        {
            var graphStore = sp.GetService<IGraphStore>();
            var embeddingProvider = sp.GetService<IEmbeddingProvider>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Neo4jGraphService>>();
            return new Neo4jGraphService(graphStore, embeddingProvider, logger);
        });

        return services;
    }

    /// <summary>
    /// Adds FileVault services for file tracking and memorization.
    /// Uses Extensions.FileVault's file system-based storage (.vault directory).
    /// </summary>
    public static IServiceCollection AddFileVault(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("FileVault");
        var vaultBasePath = section.GetValue<string>("BasePath") ?? "./data/.vault";

        // Register FileVault with FluxIndex integration
        // This provides: IVault, IVaultStorageService, IFileWatcherService, IVaultQueueService
        // Adapters: FileFluxExtractor, FileFluxChunker for document processing
        services.AddFileVaultWithFluxIndex(options =>
        {
            options.VaultBasePath = vaultBasePath;
            options.EnableBackgroundProcessing = section.GetValue<bool>("EnableBackgroundProcessing", true);
            options.MaxConcurrentProcessing = section.GetValue<int>("MaxConcurrentProcessing", 4);
            options.EnableRealTimeWatch = section.GetValue<bool>("EnableRealTimeWatch", true);
            options.VersionRetentionCount = section.GetValue<int>("VersionRetentionCount", 5);
        });

        // Register the background queue processing service from Extensions.FileVault
        services.AddHostedService<FluxIndex.Extensions.FileVault.Services.VaultBackgroundService>();

        return services;
    }
}
