using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluxIndex.Core.Application.Interfaces;
using Flux.Abstractions;

namespace FluxIndex.Integrations.FileFlux;

/// <summary>
/// Document processing pipeline service collection extensions
/// </summary>
public static class DocumentProcessingExtensions
{
    /// <summary>
    /// Adds document processing pipeline with default (mock) services.
    /// Use this when no LLM provider is configured.
    /// </summary>
    public static IServiceCollection AddDocumentProcessingPipeline(this IServiceCollection services)
    {
        // Register mock services for contextual enrichment and QA generation
        // These return empty results when no LLM is available
        services.AddSingleton<IContextualEnrichmentService, MockContextualEnrichmentService>();
        services.AddSingleton<IQAGenerationService, MockQAGenerationService>();
        services.AddSingleton<ITextCompletionService, MockTextCompletionService>();

        // Register the pipeline
        services.AddScoped<FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline>();

        return services;
    }

    /// <summary>
    /// Adds document processing pipeline with custom service implementations.
    /// </summary>
    public static IServiceCollection AddDocumentProcessingPipeline<TContextual, TQA, TCompletion>(
        this IServiceCollection services)
        where TContextual : class, IContextualEnrichmentService
        where TQA : class, IQAGenerationService
        where TCompletion : class, ITextCompletionService
    {
        services.AddSingleton<IContextualEnrichmentService, TContextual>();
        services.AddSingleton<IQAGenerationService, TQA>();
        services.AddSingleton<ITextCompletionService, TCompletion>();
        services.AddScoped<FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline>();

        return services;
    }

    /// <summary>
    /// Adds document processing pipeline with externally registered services.
    /// Assumes IContextualEnrichmentService, IQAGenerationService, and ITextCompletionService
    /// are already registered. Falls back to mock implementations if services are not found.
    /// </summary>
    public static IServiceCollection AddDocumentProcessingPipelineWithFallback(this IServiceCollection services)
    {
        // Register mock services only if not already registered
        services.TryAddSingleton<IContextualEnrichmentService, MockContextualEnrichmentService>();
        services.TryAddSingleton<IQAGenerationService, MockQAGenerationService>();
        services.TryAddSingleton<ITextCompletionService, MockTextCompletionService>();

        // Register the pipeline
        services.AddScoped<FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline>();

        return services;
    }
}