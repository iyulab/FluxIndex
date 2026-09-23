using AwesomeAssertions;
using FileFlux;
using FileFlux.Core;
using Flux.Abstractions;
using FluxIndex.Integrations.FileFlux;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// The FileFlux adapter passes FileFlux's sampling settings to the wrapped completion service. Before, it
/// did not implement <c>GenerateAsync(prompt, GenerationSettings, ct)</c>, so FileFlux's text-sized output budget
/// was dropped and every refinement pass ran on a fixed 2000 tokens.
/// </summary>
public class FluxIndexTextCompletionAdapterTests
{
    private readonly ITextCompletionService _service = Substitute.For<ITextCompletionService>();

    private FluxIndexTextCompletionAdapter CreateAdapter() => new(_service, NullLogger<FluxIndexTextCompletionAdapter>.Instance);

    [Fact]
    public async Task Settings_reach_the_completion_service()
    {
        _service.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("ok");

        await CreateAdapter().GenerateAsync("p", new GenerationSettings(Temperature: 0.2, MaxTokens: 6000), TestContext.Current.CancellationToken);

        await _service.Received(1).CompleteAsync("p",
            Arg.Is<TextCompletionOptions?>(o => o!.MaxTokens == 6000 && o.Temperature == 0.2f), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unset_settings_keep_the_previous_defaults()
    {
        _service.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions?>(), Arg.Any<CancellationToken>()).Returns("ok");

        await CreateAdapter().GenerateAsync("p", TestContext.Current.CancellationToken);

        await _service.Received(1).CompleteAsync("p",
            Arg.Is<TextCompletionOptions?>(o => o!.MaxTokens == 2000 && o.Temperature == 0.7f), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MaxContextLength_is_not_declared()
        => CreateAdapter().ProviderInfo.MaxContextLength.Should().Be(0, "the wrapped service may be any model; FileFlux skips its context check for 0");

    [Fact]
    public async Task Refining_a_long_document_asks_for_a_budget_sized_from_the_text()
    {
        var document = string.Join("\n\n", Enumerable.Range(1, 150).Select(i => $"Paragraph {i}: the quarterly report covers revenue, cost and headcount in detail."));
        TextCompletionOptions? sent = null;
        _service.CompleteAsync(Arg.Any<string>(), Arg.Do<TextCompletionOptions?>(o => sent = o), Arg.Any<CancellationToken>())
            .Returns(ci => document);
        var noiseOnly = new LlmRefineOptions
        {
            RestoreSentences = false, CorrectOcrErrors = false, RestructureSections = false, MergeDuplicates = false, RemoveNoise = true,
        };

        await new FileFlux.Infrastructure.LlmRefiner(CreateAdapter()).RefineAsync(
            new RefinedContent { Text = document }, noiseOnly, TestContext.Current.CancellationToken);

        sent!.MaxTokens.Should().BeGreaterThan(2000, "a ~12 KB rewrite does not fit the adapter's old fixed 2000-token budget");
    }
}
