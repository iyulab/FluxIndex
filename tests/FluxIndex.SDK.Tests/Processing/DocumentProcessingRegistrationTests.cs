using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Integrations.FileFlux;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// <c>AddDocumentProcessingPipeline()</c> ships no-op defaults for the three LLM-backed ports. Those defaults
/// must yield to a real implementation the consumer registered first — before 0.34.0 the no-arg overload used
/// <c>Add</c>, so a FluxImprover-backed <see cref="IContextualEnrichmentService"/> registered earlier was silently
/// replaced by the no-op and enrichment "worked" while producing nothing.
/// </summary>
public sealed class DocumentProcessingRegistrationTests
{
    [Fact]
    public void NoArgOverload_DoesNotShadow_AConsumersRealEnrichmentService()
    {
        var real = Substitute.For<IContextualEnrichmentService>();
        var services = new ServiceCollection();
        services.AddSingleton(real);

        services.AddDocumentProcessingPipeline();

        using var provider = services.BuildServiceProvider();
        Assert.Same(real, provider.GetRequiredService<IContextualEnrichmentService>());
    }

    [Fact]
    public void NoArgOverload_Alone_RegistersTheNoOpDefaults()
    {
        var services = new ServiceCollection();

        services.AddDocumentProcessingPipeline();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<NoOpContextualEnrichmentService>(provider.GetRequiredService<IContextualEnrichmentService>());
        Assert.IsType<NoOpQAGenerationService>(provider.GetRequiredService<IQAGenerationService>());
        Assert.IsType<NoOpTextCompletionService>(provider.GetRequiredService<ITextCompletionService>());
    }

    [Fact]
    public async Task NoOpEnrichment_ReturnsOneEmptyContextPerChunk()
    {
        var noOp = new NoOpContextualEnrichmentService();

        var contexts = await noOp.GenerateContextBatchAsync(["a", "b", "c"], "a b c", TestContext.Current.CancellationToken);

        Assert.Equal(3, contexts.Count);
        Assert.All(contexts, c => Assert.Equal(string.Empty, c));
    }
}
