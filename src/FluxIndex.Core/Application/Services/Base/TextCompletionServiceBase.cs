using Flux.Abstractions;

namespace FluxIndex.Core.Application.Services.Base;

/// <summary>
/// Base class for text completion services.
/// Provides default implementations for optional methods.
/// Consumers implementing AI providers (LMSupply, OpenAI, etc.) should extend this class.
/// </summary>
/// <example>
/// Implement <see cref="CompleteCoreAsync"/>; empty prompts and <see cref="CompleteJsonAsync"/> have defaults
/// (the JSON path calls it with <c>ResponseFormat = "json"</c>).
/// <code>
/// public sealed class MyCompletion(MyClient client) : TextCompletionServiceBase
/// {
///     protected override Task&lt;string&gt; CompleteCoreAsync(
///         string prompt, TextCompletionOptions options, CancellationToken cancellationToken)
///         =&gt; client.CompleteAsync(prompt, options.MaxTokens, options.Temperature, cancellationToken);
/// }
/// </code>
/// <para>Complete samples for OpenAI, compiled against this version, are in the repository's
/// <c>docs/AI_PROVIDER_INTEGRATION.md</c>.</para>
/// </example>
public abstract class TextCompletionServiceBase : ITextCompletionService
{
    private static readonly TextCompletionOptions DefaultOptions = new();

    /// <summary>
    /// Core generation method to implement. Called by <see cref="CompleteAsync"/> after validation.
    /// </summary>
    /// <remarks>
    /// An implementation that can observe the provider's completion reason throws
    /// <see cref="TextCompletionTruncatedException"/> when <see cref="TextCompletionOptions.ThrowOnTruncation"/> is set and
    /// the model stopped at <see cref="TextCompletionOptions.MaxTokens"/>.
    /// </remarks>
    protected abstract Task<string> CompleteCoreAsync(
        string prompt,
        TextCompletionOptions options,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return string.Empty;

        return await CompleteCoreAsync(prompt, options ?? DefaultOptions, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation appends JSON instruction to prompt and extracts JSON from response.
    /// Override for structured output support if your provider has native JSON mode.
    /// </remarks>
    public virtual async Task<string> CompleteJsonAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "{}";

        var jsonPrompt = $"{prompt}\n\nRespond with valid JSON only. No markdown, no explanation, just JSON:";
        var jsonOptions = new TextCompletionOptions
        {
            MaxTokens = options?.MaxTokens ?? DefaultOptions.MaxTokens,
            Temperature = 0.1f,
            TopP = options?.TopP,
            FrequencyPenalty = options?.FrequencyPenalty,
            PresencePenalty = options?.PresencePenalty,
            StopSequences = options?.StopSequences,
            SystemPrompt = options?.SystemPrompt,
            ResponseFormat = "json",
            ResponseSchema = options?.ResponseSchema,
            ThrowOnTruncation = options?.ThrowOnTruncation ?? false,
            EnableThinking = options?.EnableThinking,
        };
        var result = await CompleteCoreAsync(jsonPrompt, jsonOptions, cancellationToken);

        return ExtractJson(result);
    }

    /// <summary>
    /// Extracts JSON object or array from a response string.
    /// </summary>
    protected static string ExtractJson(string response)
    {
        var start = response.IndexOf('{');
        var arrayStart = response.IndexOf('[');

        if (start < 0 && arrayStart < 0)
            return "{}";

        if (start < 0 || (arrayStart >= 0 && arrayStart < start))
            start = arrayStart;

        var isArray = response[start] == '[';
        var end = isArray
            ? response.LastIndexOf(']')
            : response.LastIndexOf('}');

        if (end <= start)
            return isArray ? "[]" : "{}";

        return response[start..(end + 1)];
    }

}
