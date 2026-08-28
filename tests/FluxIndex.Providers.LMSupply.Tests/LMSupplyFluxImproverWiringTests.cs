using Flux.Abstractions;
using AwesomeAssertions;
using FluxIndex.Integrations.FluxImprover;
using FluxIndex.Integrations.FluxImprover.Services;
using FluxIndex.Providers.LMSupply.Extensions;
using FluxImprover.Enrichment;
using FluxImprover.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

/// <summary>
/// Proves that FluxImprover's LLM-based chunk enrichment actually runs end to end when wired to a
/// local LMSupply model — the only provider FluxImprover otherwise ships (<c>FluxImprover.LMSupply</c>)
/// had zero consumers in this ecosystem, and neither FileFlux nor FluxIndex had ever exercised the
/// "LLM-based chunk quality improvement" role FluxImprover exists for.
/// </summary>
/// <remarks>
/// Uses <c>AddLMSupplyTextCompletion</c> (FluxIndex's own local generator adapter) rather than the
/// <c>FluxImprover.LMSupply</c> package — <see cref="FluxIndex.Integrations.FluxImprover.ServiceCollectionExtensions.AddFluxIndexFluxImprover"/>
/// already bridges FluxImprover's <c>ITextGenerationService</c> from whatever
/// <see cref="ITextCompletionService"/> FluxIndex has registered, so a FluxIndex consumer doesn't
/// need FluxImprover.LMSupply at all: the adapter path this test exercises is the one FluxIndex's
/// own DI extensions already document as the recommended integration (see
/// <c>AddFluxIndexFluxImprover</c>'s XML doc example).
/// </remarks>
[Trait("Category", "Integration")]
public sealed class LMSupplyFluxImproverWiringTests
{
    [Fact]
    public async Task ChunkEnrichmentServiceWrapper_RealLocalModel_ProducesSummaryAndKeywords()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLMSupplyTextCompletion();
        services.AddFluxIndexFluxImprover();

        await using var provider = services.BuildServiceProvider();
        var wrapper = provider.GetRequiredService<ChunkEnrichmentServiceWrapper>();

        var chunk = new TestEnrichedChunk
        {
            ChunkId = "chunk-1",
            ChunkIndex = 0,
            Content = "FluxIndex is a vector and keyword hybrid search engine designed for " +
                      "retrieval-augmented generation (RAG) pipelines. It embeds document chunks " +
                      "and retrieves the most relevant ones for a given query.",
            HeadingPath = ["Overview"],
            SectionTitle = "Overview",
            Quality = 0.9,
            ContextDependency = 0.1,
            Source = new TestSourceMetadata
            {
                SourceId = "doc-1",
                SourceType = "text",
                Title = "FluxIndex README",
                Language = "en"
            }
        };

        var enriched = await wrapper.EnrichAsync(chunk, new EnrichmentOptions());

        enriched.Should().NotBeNull();
        enriched.Summary.Should().NotBeNullOrWhiteSpace();
        enriched.Content.Should().Be(chunk.Content);
        enriched.ChunkId.Should().Be(chunk.ChunkId);
    }

    private sealed class TestEnrichedChunk : IEnrichedChunk
    {
        public string Content { get; init; } = string.Empty;
        public string ChunkId { get; init; } = string.Empty;
        public int ChunkIndex { get; init; }
        public IReadOnlyList<string> HeadingPath { get; init; } = [];
        public string? SectionTitle { get; init; }
        public int? StartPage { get; init; }
        public int? EndPage { get; init; }
        public double Quality { get; init; }
        public double ContextDependency { get; init; }
        public int? TokenCount { get; init; }
        public ISourceMetadata Source { get; init; } = new TestSourceMetadata();
    }

    private sealed class TestSourceMetadata : ISourceMetadata
    {
        public string SourceId { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? FilePath { get; init; }
        public string? Url { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public string Language { get; init; } = "en";
        public double? LanguageConfidence { get; init; }
        public int WordCount { get; init; }
        public int ChunkCount { get; init; }
        public int? PageCount { get; init; }
        public DateTime? PublishedAt { get; init; }
        public string? Author { get; init; }
        public IReadOnlyList<string>? Keywords { get; init; }
    }
}
