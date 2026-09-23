using AwesomeAssertions;
using Flux.Abstractions;
using FluxIndex.Core.Application.Services.Base;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// <see cref="TextCompletionServiceBase.CompleteJsonAsync"/> rebuilds the caller's options for the JSON call — every
/// caller setting that is not about the JSON mode itself must survive the rebuild.
/// </summary>
public class TextCompletionServiceBaseOptionsTests
{
    private sealed class RecordingService : TextCompletionServiceBase
    {
        public TextCompletionOptions? Seen { get; private set; }

        protected override Task<string> CompleteCoreAsync(string prompt, TextCompletionOptions options, CancellationToken cancellationToken)
        {
            Seen = options;
            return Task.FromResult("{}");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompleteJsonAsync_ForwardsThrowOnTruncation(bool value)
    {
        var service = new RecordingService();

        await service.CompleteJsonAsync("prompt", new TextCompletionOptions { ThrowOnTruncation = value });

        service.Seen!.ThrowOnTruncation.Should().Be(value);
    }

    [Fact]
    public async Task CompleteAsync_PassesTheCallersOptionsThrough()
    {
        var service = new RecordingService();
        var options = new TextCompletionOptions { ThrowOnTruncation = true, MaxTokens = 42 };

        await service.CompleteAsync("prompt", options);

        service.Seen.Should().BeSameAs(options);
    }
}
