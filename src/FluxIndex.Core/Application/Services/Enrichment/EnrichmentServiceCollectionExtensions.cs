using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxIndex.Core.Application.Services.Enrichment;

/// <summary>
/// DI extensions for document enrichment services.
/// </summary>
public static class EnrichmentServiceCollectionExtensions
{
    /// <summary>
    /// Adds the document enrichment pipeline to the service collection.
    /// Requires IEmbeddingService to be registered.
    /// Optionally uses ITextCompletionService for LLM-powered enrichment.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddDocumentEnrichmentPipeline(
        this IServiceCollection services,
        Action<EnrichmentPipelineConfig>? configure = null)
    {
        var config = new EnrichmentPipelineConfig();
        configure?.Invoke(config);

        services.TryAddSingleton(config);
        services.TryAddScoped<IDocumentEnrichmentPipeline, DocumentEnrichmentPipeline>();

        // Add entity extraction service if not already registered
        services.TryAddScoped<IAdvancedEntityExtractionService, EntityExtractionService>();

        return services;
    }

    /// <summary>
    /// Adds the document enrichment pipeline with full AI capabilities.
    /// Requires IEmbeddingService and ITextCompletionService to be registered.
    /// </summary>
    public static IServiceCollection AddDocumentEnrichmentPipelineWithAI(
        this IServiceCollection services,
        Action<EnrichmentPipelineConfig>? configure = null)
    {
        return services.AddDocumentEnrichmentPipeline(config =>
        {
            // Enable all AI-powered features in default options
            config = config with
            {
                DefaultOptions = new EnrichmentOptions
                {
                    GenerateContentEmbedding = true,
                    GenerateContextualEmbedding = true,
                    GenerateHypotheticalEmbedding = true,
                    GenerateSummaryEmbedding = true,
                    ExtractEntities = true,
                    ExtractRelationships = true,
                    GenerateContextualSummary = true,
                    ExtractKeywords = true,
                    GenerateHypotheticalQuestions = true
                }
            };
            configure?.Invoke(config);
        });
    }

    /// <summary>
    /// Adds minimal enrichment pipeline (embeddings only, no LLM).
    /// </summary>
    public static IServiceCollection AddDocumentEnrichmentPipelineBasic(
        this IServiceCollection services)
    {
        return services.AddDocumentEnrichmentPipeline(config =>
        {
            config = config with
            {
                DefaultOptions = new EnrichmentOptions
                {
                    GenerateContentEmbedding = true,
                    GenerateContextualEmbedding = false,
                    GenerateHypotheticalEmbedding = false,
                    GenerateSummaryEmbedding = false,
                    ExtractEntities = false,
                    ExtractRelationships = false,
                    GenerateContextualSummary = false,
                    ExtractKeywords = false,
                    GenerateHypotheticalQuestions = false
                }
            };
        });
    }
}
