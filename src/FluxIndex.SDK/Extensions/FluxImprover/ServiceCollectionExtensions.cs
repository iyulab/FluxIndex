using FluxIndex.SDK.Extensions.FluxImprover.Adapters;
using FluxIndex.SDK.Extensions.FluxImprover.Services;
using FluxImprover;
using FluxImprover.ChunkFiltering;
using FluxImprover.ContextualRetrieval;
using FluxImprover.Enrichment;
using FluxImprover.Evaluation;
using FluxImprover.QAGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FluxIndexCompletion = FluxIndex.Core.Application.Interfaces.ITextCompletionService;
using FluxImproverCompletion = FluxImprover.Services.ITextGenerationService;

namespace FluxIndex.SDK.Extensions.FluxImprover;

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
    /// The wrapper is registered as scoped to match FluxImprover's service lifetime.
    /// </remarks>
    public static IServiceCollection AddChunkEnrichmentWrapper(this IServiceCollection services)
    {
        services.AddScoped<ChunkEnrichmentServiceWrapper>(provider =>
        {
            var enrichmentService = provider.GetRequiredService<ChunkEnrichmentService>();
            return new ChunkEnrichmentServiceWrapper(enrichmentService);
        });

        return services;
    }

    /// <summary>
    /// Registers the ContextualEnrichmentServiceWrapper for Anthropic's Contextual Retrieval pattern.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method requires FluxImprover's IContextualEnrichmentService to be already registered.
    /// The wrapper is registered as scoped to match FluxImprover's service lifetime.
    /// </para>
    /// <para>
    /// Contextual Retrieval reduces failed retrievals by 49% (67% with reranking) by prepending
    /// LLM-generated document context to each chunk before embedding.
    /// </para>
    /// <para>
    /// Reference: https://www.anthropic.com/news/contextual-retrieval
    /// </para>
    /// </remarks>
    public static IServiceCollection AddContextualEnrichmentWrapper(this IServiceCollection services)
    {
        services.AddScoped<ContextualEnrichmentServiceWrapper>(provider =>
        {
            var contextualService = provider.GetRequiredService<IContextualEnrichmentService>();
            return new ContextualEnrichmentServiceWrapper(contextualService);
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
    /// The wrapper is registered as scoped to match FluxImprover's service lifetime.
    /// </remarks>
    public static IServiceCollection AddRAGEvaluation(this IServiceCollection services)
    {
        services.AddScoped<RAGEvaluationService>(provider =>
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
    /// The wrapper is registered as scoped to match FluxImprover's service lifetime.
    /// </remarks>
    public static IServiceCollection AddQAGeneration(this IServiceCollection services)
    {
        services.AddScoped<QAGenerationService>(provider =>
        {
            var generatorService = provider.GetRequiredService<QAGeneratorService>();
            var filterService = provider.GetRequiredService<QAFilterService>();
            var pipeline = provider.GetRequiredService<QAPipeline>();
            return new QAGenerationService(generatorService, filterService, pipeline);
        });

        return services;
    }

    /// <summary>
    /// Registers the ChunkFilteringServiceWrapper for LLM-based 3-stage chunk filtering.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This method requires FluxImprover's IChunkFilteringService to be already registered.
    /// The wrapper is registered as scoped to match FluxImprover's service lifetime.
    /// Provides 3-stage LLM evaluation: Initial → Self-Reflection → Critic Validation.
    /// </remarks>
    public static IServiceCollection AddChunkFilteringWrapper(this IServiceCollection services)
    {
        services.AddScoped<ChunkFilteringServiceWrapper>(provider =>
        {
            var filteringService = provider.GetRequiredService<IChunkFilteringService>();
            return new ChunkFilteringServiceWrapper(filteringService);
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
    /// The pipeline is registered as scoped to match FluxImprover's service lifetime.
    /// </remarks>
    public static IServiceCollection AddFluxImproverPipeline(this IServiceCollection services)
    {
        services.AddScoped<FluxImproverPipeline>(provider =>
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
        services.AddScoped<ParallelPipelineExecutor>(provider =>
        {
            var enrichmentService = provider.GetService<ChunkEnrichmentServiceWrapper>();
            var qaService = provider.GetService<QAGenerationService>();
            var evaluationService = provider.GetService<RAGEvaluationService>();
            var logger = provider.GetService<ILogger<ParallelPipelineExecutor>>();
            return new ParallelPipelineExecutor(enrichmentService, qaService, evaluationService, logger);
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
        services.AddScoped<CachedPipelineExecutor>(provider =>
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

    /// <summary>
    /// One-stop registration for FluxImprover integration with FluxIndex.
    /// Requires FluxIndex's ITextCompletionService to be already registered.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is the recommended way to integrate FluxImprover with FluxIndex.
    /// It registers all FluxImprover services (v0.4.1+) and all FluxIndex wrapper services in one call.
    /// </para>
    /// <para>
    /// <b>Prerequisites:</b> FluxIndex's ITextCompletionService must be registered first (e.g., via AddOpenAITextCompletion).
    /// </para>
    /// <para>
    /// <b>Registers:</b>
    /// <list type="bullet">
    /// <item><description>All FluxImprover core services (ChunkEnrichmentService, QAPipeline, Evaluators, etc.)</description></item>
    /// <item><description>ChunkEnrichmentServiceWrapper - LLM-powered chunk enrichment</description></item>
    /// <item><description>QAGenerationService - Q&amp;A pair generation from chunks</description></item>
    /// <item><description>RAGEvaluationService - RAG pipeline quality evaluation</description></item>
    /// <item><description>ChunkFilteringServiceWrapper - 3-stage LLM chunk filtering</description></item>
    /// <item><description>ParallelPipelineExecutor - High-performance parallel processing</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Register FluxIndex services first
    /// services.AddOpenAITextCompletion(options => { options.ApiKey = apiKey; });
    ///
    /// // Then register FluxImprover integration in one call
    /// services.AddFluxIndexFluxImprover();
    ///
    /// // Now inject any service you need
    /// public class MyService(QAGenerationService qa, RAGEvaluationService eval) { }
    /// </code>
    /// </example>
    public static IServiceCollection AddFluxIndexFluxImprover(this IServiceCollection services)
    {
        // Register all FluxImprover core services using v0.4.1+ simplified DI
        services.AddFluxImprover(sp =>
            new TextCompletionServiceAdapter(sp.GetRequiredService<FluxIndexCompletion>()));

        // Register FluxIndex wrapper services
        services.AddChunkEnrichmentWrapper();
        services.AddContextualEnrichmentWrapper();
        services.AddQAGeneration();
        services.AddRAGEvaluation();
        services.AddChunkFilteringWrapper();

        // Register parallel executor
        services.AddParallelPipelineExecutor();

        return services;
    }

    /// <summary>
    /// One-stop registration for FluxImprover integration with FluxIndex, including caching support.
    /// Requires FluxIndex's ITextCompletionService to be already registered.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configureCacheOptions">Optional cache options configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Same as <see cref="AddFluxIndexFluxImprover(IServiceCollection)"/> but also registers CachedPipelineExecutor.
    /// </remarks>
    public static IServiceCollection AddFluxIndexFluxImprover(
        this IServiceCollection services,
        Action<CacheOptions> configureCacheOptions)
    {
        services.AddFluxIndexFluxImprover();
        services.AddCachedPipelineExecutor(configureCacheOptions);
        return services;
    }
}
