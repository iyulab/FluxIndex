using AwesomeAssertions;
using FluxImprover.ContextualRetrieval;
using FluxImprover.Models;
using FluxImprover.Options;
using FluxImprover.Services;
using FluxIndex.Integrations.FluxImprover;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using CoreEnrichment = FluxIndex.Core.Application.Interfaces.IContextualEnrichmentService;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// FluxIndex.Core's string-shaped enrichment port is what FluxFeed and the FileFlux integration call, and before 0.52.0
/// it always passed <c>options: null</c>: a consumer of those pipelines could not set the model's budget, temperature
/// or reasoning for enrichment at all. The options given at registration now reach every call through the port.
/// </summary>
public class ContextualEnrichmentWrapperOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (CoreEnrichment Port, IContextualEnrichmentService Inner) Build(ContextualEnrichmentOptions? options)
    {
        var inner = Substitute.For<IContextualEnrichmentService>();
        inner.EnrichAsync(Arg.Any<Chunk>(), Arg.Any<string>(), Arg.Any<ContextualEnrichmentOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ContextualChunk { SourceId = "doc", Id = ci.Arg<Chunk>().Id, Text = ci.Arg<Chunk>().Content, ContextSummary = "ctx" });
        inner.EnrichBatchAsync(Arg.Any<IEnumerable<Chunk>>(), Arg.Any<string>(), Arg.Any<ContextualEnrichmentOptions?>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<Chunk>>().Select(c => new ContextualChunk { SourceId = "doc", Id = c.Id, Text = c.Content, ContextSummary = "ctx" }).ToList());

        var services = new ServiceCollection();
        services.AddScoped(_ => inner);
        services.AddContextualEnrichmentWrapper(options);
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<CoreEnrichment>(), inner);
    }

    [Fact]
    public async Task OptionsGivenAtRegistration_ReachTheSingleChunkPort()
    {
        var options = new ContextualEnrichmentOptions { MaxTokens = 128, Thinking = ThinkingMode.Auto };
        var (port, inner) = Build(options);

        await port.GenerateContextAsync("chunk text", "full document", 0, 1, Ct);

        await inner.Received(1).EnrichAsync(Arg.Any<Chunk>(), "full document", options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OptionsGivenAtRegistration_ReachTheBatchPort()
    {
        var options = new ContextualEnrichmentOptions { Temperature = 0.2f };
        var (port, inner) = Build(options);

        await port.GenerateContextBatchAsync(["a", "b"], "full document", Ct);

        await inner.Received(1).EnrichBatchAsync(Arg.Any<IEnumerable<Chunk>>(), "full document", options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoOptions_LeavesFluxImproversDefaults()
    {
        var (port, inner) = Build(options: null);

        await port.GenerateContextBatchAsync(["a"], "full document", Ct);

        await inner.Received(1).EnrichBatchAsync(Arg.Any<IEnumerable<Chunk>>(), "full document", null, Arg.Any<CancellationToken>());
    }
}
