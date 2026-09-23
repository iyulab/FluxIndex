using AwesomeAssertions;
using Flux.Abstractions;
using FluxImprover.Services;
using FluxIndex.Integrations.FluxImprover.Adapters;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// FluxImprover's <see cref="CompletionOptions.ThrowOnTruncation"/> means the same as the port's
/// <see cref="TextCompletionOptions.ThrowOnTruncation"/>. The adapter that runs FluxImprover on a FluxIndex completion
/// service has to pass it through, or a caller that asked to hear about a cut-off answer silently gets the text.
/// </summary>
public class FluxImproverTextCompletionAdapterTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task CompleteAsync_PassesThrowOnTruncation_ToThePort(bool jsonMode, bool throwOnTruncation)
    {
        var port = Substitute.For<ITextCompletionService>();
        TextCompletionOptions? sent = null;
        port.CompleteAsync(Arg.Any<string>(), Arg.Do<TextCompletionOptions>(o => sent = o), Arg.Any<CancellationToken>())
            .Returns("text");
        port.CompleteJsonAsync(Arg.Any<string>(), Arg.Do<TextCompletionOptions>(o => sent = o), Arg.Any<CancellationToken>())
            .Returns("{}");
        var adapter = new TextCompletionServiceAdapter(port);

        await adapter.CompleteAsync(
            "prompt",
            new CompletionOptions { JsonMode = jsonMode, ThrowOnTruncation = throwOnTruncation },
            TestContext.Current.CancellationToken);

        sent!.ThrowOnTruncation.Should().Be(throwOnTruncation);
    }

    [Fact]
    public async Task CompleteAsync_TheSignalFromThePort_ReachesTheFluxImproverCaller()
    {
        var port = Substitute.For<ITextCompletionService>();
        port.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TextCompletionTruncatedException(64));
        var adapter = new TextCompletionServiceAdapter(port);

        var act = () => adapter.CompleteAsync(
            "prompt", new CompletionOptions { MaxTokens = 64, ThrowOnTruncation = true }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TextCompletionTruncatedException>();
    }
}
