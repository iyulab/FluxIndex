namespace Flux.Abstractions;

/// <summary>
/// Options for text completion generation.
/// </summary>
public class TextCompletionOptions
{
    /// <summary>
    /// Temperature for sampling (0.0 = deterministic, higher = more creative).
    /// Default: 0.7
    /// </summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>
    /// Maximum number of tokens to generate.
    /// Default: 500
    /// </summary>
    public int MaxTokens { get; init; } = 500;

    /// <summary>
    /// Top-p nucleus sampling threshold (0.0 - 1.0).
    /// </summary>
    public float? TopP { get; init; }

    /// <summary>
    /// Frequency penalty (-2.0 to 2.0).
    /// </summary>
    public float? FrequencyPenalty { get; init; }

    /// <summary>
    /// Presence penalty (-2.0 to 2.0).
    /// </summary>
    public float? PresencePenalty { get; init; }

    /// <summary>
    /// Stop sequences that will terminate generation.
    /// </summary>
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// System prompt prepended to the request.
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Response format hint (e.g., "json" for JSON mode).
    /// </summary>
    public string? ResponseFormat { get; init; }
}
