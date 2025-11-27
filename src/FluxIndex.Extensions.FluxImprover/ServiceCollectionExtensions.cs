using FluxIndex.Extensions.FluxImprover.Adapters;
using FluxIndex.Extensions.FluxImprover.Services;
using FluxImprover.Enrichment;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using Microsoft.Extensions.DependencyInjection;
using FluxIndexCompletion = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using FluxImproverCompletion = FluxImprover.Abstractions.Services.ITextCompletionService;

namespace FluxIndex.Extensions.FluxImprover;

/// <summary>
/// Extension methods for registering FluxImprover integration services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the FluxImprover text completion adapter that bridges FluxIndex's ITextCompletionService.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method requires FluxIndex's ITextCompletionService to be already registered.
    /// The adapter is registered as a singleton.
    /// </remarks>
    public static IServiceCollection AddFluxImproverTextCompletion(this IServiceCollection services)
    {
        services.AddSingleton<FluxImproverCompletion>(provider =>
        {
            var fluxIndexService = provider.GetRequiredService<FluxIndexCompletion>();
            return new TextCompletionServiceAdapter(fluxIndexService);
        });

        return services;
    }

    /// <summary>
    /// Registers the ChunkEnrichmentServiceWrapper for enriching FluxIndex chunks with LLM-generated summaries and keywords.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method requires FluxImprover's ChunkEnrichmentService to be already registered.
    /// The wrapper is registered as a singleton.
    /// </remarks>
    public static IServiceCollection AddChunkEnrichmentWrapper(this IServiceCollection services)
    {
        services.AddSingleton<ChunkEnrichmentServiceWrapper>(provider =>
        {
            var enrichmentService = provider.GetRequiredService<ChunkEnrichmentService>();
            return new ChunkEnrichmentServiceWrapper(enrichmentService);
        });

        return services;
    }

    /// <summary>
    /// Registers the RAGEvaluationService for comprehensive RAG pipeline evaluation.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method requires FluxImprover's evaluation services to be already registered:
    /// - AnswerabilityEvaluator
    /// - FaithfulnessEvaluator
    /// - RelevancyEvaluator
    /// The wrapper is registered as a singleton.
    /// </remarks>
    public static IServiceCollection AddRAGEvaluation(this IServiceCollection services)
    {
        services.AddSingleton<RAGEvaluationService>(provider =>
        {
            var answerabilityEvaluator = provider.GetRequiredService<AnswerabilityEvaluator>();
            var faithfulnessEvaluator = provider.GetRequiredService<FaithfulnessEvaluator>();
            var relevancyEvaluator = provider.GetRequiredService<RelevancyEvaluator>();
            return new RAGEvaluationService(answerabilityEvaluator, faithfulnessEvaluator, relevancyEvaluator);
        });

        return services;
    }

    /// <summary>
    /// Registers the QAGenerationService for generating Q&amp;A pairs from FluxIndex chunks.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method requires FluxImprover's QA generation services to be already registered:
    /// - QAGeneratorService
    /// - QAFilterService
    /// - QAPipeline
    /// The wrapper is registered as a singleton.
    /// </remarks>
    public static IServiceCollection AddQAGeneration(this IServiceCollection services)
    {
        services.AddSingleton<QAGenerationService>(provider =>
        {
            var generatorService = provider.GetRequiredService<QAGeneratorService>();
            var filterService = provider.GetRequiredService<QAFilterService>();
            var pipeline = provider.GetRequiredService<QAPipeline>();
            return new QAGenerationService(generatorService, filterService, pipeline);
        });

        return services;
    }

    /// <summary>
    /// Registers the FluxImproverPipeline for orchestrating the complete FluxImprover workflow.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method will use available services (ChunkEnrichmentServiceWrapper, QAGenerationService, RAGEvaluationService)
    /// and gracefully handle cases where some services are not registered.
    /// The pipeline is registered as a singleton.
    /// </remarks>
    public static IServiceCollection AddFluxImproverPipeline(this IServiceCollection services)
    {
        services.AddSingleton<FluxImproverPipeline>(provider =>
        {
            var enrichmentService = provider.GetService<ChunkEnrichmentServiceWrapper>();
            var qaService = provider.GetService<QAGenerationService>();
            var evaluationService = provider.GetService<RAGEvaluationService>();
            return new FluxImproverPipeline(enrichmentService, qaService, evaluationService);
        });

        return services;
    }

    /// <summary>
    /// Registers the ParallelPipelineExecutor for high-performance parallel chunk processing.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddParallelPipelineExecutor(this IServiceCollection services)
    {
        services.AddSingleton<ParallelPipelineExecutor>(provider =>
        {
            var enrichmentService = provider.GetService<ChunkEnrichmentServiceWrapper>();
            var qaService = provider.GetService<QAGenerationService>();
            var evaluationService = provider.GetService<RAGEvaluationService>();
            return new ParallelPipelineExecutor(enrichmentService, qaService, evaluationService);
        });

        return services;
    }

    /// <summary>
    /// Registers the CachedPipelineExecutor for cached chunk processing with automatic eviction.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configureOptions">Optional cache options configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCachedPipelineExecutor(
        this IServiceCollection services,
        Action<CacheOptions>? configureOptions = null)
    {
        services.AddSingleton<CachedPipelineExecutor>(provider =>
        {
            var enrichmentService = provider.GetService<ChunkEnrichmentServiceWrapper>();
            var qaService = provider.GetService<QAGenerationService>();
            var evaluationService = provider.GetService<RAGEvaluationService>();

            var cacheOptions = new CacheOptions();
            configureOptions?.Invoke(cacheOptions);

            return new CachedPipelineExecutor(enrichmentService, qaService, evaluationService, cacheOptions);
        });

        return services;
    }

    /// <summary>
    /// Registers all FluxImprover integration services including adapters and pipeline.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method requires FluxIndex's core services to be already registered.
    /// Registers:
    /// - TextCompletionServiceAdapter for ITextCompletionService bridging
    /// - FluxImproverPipeline for workflow orchestration
    /// </remarks>
    public static IServiceCollection AddFluxImproverIntegration(this IServiceCollection services)
    {
        services.AddFluxImproverTextCompletion();
        services.AddFluxImproverPipeline();
        return services;
    }

    /// <summary>
    /// Registers all FluxImprover integration services with full performance optimization support.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configureCacheOptions">Optional cache options configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method registers all available services including:
    /// - TextCompletionServiceAdapter for ITextCompletionService bridging
    /// - FluxImproverPipeline for workflow orchestration
    /// - ParallelPipelineExecutor for high-performance parallel processing
    /// - CachedPipelineExecutor for cached processing with automatic eviction
    /// </remarks>
    public static IServiceCollection AddFluxImproverFullIntegration(
        this IServiceCollection services,
        Action<CacheOptions>? configureCacheOptions = null)
    {
        services.AddFluxImproverTextCompletion();
        services.AddFluxImproverPipeline();
        services.AddParallelPipelineExecutor();
        services.AddCachedPipelineExecutor(configureCacheOptions);
        return services;
    }
}
