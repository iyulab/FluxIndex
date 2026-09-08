using System.Text.RegularExpressions;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Services.KeywordSearch;

/// <summary>
/// The analyzer the keyword index has always used: split on non-word characters, lower-case, drop
/// tokens shorter than two characters and a small English stop-word list.
/// </summary>
/// <remarks>
/// Word characters include every letter the runtime knows, so a Korean, Japanese or Chinese run
/// stays one token per whitespace-delimited word (<c>규정에</c> and <c>규정</c> are different terms
/// here). Use <see cref="CjkBigramTextAnalyzer"/> — or register your own <see cref="ITextAnalyzer"/>
/// — for text where that loses recall.
/// </remarks>
public sealed partial class DefaultTextAnalyzer : ITextAnalyzer
{
    /// <summary>Shared instance; the analyzer holds no state.</summary>
    public static DefaultTextAnalyzer Instance { get; } = new();

    /// <summary>Stop words removed during tokenization (English).</summary>
    public static IReadOnlySet<string> StopWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
        "in", "with", "to", "for", "of", "as", "by", "this", "that", "these", "those",
        "it", "its", "be", "are", "was", "were", "been", "being", "have", "has", "had"
    };

    [GeneratedRegex(@"\W+")]
    private static partial Regex NonWord();

    /// <inheritdoc />
    public IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var token in NonWord().Split(text.ToLowerInvariant()))
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length <= 1)
                continue;
            if (StopWords.Contains(token))
                continue;
            yield return token;
        }
    }
}
