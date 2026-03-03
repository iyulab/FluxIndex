using Flux.Abstractions;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// FluxIndex-specific text completion service extending the canonical Flux.Abstractions contract.
/// Adds <see cref="CountTokens"/> for backward compatibility with services
/// that need rough token estimation (e.g., adapters tracking token usage).
/// </summary>
/// <remarks>
/// New code should prefer <see cref="Flux.Abstractions.ITextCompletionService"/> directly.
/// <see cref="CountTokens"/> will be removed in a future version once all callers
/// migrate to <see cref="TokenMeter.Abstractions.ITokenCounter"/>.
/// </remarks>
public interface ITextCompletionService : Flux.Abstractions.ITextCompletionService
{
    /// <summary>
    /// Counts tokens in the given text.
    /// </summary>
    /// <param name="text">Text to count tokens for.</param>
    /// <returns>Number of tokens.</returns>
    [System.Obsolete("Use TokenMeter.Abstractions.ITokenCounter instead. Will be removed in a future version.")]
    int CountTokens(string text);
}

/// <summary>
/// Mock implementation for testing without text completion service.
/// </summary>
public class MockTextCompletionService : ITextCompletionService
{
    /// <inheritdoc />
    public Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Simple mock response for testing
        return Task.FromResult($"Mock response for: {prompt[..System.Math.Min(50, prompt.Length)]}...");
    }

    /// <inheritdoc />
    public Task<string> CompleteJsonAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Return minimal valid JSON
        return Task.FromResult("{}");
    }

    /// <inheritdoc />
#pragma warning disable CS0618 // Obsolete member
    public int CountTokens(string text)
#pragma warning restore CS0618
    {
        // Rough approximation: 1 token ~ 4 characters
        return text.Length / 4;
    }
}
