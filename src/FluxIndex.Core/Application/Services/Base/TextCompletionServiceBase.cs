using Flux.Abstractions;

namespace FluxIndex.Core.Application.Services.Base;

/// <summary>
/// Base class for text completion services.
/// Provides default implementations for optional methods.
/// Consumers implementing AI providers (LMSupply, OpenAI, etc.) should extend this class.
/// </summary>
/// <example>
/// // LMSupply implementation (~15 lines):
/// public class LMSupplyGenerator : TextCompletionServiceBase
/// {
///     private readonly ITextGenerator _model;
///     public LMSupplyGenerator(ITextGenerator model) => _model = model;
///
///     protected override async Task&lt;string&gt; CompleteCoreAsync(string prompt, TextCompletionOptions options, CancellationToken ct)
///     {
///         var sb = new StringBuilder();
///         await foreach (var token in _model.GenerateAsync(prompt, new() { MaxTokens = options.MaxTokens, Temperature = options.Temperature }, ct))
///             sb.Append(token);
///         return sb.ToString();
///     }
/// }
///
/// // OpenAI implementation (~10 lines):
/// public class OpenAIGenerator : TextCompletionServiceBase
/// {
///     private readonly OpenAIClient _client;
///     public OpenAIGenerator(OpenAIClient client) => _client = client;
///
///     protected override async Task&lt;string&gt; CompleteCoreAsync(string prompt, TextCompletionOptions options, CancellationToken ct)
///     {
///         var response = await _client.GetChatCompletionsAsync(prompt, ct);
///         return response.Value.Choices[0].Message.Content;
///     }
/// }
/// </example>
public abstract class TextCompletionServiceBase : ITextCompletionService
{
    private static readonly TextCompletionOptions DefaultOptions = new();

    /// <summary>
    /// Core generation method to implement. Called by <see cref="CompleteAsync"/> after validation.
    /// </summary>
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
