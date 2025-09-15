using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Application.Interfaces;

/// <summary>
/// Abstraction for text completion services - to be implemented by consuming applications
/// FluxIndex does not provide text completion implementation, only the interface
/// This can be implemented using OpenAI, Azure OpenAI, Claude, local models, etc.
/// </summary>
public interface ITextCompletionService
{
    /// <summary>
    /// Generates a completion for the given prompt
    /// </summary>
    /// <param name="prompt">The prompt to complete</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="temperature">Sampling temperature (0.0 - 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated text completion</returns>
    Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a JSON completion with structured output
    /// </summary>
    /// <param name="prompt">The prompt requesting JSON output</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated JSON string</returns>
    Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts tokens in the given text
    /// </summary>
    /// <param name="text">Text to count tokens for</param>
    /// <returns>Number of tokens</returns>
    int CountTokens(string text);
}

/// <summary>
/// Optional: Mock implementation for testing without text completion service
/// </summary>
public class MockTextCompletionService : ITextCompletionService
{
    public Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        // Simple mock response for testing
        return Task.FromResult($"Mock response for: {prompt.Substring(0, System.Math.Min(50, prompt.Length))}...");
    }

    public Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        // Return minimal valid JSON
        return Task.FromResult("{}");
    }

    public int CountTokens(string text)
    {
        // Rough approximation: 1 token ≈ 4 characters
        return text.Length / 4;
    }
}