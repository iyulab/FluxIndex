using AwesomeAssertions;
using FileFlux;
using FileFlux.Core;
using FluxIndex.Integrations.FileFlux;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using FileFluxChunk = FileFlux.Core.DocumentChunk;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// <c>FileFluxOptions.EnableLlmRefine</c> (off by default) and <c>LlmRefineOptions</c> were read by nothing: the
/// integration handed FileFlux processing options with only chunking set, and FileFlux's own default runs the LLM refine
/// stage — so with a text completion service registered (the integration registers its refiner adapter) every file was
/// refined by an LLM whatever the switch said, with FileFlux's default refine settings. The switch now decides, on both
/// the whole-document and the streaming path, and the configured refine settings are what FileFlux receives.
/// </summary>
public class FileFluxLlmRefineWiringTests
{
    private static readonly FileFluxChunk[] Chunks =
    [
        new() { Content = "Quarterly revenue grew twelve percent.", Location = new SourceLocation { StartChar = 0, EndChar = 38 } },
    ];

    private static (FileFluxIntegration Integration, List<FileFlux.Core.ProcessingOptions> Seen) Create(Action<FileFluxOptions> configure)
    {
        var seen = new List<FileFlux.Core.ProcessingOptions>();
        var processor = Substitute.For<IDocumentProcessor>();
        processor.Result.Returns(new ProcessingResult { Chunks = Chunks });
        processor.ProcessAsync(Arg.Do<FileFlux.Core.ProcessingOptions?>(o => seen.Add(o!)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        processor.ProcessStreamAsync(Arg.Do<FileFlux.Core.ProcessingOptions?>(o => seen.Add(o!)), Arg.Any<CancellationToken>())
            .Returns(_ => Chunks.ToAsyncEnumerable());
        var factory = Substitute.For<IDocumentProcessorFactory>();
        factory.Create(Arg.Any<string>()).Returns(processor);

        var options = new FileFluxOptions();
        configure(options);
        var builder = FluxIndexContext.CreateBuilder().UseInMemoryEmbedding().SuppressStartupMessages();
        var integration = new FileFluxIntegration(
            factory,
            Substitute.For<ILanguageProfileProvider>(),
            builder.Build().Indexer,
            NullLogger<FileFluxIntegration>.Instance,
            Options.Create(options));
        return (integration, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ByDefault_TheLlmRefineStageDoesNotRun(bool streaming)
    {
        var (integration, seen) = Create(o => o.UseStreamingApi = streaming);

        await integration.ProcessAndIndexAsync("report.pdf", cancellationToken: TestContext.Current.CancellationToken);

        seen.Should().ContainSingle().Which.IncludeLlmRefine.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enabled_RunsTheStageWithTheConfiguredSettings(bool streaming)
    {
        var refine = new LlmRefineOptions { RemoveNoise = false, Temperature = 0.1, CustomInstructions = "Keep tables." };
        var (integration, seen) = Create(o =>
        {
            o.UseStreamingApi = streaming;
            o.EnableLlmRefine = true;
            o.LlmRefineOptions = refine;
        });

        await integration.ProcessAndIndexAsync("report.pdf", cancellationToken: TestContext.Current.CancellationToken);

        var passed = seen.Should().ContainSingle().Subject;
        passed.IncludeLlmRefine.Should().BeTrue();
        passed.LlmRefine.Should().BeSameAs(refine);
    }
}
