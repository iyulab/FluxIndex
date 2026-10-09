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
    /// Adds the document processing pipeline. It uses whatever contextual enrichment, QA generation, text completion and
    /// embedding services are registered; a stage whose service is missing must stay off in the options, or processing
    /// throws <see cref="InvalidOperationException"/> before it starts.
    /// </summary>
    public static IServiceCollection AddDocumentProcessingPipeline(this IServiceCollection services)
    {
        services.TryAddScoped<FluxIndex.Integrations.FileFlux.Processing.DocumentProcessingPipeline>();

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

        return services.AddDocumentProcessingPipeline();
    }
}
