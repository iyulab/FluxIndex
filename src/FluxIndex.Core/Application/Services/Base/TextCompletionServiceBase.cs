using FluxIndex.Core.Application.Interfaces;

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
///     private readonly IGeneratorModel _model;
///     public LMSupplyGenerator(IGeneratorModel model) => _model = model;
///
///     protected override async Task&lt;string&gt; GenerateCoreAsync(string prompt, int maxTokens, float temperature, CancellationToken ct)
///     {
///         var sb = new StringBuilder();
///         await foreach (var token in _model.GenerateAsync(prompt, new() { MaxTokens = maxTokens, Temperature = temperature }, ct))
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
///     protected override async Task&lt;string&gt; GenerateCoreAsync(string prompt, int maxTokens, float temperature, CancellationToken ct)
///     {
///         var response = await _client.GetChatCompletionsAsync(prompt, ct);
///         return response.Value.Choices[0].Message.Content;
///     }
/// }
/// </example>
public abstract class TextCompletionServiceBase : ITextCompletionService
{
    /// <summary>
    /// Core generation method to implement. Called by GenerateCompletionAsync after validation.
    /// </summary>
    protected abstract Task<string> GenerateCoreAsync(
        string prompt,
        int maxTokens,
        float temperature,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<string> GenerateCompletionAsync(
        string prompt,
        int maxTokens = 500,
        float temperature = 0.7f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return string.Empty;

        return await GenerateCoreAsync(prompt, maxTokens, temperature, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation appends JSON instruction to prompt and extracts JSON from response.
    /// Override for structured output support if your provider has native JSON mode.
    /// </remarks>
    public virtual async Task<string> GenerateJsonCompletionAsync(
        string prompt,
        int maxTokens = 500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "{}";

        var jsonPrompt = $"{prompt}\n\nRespond with valid JSON only. No markdown, no explanation, just JSON:";
        var result = await GenerateCoreAsync(jsonPrompt, maxTokens, 0.1f, cancellationToken);

        return ExtractJson(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default: rough approximation (length / 4 for English, 1:1 for CJK).
    /// Override for accurate tokenization if your provider has a tokenizer.
    /// </remarks>
    public virtual int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var cjkCount = text.Count(IsCjkCharacter);
        var otherCount = text.Length - cjkCount;

        return cjkCount + (otherCount / 4) + 1;
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

    private static bool IsCjkCharacter(char c) =>
        (c >= '\u4E00' && c <= '\u9FFF') ||
        (c >= '\u3400' && c <= '\u4DBF') ||
        (c >= '\uAC00' && c <= '\uD7AF') ||
        (c >= '\u3040' && c <= '\u309F') ||
        (c >= '\u30A0' && c <= '\u30FF');
}
