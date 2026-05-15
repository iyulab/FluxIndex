using Flux.Abstractions;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Mock implementation of <see cref="ITextCompletionService"/> for testing without a real provider.
/// </summary>
public class MockTextCompletionService : ITextCompletionService
{
    /// <inheritdoc />
    public Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"Mock response for: {prompt[..Math.Min(50, prompt.Length)]}...");
    }

    /// <inheritdoc />
    public Task<string> CompleteJsonAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult("{}");
    }
}
