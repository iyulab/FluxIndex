namespace Flux.Abstractions;

/// <summary>
/// Minimal language profile contract shared across the Flux ecosystem.
/// Provides common language-specific text segmentation metadata.
/// </summary>
/// <remarks>
/// Each Flux module may extend this with module-specific members:
/// <list type="bullet">
/// <item><description>FileFlux: rich Regex-based members via <c>ILanguageProfile</c> in <c>FileFlux.Core</c></description></item>
/// <item><description>FluxCurator: string-pattern-based members via <c>ILanguageProfile</c> in <c>FluxCurator.Core</c></description></item>
/// </list>
/// </remarks>
public interface ILanguageProfile
{
    /// <summary>
    /// ISO 639-1 language code (e.g., "en", "ko", "ja", "zh").
    /// </summary>
    string LanguageCode { get; }

    /// <summary>
    /// Human-readable language name (e.g., "English", "Korean").
    /// </summary>
    string LanguageName { get; }

    /// <summary>
    /// Regex pattern string for detecting sentence endings.
    /// Implementations may compile this into a <see cref="System.Text.RegularExpressions.Regex"/> internally.
    /// </summary>
    string SentenceEndPattern { get; }

    /// <summary>
    /// Finds sentence boundaries in the given text.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <returns>A list of character positions (exclusive) where sentences end.</returns>
    IReadOnlyList<int> FindSentenceBoundaries(string text);
}
