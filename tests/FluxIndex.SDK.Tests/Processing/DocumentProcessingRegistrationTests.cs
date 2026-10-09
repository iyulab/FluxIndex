using FileFlux;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Integrations.FileFlux;
using FluxIndex.Integrations.FileFlux.Processing;
using FluxIndex.Integrations.FluxImprover;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// <c>AddDocumentProcessingPipeline()</c> uses the services the consumer registered and nothing else. A stage the
/// options ask for without its service fails before the document is read — it used to run against a placeholder and
/// hand back chunks without the context or QA pairs that were asked for.
/// </summary>
public sealed class DocumentProcessingRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFileFlux();
        configure?.Invoke(services);
        services.AddDocumentProcessingPipeline();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Pipeline_ResolvesWithoutAnyOptionalService()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<DocumentProcessingPipeline>());
        Assert.Null(provider.GetService<IContextualEnrichmentService>());
        Assert.Null(provider.GetService<IQAGenerationService>());
    }

    [Fact]
    public void Registration_DoesNotShadow_AConsumersRealService()
    {
        var real = Substitute.For<IContextualEnrichmentService>();
        using var provider = BuildProvider(services => services.AddSingleton(real));

        Assert.Same(real, provider.GetRequiredService<IContextualEnrichmentService>());
    }

    [Theory]
    [InlineData(nameof(ContentProcessingOptions.EnableContextualEnrichment))]
    [InlineData(nameof(ContentProcessingOptions.EnableQAGeneration))]
    [InlineData(nameof(ContentProcessingOptions.GenerateEmbeddings))]
    public async Task RequestedStage_WithoutItsService_FailsBeforeProcessing(string option)
    {
        using var provider = BuildProvider();
        var pipeline = provider.GetRequiredService<DocumentProcessingPipeline>();
        var options = new ContentProcessingOptions
        {
            GenerateEmbeddings = option == nameof(ContentProcessingOptions.GenerateEmbeddings),
            EnableContextualEnrichment = option == nameof(ContentProcessingOptions.EnableContextualEnrichment),
            EnableQAGeneration = option == nameof(ContentProcessingOptions.EnableQAGeneration),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ProcessFromContentAsync("Some text to chunk.", options, TestContext.Current.CancellationToken));

        Assert.Contains(option, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestedStage_WithoutItsService_FailsBeforeTheFileIsRead()
    {
        using var provider = BuildProvider();
        var pipeline = provider.GetRequiredService<DocumentProcessingPipeline>();
        var options = new DocumentProcessingOptions { GenerateEmbeddings = false, EnableMetadataEnrichment = true };

        // The file does not exist: reaching extraction would throw FileNotFoundException instead.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ProcessAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt"), options, TestContext.Current.CancellationToken));

        Assert.Contains(nameof(DocumentProcessingOptions.EnableMetadataEnrichment), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultOptions_ProcessWithoutOptionalServices_AndLeaveChunksWithoutVectors()
    {
        using var provider = BuildProvider();
        var pipeline = provider.GetRequiredService<DocumentProcessingPipeline>();

        // GenerateEmbeddings is unset: embed when an embedder exists, otherwise keyword-only.
        var result = await pipeline.ProcessFromContentAsync(
            "First paragraph of the document.\n\nSecond paragraph of the document.",
            new ContentProcessingOptions(),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Chunks);
        Assert.All(result.Chunks, chunk => Assert.Null(chunk.Embedding));
        Assert.Empty(result.QAPairs);
    }

    [Fact]
    public async Task QAGeneration_RunsThroughFluxImprover_WhenItIsRegistered()
    {
        var completion = Substitute.For<ITextCompletionService>();
        completion.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns("""{"qa_pairs":[{"question":"What does the document describe?","answer":"Two paragraphs of the document."}]}""");
        completion.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions?>(), Arg.Any<CancellationToken>())
            .Returns("""{"qa_pairs":[{"question":"What does the document describe?","answer":"Two paragraphs of the document."}]}""");

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(completion);
            services.AddFluxIndexFluxImprover();
        });
        using var scope = provider.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<DocumentProcessingPipeline>();

        var result = await pipeline.ProcessFromContentAsync(
            "First paragraph of the document.\n\nSecond paragraph of the document.",
            new ContentProcessingOptions { GenerateEmbeddings = false, EnableQAGeneration = true, MaxQAPairsPerChunk = 1 },
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.QAPairs);
        Assert.All(result.QAPairs, pair => Assert.Equal("What does the document describe?", pair.Question));
    }
}
