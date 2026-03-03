using System.Runtime.CompilerServices;

namespace Flux.Abstractions;

/// <summary>
/// Abstraction for text completion services.
/// Implementations wrap LLM providers (OpenAI, Anthropic, local models, etc.).
/// </summary>
/// <remarks>
/// Only <see cref="CompleteAsync"/> is required; all other methods have
/// default interface method (DIM) implementations that delegate to it.
/// </remarks>
public interface ITextCompletionService
{
    /// <summary>
    /// Generates a completion for the given prompt.
    /// </summary>
    /// <param name="prompt">The prompt to complete.</param>
    /// <param name="options">Optional completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated text completion.</returns>
    Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a JSON completion with structured output.
    /// Default implementation delegates to <see cref="CompleteAsync"/>.
    /// </summary>
    /// <param name="prompt">The prompt requesting JSON output.</param>
    /// <param name="options">Optional completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated JSON string.</returns>
    Task<string> CompleteJsonAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return CompleteAsync(prompt, options, cancellationToken);
    }

    /// <summary>
    /// Generates text completions for multiple prompts in batch.
    /// Default implementation runs prompts sequentially.
    /// </summary>
    /// <param name="prompts">The prompts to complete.</param>
    /// <param name="options">Optional completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated completions in the same order as inputs.</returns>
    async Task<IReadOnlyList<string>> CompleteBatchAsync(
        IEnumerable<string> prompts,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        foreach (var prompt in prompts)
        {
            var result = await CompleteAsync(prompt, options, cancellationToken);
            results.Add(result);
        }
        return results;
    }

    /// <summary>
    /// Streams a completion for the given prompt token by token.
    /// Default implementation wraps <see cref="CompleteAsync"/> as a single item.
    /// </summary>
    /// <param name="prompt">The prompt to complete.</param>
    /// <param name="options">Optional completion options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async stream of generated text tokens.</returns>
    async IAsyncEnumerable<string> CompleteStreamAsync(
        string prompt,
        TextCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await CompleteAsync(prompt, options, cancellationToken);
        yield return result;
    }
}
