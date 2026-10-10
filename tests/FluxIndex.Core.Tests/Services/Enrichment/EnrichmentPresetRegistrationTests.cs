using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.Enrichment;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Enrichment;

/// <summary>
/// The presets reassigned their lambda's parameter («config = config with { … }»), so the registered configuration was
/// always the plain default: «Basic» still ran entity extraction and LLM summaries, «WithAI» never turned on HyDE or
/// questions, and the caller's configure never reached the pipeline.
/// </summary>
public class EnrichmentPresetRegistrationTests
{
    private static EnrichmentPipelineConfig Registered(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider().GetRequiredService<EnrichmentPipelineConfig>();
    }

    [Fact]
    public void Basic_registers_embeddings_only()
    {
        var options = Registered(s => s.AddDocumentEnrichmentPipelineBasic()).DefaultOptions;

        Assert.True(options.GenerateContentEmbedding);
        Assert.False(options.ExtractEntities);
        Assert.False(options.GenerateContextualSummary);
        Assert.False(options.ExtractKeywords);
        Assert.False(options.GenerateContextualEmbedding);
    }

    [Fact]
    public void WithAI_registers_every_AI_feature_and_then_the_callers_configure()
    {
        var config = Registered(s => s.AddDocumentEnrichmentPipelineWithAI(c => c.MaxEmbeddingBatchSize = 8));

        Assert.True(config.DefaultOptions.GenerateHypotheticalEmbedding);
        Assert.True(config.DefaultOptions.GenerateSummaryEmbedding);
        Assert.Equal(8, config.MaxEmbeddingBatchSize);
    }

    [Fact]
    public void The_plain_registration_applies_configure()
    {
        var config = Registered(s => s.AddDocumentEnrichmentPipeline(c => c.DefaultOptions = new EnrichmentOptions { ExtractKeywords = false }));

        Assert.False(config.DefaultOptions.ExtractKeywords);
    }
}
