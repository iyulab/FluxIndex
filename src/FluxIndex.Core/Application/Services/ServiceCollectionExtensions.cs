using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services.Enrichment;
using FluxIndex.Core.Application.Services.Quantization;
using FluxIndex.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 메타데이터 증강 서비스 등록 확장 메서드
/// </summary>
public static class MetadataAugmentationServiceExtensions
{
    /// <summary>
    /// Contextual Header 생성 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddContextualHeaderGenerator(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ContextualHeaderOptions>(_ => { });
        }

        // 생성기 등록
        services.AddScoped<IContextualHeaderGenerator, HybridContextualHeaderGenerator>();

        return services;
    }

    /// <summary>
    /// 메타데이터 증강 전체 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddMetadataAugmentation(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? configureOptions = null)
    {
        services.AddContextualHeaderGenerator(configureOptions);

        return services;
    }

    /// <summary>
    /// 청크 분류 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddChunkClassification(
        this IServiceCollection services,
        Action<ClassificationOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ClassificationOptions>(_ => { });
        }

        // 검증 서비스 등록
        services.AddScoped<IClassificationValidationService, ClassificationValidationService>();

        // 분류 서비스 등록
        services.AddScoped<IChunkClassificationService, LlmChunkClassificationService>();

        return services;
    }

    /// <summary>
    /// 전체 증강 서비스 등록 (Header + Classification)
    /// </summary>
    public static IServiceCollection AddFullAugmentation(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? headerOptions = null,
        Action<ClassificationOptions>? classificationOptions = null)
    {
        services.AddContextualHeaderGenerator(headerOptions);
        services.AddChunkClassification(classificationOptions);

        return services;
    }

    /// <summary>
    /// 토큰 예산 기반 검색 서비스 등록
    /// </summary>
    public static IServiceCollection AddTokenAwareSearch(this IServiceCollection services)
    {
        services.AddSingleton<ITokenCounter, SimpleTokenCounter>();
        services.AddScoped<IQueryAnalysisService, QueryAnalysisService>();
        services.AddScoped<ITokenAwareSearchService, TokenAwareSearchService>();

        return services;
    }

    /// <summary>
    /// 그래프 탐색 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddGraphTraversal(this IServiceCollection services)
    {
        services.AddScoped<IGraphTraversalService, GraphTraversalService>();

        return services;
    }

    /// <summary>
    /// Dynamic Alpha Tuning (DAT) 서비스 등록.
    /// 쿼리 유형에 따라 최적의 융합 가중치를 자동 결정합니다.
    /// 연구 결과 6.6% 검색 품질 향상이 확인되었습니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddDynamicAlphaTuning(this IServiceCollection services)
    {
        // QueryComplexityAnalyzer 등록 (없으면)
        services.TryAddScoped<IQueryComplexityAnalyzer, QueryComplexityAnalyzer>();

        // DynamicFusionService 등록
        services.TryAddScoped<IDynamicFusionService, DynamicFusionService>();

        return services;
    }

    /// <summary>
    /// 쿼리 복잡도 분석기 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddQueryComplexityAnalyzer(this IServiceCollection services)
    {
        services.TryAddScoped<IQueryComplexityAnalyzer, QueryComplexityAnalyzer>();

        return services;
    }

    /// <summary>
    /// 벡터 양자화 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">양자화 옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddVectorQuantization(
        this IServiceCollection services,
        Action<QuantizationOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<QuantizationOptions>(_ => { });
        }

        // 양자화 타입에 따라 적절한 구현체 등록
        services.AddSingleton<IVectorQuantizer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QuantizationOptions>>();
            var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();

            return options.Value.Type switch
            {
                QuantizationType.Binary => new BinaryQuantizer(
                    options,
                    loggerFactory.CreateLogger<BinaryQuantizer>()),

                QuantizationType.ProductQuantization or
                QuantizationType.OptimizedProductQuantization => new ProductQuantizer(
                    options,
                    loggerFactory.CreateLogger<ProductQuantizer>()),

                // 기본값은 Scalar Quantization
                _ => new ScalarQuantizer(
                    options,
                    loggerFactory.CreateLogger<ScalarQuantizer>())
            };
        });

        return services;
    }

    /// <summary>
    /// Scalar Quantization 서비스 등록 (Int8 기본)
    /// </summary>
    public static IServiceCollection AddScalarQuantization(
        this IServiceCollection services,
        int dimension = 1536,
        QuantizationType type = QuantizationType.ScalarInt8)
    {
        return services.AddVectorQuantization(options =>
        {
            options.Dimension = dimension;
            options.Type = type;
        });
    }

    /// <summary>
    /// Product Quantization 서비스 등록
    /// </summary>
    public static IServiceCollection AddProductQuantization(
        this IServiceCollection services,
        int dimension = 1536,
        int numSubvectors = 8,
        int codebookSize = 256)
    {
        return services.AddVectorQuantization(options =>
        {
            options.Dimension = dimension;
            options.Type = QuantizationType.ProductQuantization;
            options.NumSubvectors = numSubvectors;
            options.CodebookSize = codebookSize;
        });
    }

    /// <summary>
    /// Binary Quantization 서비스 등록 (최대 압축)
    /// </summary>
    public static IServiceCollection AddBinaryQuantization(
        this IServiceCollection services,
        int dimension = 1536)
    {
        return services.AddVectorQuantization(options =>
        {
            options.Dimension = dimension;
            options.Type = QuantizationType.Binary;
        });
    }

    /// <summary>
    /// 이미지 추출 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddImageExtraction(this IServiceCollection services)
    {
        services.TryAddSingleton<IImageExtractionService, ImageExtractionService>();
        return services;
    }

    /// <summary>
    /// 규칙 기반 메타데이터 증강 서비스 등록.
    /// ChunkMetadata, ChunkQuality, ChunkRelationship를 휴리스틱 규칙으로 생성합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddMetadataEnrichment(this IServiceCollection services)
    {
        services.TryAddScoped<IMetadataEnrichmentService, RuleBasedMetadataEnrichmentService>();
        return services;
    }

    /// <summary>
    /// FluxIndex Core 전체 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="headerOptions">Contextual Header 옵션</param>
    /// <param name="classificationOptions">분류 옵션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFluxIndexCore(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? headerOptions = null,
        Action<ClassificationOptions>? classificationOptions = null)
    {
        services.AddFullAugmentation(headerOptions, classificationOptions);
        services.AddTokenAwareSearch();
        services.AddGraphTraversal();
        services.AddImageExtraction();
        services.AddMetadataEnrichment(); // Rule-based ChunkMetadata/Quality enrichment
        services.AddDynamicAlphaTuning(); // DAT for query-adaptive fusion weights

        return services;
    }

    /// <summary>
    /// Quantized Vector Store Decorator 등록.
    /// 기존 IVectorStore 구현체를 래핑하여 양자화 기능을 추가합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">데코레이터 옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddQuantizedVectorStoreDecorator(
        this IServiceCollection services,
        Action<QuantizedVectorStoreOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<QuantizedVectorStoreOptions>(_ => { });
        }

        // 기존 IVectorStore 등록을 찾아서 데코레이터로 래핑
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IVectorStore));
        if (descriptor != null)
        {
            services.Remove(descriptor);

            services.Add(new ServiceDescriptor(
                typeof(IVectorStore),
                sp =>
                {
                    // 원본 구현체 생성
                    IVectorStore innerStore;
                    if (descriptor.ImplementationFactory != null)
                    {
                        innerStore = (IVectorStore)descriptor.ImplementationFactory(sp);
                    }
                    else if (descriptor.ImplementationInstance != null)
                    {
                        innerStore = (IVectorStore)descriptor.ImplementationInstance;
                    }
                    else if (descriptor.ImplementationType != null)
                    {
                        innerStore = (IVectorStore)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
                    }
                    else
                    {
                        throw new InvalidOperationException("Cannot resolve inner IVectorStore implementation.");
                    }

                    var quantizer = sp.GetRequiredService<IVectorQuantizer>();
                    var logger = sp.GetRequiredService<ILogger<QuantizedVectorStoreDecorator>>();
                    var options = sp.GetService<Microsoft.Extensions.Options.IOptions<QuantizedVectorStoreOptions>>()?.Value;
                    return new QuantizedVectorStoreDecorator(innerStore, quantizer, logger, options);
                },
                descriptor.Lifetime));
        }

        // IQuantizedVectorStore 인터페이스도 동일한 인스턴스로 등록
        services.AddScoped<IQuantizedVectorStore>(sp =>
        {
            var store = sp.GetRequiredService<IVectorStore>();
            if (store is IQuantizedVectorStore quantizedStore)
            {
                return quantizedStore;
            }
            throw new InvalidOperationException(
                "IVectorStore is not decorated with QuantizedVectorStoreDecorator. " +
                "Ensure AddQuantizedVectorStoreDecorator is called after registering the base IVectorStore.");
        });

        return services;
    }

    /// <summary>
    /// Quantized Vector Store Decorator 등록 (간단한 설정).
    /// 먼저 AddVectorQuantization 또는 AddScalarQuantization 등을 호출해야 합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="autoQuantize">저장 시 자동 양자화 여부</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddQuantizedVectorStoreDecorator(
        this IServiceCollection services,
        bool autoQuantize = true)
    {
        return services.AddQuantizedVectorStoreDecorator(options =>
        {
            options.AutoQuantizeOnStore = autoQuantize;
        });
    }

    /// <summary>
    /// 벡터 양자화 마이그레이션 서비스 등록.
    /// 기존 벡터를 양자화 형식으로 일괄 변환하는 서비스입니다.
    /// IVectorStore와 IVectorQuantizer가 먼저 등록되어 있어야 합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddVectorQuantizationMigration(
        this IServiceCollection services)
    {
        services.AddScoped<VectorQuantizationMigrationService>();
        return services;
    }

    /// <summary>
    /// Contextual Embedding Service registration.
    /// Implements Anthropic's Contextual Retrieval approach - prepends LLM-generated context
    /// to chunks before embedding, improving retrieval accuracy by up to 67%.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddContextualEmbedding(
        this IServiceCollection services,
        Action<ContextualEmbeddingOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ContextualEmbeddingOptions>(_ => { });
        }

        // Register contextual header generator (required dependency)
        services.TryAddScoped<IContextualHeaderGenerator, HybridContextualHeaderGenerator>();

        // Register contextual embedding service
        services.TryAddScoped<IContextualEmbeddingService, ContextualEmbeddingService>();

        return services;
    }

    /// <summary>
    /// Contextual Embedding Service with default options.
    /// Includes contextual header generation and embedding pipeline.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="llmThreshold">LLM usage threshold (0.0-1.0, default 0.7)</param>
    /// <param name="generateDualEmbeddings">Whether to generate both contextual and standard embeddings</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddContextualEmbedding(
        this IServiceCollection services,
        double llmThreshold = 0.7,
        bool generateDualEmbeddings = false)
    {
        return services.AddContextualEmbedding(options =>
        {
            options.LlmThreshold = llmThreshold;
            options.GenerateDualEmbeddings = generateDualEmbeddings;
        });
    }

    /// <summary>
    /// Advanced Entity Extraction Service registration.
    /// Foundation for GraphRAG - extracts named entities with type classification,
    /// confidence scoring, position tracking, and relation extraction.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddEntityExtraction(
        this IServiceCollection services,
        Action<EntityExtractionOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<EntityExtractionOptions>(_ => { });
        }

        // Register entity extraction service
        services.TryAddScoped<IAdvancedEntityExtractionService, EntityExtractionService>();

        return services;
    }

    /// <summary>
    /// Advanced Entity Extraction Service with default options.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="useLlm">Whether to use LLM for complex entity extraction</param>
    /// <param name="minConfidence">Minimum confidence threshold for entity inclusion</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddEntityExtraction(
        this IServiceCollection services,
        bool useLlm = true,
        double minConfidence = 0.5)
    {
        return services.AddEntityExtraction(options =>
        {
            options.UseLlm = useLlm;
            options.MinConfidence = minConfidence;
        });
    }

    /// <summary>
    /// Leiden Community Detection Service registration.
    /// Implements hierarchical community detection using the Leiden algorithm
    /// for GraphRAG global search support.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddLeidenCommunityDetection(
        this IServiceCollection services,
        Action<LeidenOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<LeidenOptions>(_ => { });
        }

        // Register Leiden community service
        services.TryAddScoped<ILeidenCommunityService, LeidenCommunityService>();

        return services;
    }

    /// <summary>
    /// Leiden Community Detection Service with default options.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="resolution">Resolution parameter for modularity (higher = more communities)</param>
    /// <param name="maxHierarchyLevels">Maximum hierarchy levels to generate</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddLeidenCommunityDetection(
        this IServiceCollection services,
        double resolution = 1.0,
        int maxHierarchyLevels = 3)
    {
        return services.AddLeidenCommunityDetection(options =>
        {
            options.Resolution = resolution;
            options.MaxHierarchyLevels = maxHierarchyLevels;
        });
    }

    /// <summary>
    /// GraphRAG Core Services registration.
    /// Includes Entity Extraction and Leiden Community Detection for GraphRAG support.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="entityOptions">Entity extraction options</param>
    /// <param name="leidenOptions">Leiden algorithm options</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddGraphRAGCore(
        this IServiceCollection services,
        Action<EntityExtractionOptions>? entityOptions = null,
        Action<LeidenOptions>? leidenOptions = null)
    {
        services.AddEntityExtraction(entityOptions);
        services.AddLeidenCommunityDetection(leidenOptions);

        return services;
    }

    /// <summary>
    /// Listwise Reranker Service registration.
    /// Implements advanced listwise reranking with sliding window, tournament,
    /// attention-based, and hybrid methods for optimal document ordering.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddListwiseReranking(
        this IServiceCollection services,
        Action<ListwiseRerankOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ListwiseRerankOptions>(_ => { });
        }

        // Register listwise reranker
        services.TryAddScoped<IListwiseReranker, Reranking.ListwiseReranker>();

        return services;
    }

    /// <summary>
    /// Listwise Reranker Service with default options.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="method">Default listwise method</param>
    /// <param name="topN">Number of top results to return</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddListwiseReranking(
        this IServiceCollection services,
        ListwiseMethod method = ListwiseMethod.SlidingWindow,
        int topN = 10)
    {
        return services.AddListwiseReranking(options =>
        {
            options.Method = method;
            options.TopN = topN;
        });
    }

    /// <summary>
    /// Iterative Retrieval Service registration.
    /// Implements advanced iterative retrieval patterns including IRCOT, Self-Ask,
    /// Multi-Hop, and Agentic retrieval for complex query handling.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddIterativeRetrieval(
        this IServiceCollection services,
        Action<IterativeRetrievalOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<IterativeRetrievalOptions>(_ => { });
        }

        // Register iterative retrieval service
        services.TryAddScoped<IIterativeRetrievalService, IterativeRetrievalService>();

        return services;
    }

    /// <summary>
    /// Iterative Retrieval Service with default options.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="maxIterations">Maximum retrieval iterations</param>
    /// <param name="maxDocsPerIteration">Maximum documents per iteration</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddIterativeRetrieval(
        this IServiceCollection services,
        int maxIterations = 5,
        int maxDocsPerIteration = 10)
    {
        return services.AddIterativeRetrieval(options =>
        {
            options.MaxIterations = maxIterations;
            options.MaxDocsPerIteration = maxDocsPerIteration;
        });
    }

    /// <summary>
    /// Advanced RAG Services registration.
    /// Includes GraphRAG core services, advanced reranking, and iterative retrieval.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddAdvancedRAG(this IServiceCollection services)
    {
        services.AddGraphRAGCore();
        services.AddListwiseReranking(configureOptions: null);
        services.AddIterativeRetrieval(configureOptions: null);
        services.AddEntityGraphService();

        return services;
    }

    /// <summary>
    /// Entity Graph Service registration for entity-centric indexing and retrieval.
    /// Provides GraphRAG capabilities through entity-based search and traversal.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddEntityGraphService(this IServiceCollection services)
    {
        services.TryAddScoped<IEntityGraphService, Graph.EntityGraphService>();
        return services;
    }

    /// <summary>
    /// Hierarchical Summarization Service registration.
    /// Implements map-reduce summarization at community level with caching
    /// and global search support for GraphRAG.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddHierarchicalSummarization(
        this IServiceCollection services,
        Action<HierarchicalSummarizationOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<HierarchicalSummarizationOptions>(_ => { });
        }

        // Register hierarchical summarization service
        services.TryAddScoped<IHierarchicalSummarizationService, Graph.HierarchicalSummarizationService>();

        return services;
    }

    /// <summary>
    /// Hierarchical Summarization Service with default options.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="parallelGeneration">Enable parallel summary generation</param>
    /// <param name="enableCaching">Enable summary caching</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddHierarchicalSummarization(
        this IServiceCollection services,
        bool parallelGeneration = true,
        bool enableCaching = true)
    {
        return services.AddHierarchicalSummarization(options =>
        {
            options.ParallelGeneration = parallelGeneration;
            options.EnableCaching = enableCaching;
        });
    }

    /// <summary>
    /// Full GraphRAG Services registration.
    /// Includes Entity Extraction, Leiden Community Detection, Entity Graph,
    /// and Hierarchical Summarization for complete GraphRAG support.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddFullGraphRAG(this IServiceCollection services)
    {
        services.AddGraphRAGCore();
        services.AddEntityGraphService();
        services.AddHierarchicalSummarization(configureOptions: null);
        services.AddGraphRAGService();

        return services;
    }

    /// <summary>
    /// GraphRAG Pipeline Service registration.
    /// Orchestrates entity graph, community detection, and hierarchical summarization
    /// for comprehensive retrieval-augmented generation with local and global search.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddGraphRAGService(this IServiceCollection services)
    {
        services.TryAddScoped<IGraphRAGService, Graph.GraphRAGService>();
        return services;
    }

    /// <summary>
    /// Learning-based Fusion Service registration.
    /// Implements machine learning approach to predict optimal fusion weights
    /// based on query characteristics and historical feedback.
    /// Supports online learning for continuous improvement.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddLearningBasedFusion(this IServiceCollection services)
    {
        services.TryAddSingleton<ILearningBasedFusionService, Fusion.LearningBasedFusionService>();
        return services;
    }

    /// <summary>
    /// Advanced Hybrid Search Services registration.
    /// Includes Dynamic Alpha Tuning, Learning-based Fusion, and Query Complexity Analyzer
    /// for intelligent query-adaptive search optimization.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddAdvancedHybridSearch(this IServiceCollection services)
    {
        services.AddDynamicAlphaTuning();
        services.AddLearningBasedFusion();
        return services;
    }

    /// <summary>
    /// Retrieval Verification Service registration.
    /// Implements real-time validation of retrieved documents with document grading,
    /// hallucination detection, factual grounding, and confidence-based filtering.
    /// Supports CRAG (Corrective RAG) patterns for improved RAG reliability.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddRetrievalVerification(
        this IServiceCollection services,
        Action<RetrievalVerificationServiceOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<RetrievalVerificationServiceOptions>(_ => { });
        }

        // Register retrieval verification service
        services.TryAddScoped<Interfaces.IRetrievalVerificationService, RetrievalVerificationService>();

        return services;
    }

    /// <summary>
    /// Retrieval Verification Service with default options.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="alwaysCheckHallucination">Whether to always check for hallucination risks</param>
    /// <param name="useLlmForGrading">Whether to use LLM for document grading explanations</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddRetrievalVerification(
        this IServiceCollection services,
        bool alwaysCheckHallucination = false,
        bool useLlmForGrading = false)
    {
        return services.AddRetrievalVerification(options =>
        {
            options.AlwaysCheckHallucination = alwaysCheckHallucination;
            options.UseLlmForGrading = useLlmForGrading;
        });
    }

    /// <summary>
    /// Self-Correction RAG Services registration.
    /// Includes Retrieval Verification for comprehensive self-correcting RAG support.
    /// Foundation for CRAG, Self-RAG, and Agentic RAG patterns.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddSelfCorrectionRAG(this IServiceCollection services)
    {
        services.AddRetrievalVerification(configureOptions: null);
        return services;
    }

    /// <summary>
    /// Registers Self-RAG (Self-Reflective Retrieval Augmented Generation) services.
    /// Provides iterative search with quality assessment and query refinement capabilities.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Optional configuration for Self-RAG behavior</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddSelfRAGService(
        this IServiceCollection services,
        Action<SelfRAGServiceOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<SelfRAGServiceOptions>(_ => { });
        }

        services.TryAddScoped<Interfaces.ISelfRAGService, SelfRAGService>();
        return services;
    }

    /// <summary>
    /// Adds the Corrective RAG (CRAG) service for retrieval correction.
    /// Evaluates retrieved documents and performs corrective actions based on relevance grading.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Optional configuration for Corrective RAG behavior</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddCorrectiveRAGService(
        this IServiceCollection services,
        Action<CorrectiveRAGServiceOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<CorrectiveRAGServiceOptions>(_ => { });
        }

        services.TryAddScoped<Interfaces.ICorrectiveRAGService, CorrectiveRAGService>();
        return services;
    }

    /// <summary>
    /// Agentic Retrieval Router registration.
    /// Intelligent query routing to optimal retrieval strategies based on query analysis.
    /// Supports multiple retrieval backends: HybridSearch, SelfRAG, CorrectiveRAG, SmallToBig, etc.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    /// <remarks>
    /// Prerequisites: At minimum, IHybridSearchService must be registered.
    /// Optional services (ISelfRAGService, ICorrectiveRAGService, ISmallToBigRetriever,
    /// IIterativeRetrievalService) will be resolved if available.
    /// </remarks>
    public static IServiceCollection AddAgenticRetrievalRouter(
        this IServiceCollection services)
    {
        services.TryAddScoped<Interfaces.IAgenticRetrievalRouter, AgenticRetrievalRouter>();
        return services;
    }

    /// <summary>
    /// Late Chunking Embedding Service registration.
    /// Implements Jina AI's Late Chunking approach - generates embeddings for the full
    /// document first, then derives chunk embeddings preserving more contextual information.
    /// Research shows 2.7% - 3.6% average retrieval improvement over standard chunking.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Options configuration action</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddLateChunking(
        this IServiceCollection services,
        Action<LateChunkingOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<LateChunkingOptions>(_ => { });
        }

        // Register late chunking service
        services.TryAddScoped<ILateChunkingEmbeddingService, LateChunkingEmbeddingService>();

        return services;
    }

    /// <summary>
    /// Late Chunking Embedding Service with default options.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="contextMode">Context integration mode</param>
    /// <param name="documentContextWeight">Weight for document context in weighted combination (0.0-1.0)</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddLateChunking(
        this IServiceCollection services,
        ContextIntegrationMode contextMode = ContextIntegrationMode.SurroundingContext,
        double documentContextWeight = 0.3)
    {
        return services.AddLateChunking(options =>
        {
            options.ContextIntegrationMode = contextMode;
            options.DocumentContextWeight = documentContextWeight;
        });
    }

    /// <summary>
    /// Storage Orchestrator registration.
    /// Automatically resolves the best storage provider for each capability
    /// (Vector, Graph, RDB, SemanticCache) based on registered providers.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection</returns>
    /// <remarks>
    /// Priority rules:
    /// 1. Specialized providers take priority over general-purpose providers
    /// 2. When multiple specialized providers exist, the last registered one wins
    /// 3. General-purpose providers fill in for missing capabilities
    /// 
    /// Example usage:
    /// - UseLocalStorage() registers SQLite as general-purpose (Vector, Graph, RDB, Cache)
    /// - UseQdrant() adds Qdrant as specialized Vector provider (overrides SQLite's Vector)
    /// - UseNeo4j() adds Neo4j as specialized Graph provider (overrides SQLite's Graph)
    /// </remarks>
    public static IServiceCollection AddStorageOrchestrator(this IServiceCollection services)
    {
        services.TryAddSingleton<IStorageOrchestrator, Storage.StorageOrchestrator>();
        return services;
    }
}
