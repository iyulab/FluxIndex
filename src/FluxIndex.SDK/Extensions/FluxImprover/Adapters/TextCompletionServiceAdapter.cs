using System.Runtime.CompilerServices;
using FluxImprover.Services;
using FluxIndexCompletion = FluxIndex.Core.Application.Interfaces.ITextCompletionService;

namespace FluxIndex.SDK.Extensions.FluxImprover.Adapters;

/// <summary>
/// Adapter that bridges FluxIndex.Core.ITextCompletionService to FluxImprover.Abstractions.ITextCompletionService.
/// This allows FluxImprover services to use FluxIndex's text completion implementations.
/// </summary>
public sealed class TextCompletionServiceAdapter : ITextCompletionService
{
    private readonly FluxIndexCompletion _fluxIndexService;

    private const int DefaultMaxTokens = 500;
    private const float DefaultTemperature = 0.7f;

    /// <summary>
    /// Creates a new adapter wrapping the FluxIndex text completion service.
    /// </summary>
    /// <param name="fluxIndexService">The FluxIndex text completion service to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when fluxIndexService is null.</exception>
    public TextCompletionServiceAdapter(FluxIndexCompletion fluxIndexService)
    {
        _fluxIndexService = fluxIndexService ?? throw new ArgumentNullException(nameof(fluxIndexService));
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string prompt,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var maxTokens = options?.MaxTokens ?? DefaultMaxTokens;
        var temperature = options?.Temperature ?? DefaultTemperature;

        // Build the effective prompt with system prompt if provided
        var effectivePrompt = BuildEffectivePrompt(prompt, options);

        // Use JSON completion if JsonMode is enabled
        if (options?.JsonMode == true)
        {
            return await _fluxIndexService.GenerateJsonCompletionAsync(
                effectivePrompt,
                maxTokens,
                cancellationToken);
        }

        return await _fluxIndexService.GenerateCompletionAsync(
            effectivePrompt,
            maxTokens,
            temperature,
            cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // FluxIndex's ITextCompletionService doesn't support streaming natively,
        // so we return the full response as a single token
        var response = await CompleteAsync(prompt, options, cancellationToken);
        yield return response;
    }

    /// <summary>
    /// Builds the effective prompt by combining system prompt and user prompt.
    /// </summary>
    private static string BuildEffectivePrompt(string prompt, CompletionOptions? options)
    {
        if (string.IsNullOrEmpty(options?.SystemPrompt))
        {
            return prompt;
        }

        // Combine system prompt with user prompt
        return $"{options.SystemPrompt}\n\n{prompt}";
    }
}
