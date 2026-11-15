using AwesomeAssertions;
using FileFlux.Core;
using Flux.Abstractions;
using FluxIndex.Integrations.FileFlux;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// Which <see cref="ILlmRefiner"/> a consumer of <c>AddFileFluxIntegration</c> gets: FileFlux's own, which reads the
/// <c>IDocumentAnalysisService</c> the integration registers over the container's <see cref="ITextCompletionService"/>. (FluxIndex
/// carried a second refiner behind a <c>TryAdd</c> that never took effect, because <c>AddFileFlux()</c> had already registered one;
/// it was removed.)
/// </summary>
public class LlmRefinerWiringTests
{
    [Fact]
    public async Task AddFileFluxIntegration_resolves_FileFluxs_refiner_and_it_reaches_the_container_completion_service()
    {
        var completion = Substitute.For<ITextCompletionService>();
        completion.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>()).Returns("alpha beta");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(completion);
        services.AddFileFluxIntegration();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var refiner = scope.ServiceProvider.GetRequiredService<ILlmRefiner>();

        refiner.RefinerType.Should().Be("LlmRefiner");
        refiner.IsAvailable.Should().BeTrue("the refiner has a completion service behind it");

        // The completion service the container holds is the one that answers a refinement.
        await refiner.RefineAsync(
            new RefinedContent { Text = "alpha\nbeta and a longer line of prose that ends here" },
            new LlmRefineOptions { RestoreSentences = true, RemoveNoise = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = false },
            TestContext.Current.CancellationToken);

        await completion.Received().CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>());
    }
}
