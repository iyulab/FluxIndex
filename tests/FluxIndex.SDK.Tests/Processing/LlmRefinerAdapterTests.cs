using AwesomeAssertions;
using FileFlux.Core;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Integrations.FileFlux;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FluxIndex.SDK.Tests.Processing;

/// <summary>
/// When the completion service fails, <see cref="LlmRefinerAdapter"/> returns the input text unchanged — and with it the
/// input's spans, which still index that text (pages for citations).
/// </summary>
public class LlmRefinerAdapterTests
{
    [Fact]
    public async Task AFailedRefinement_KeepsTheTextAndItsSpans()
    {
        var completion = Substitute.For<ITextCompletionService>();
        completion.CompleteAsync(Arg.Any<string>(), Arg.Any<TextCompletionOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("model unavailable"));
        var adapter = new LlmRefinerAdapter(completion, NullLogger<LlmRefinerAdapter>.Instance);
        SourceSpan[] spans = [new(0, 5) { Page = 1 }, new(7, 12) { Page = 2 }];
        var refined = new RefinedContent { Text = "alpha\n\nbravo", Spans = spans };

        var result = await adapter.RefineAsync(refined, cancellationToken: TestContext.Current.CancellationToken);

        result.Info.LlmWasUsed.Should().BeFalse();
        result.Text.Should().Be("alpha\n\nbravo");
        result.Spans.Should().Equal(spans);
    }
}
