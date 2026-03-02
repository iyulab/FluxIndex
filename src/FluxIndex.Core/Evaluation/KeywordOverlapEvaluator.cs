using System.Text.RegularExpressions;

namespace FluxIndex.Core.Evaluation;

/// <summary>
/// Deterministic evaluator using keyword overlap. No LLM required.
/// <para>
/// Faithfulness: fraction of answer words that appear in the combined retrieved contexts.
/// Relevancy:    fraction of query keywords that appear in the generated answer.
/// </para>
/// </summary>
public sealed partial class KeywordOverlapEvaluator : IResponseEvaluator
{
    // Minimal English/Korean stopword list -- not exhaustive but avoids penalising articles
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "shall", "can", "and", "or", "but", "in",
        "on", "at", "to", "for", "of", "with", "by", "from", "as", "it", "its",
        "this", "that", "these", "those", "i", "you", "he", "she", "we", "they",
        "\uc774", "\uac00", "\uc740", "\ub294", "\uc744", "\ub97c", "\uc5d0", "\uc758", "\uc640", "\uacfc", "\ub85c", "\uc73c\ub85c",
        "\uc5d0\uc11c", "\ub3c4", "\uae4c\uc9c0", "\ubd80\ud130"
    };

    /// <inheritdoc />
    public Task<(double Faithfulness, double Relevancy)> EvaluateAsync(
        string query,
        IReadOnlyList<string> retrievedContexts,
        string generatedAnswer,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(generatedAnswer))
            return Task.FromResult((0.0, 0.0));

        var answerWords = Tokenize(generatedAnswer);
        var queryWords = Tokenize(query)
            .Except(Stopwords, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contextWords = retrievedContexts
            .SelectMany(Tokenize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Faithfulness: what fraction of answer content words appear in retrieved contexts?
        var faithfulnessWords = answerWords
            .Except(Stopwords, StringComparer.OrdinalIgnoreCase)
            .ToList();

        double faithfulness = faithfulnessWords.Count == 0
            ? 1.0 // vacuously true for empty/stopword-only answers
            : (double)faithfulnessWords.Count(w => contextWords.Contains(w)) / faithfulnessWords.Count;

        // Relevancy: what fraction of meaningful query words appear in the answer?
        double relevancy = queryWords.Count == 0
            ? 1.0
            : (double)queryWords.Count(w => answerWords.Contains(w, StringComparer.OrdinalIgnoreCase)) / queryWords.Count;

        return Task.FromResult((Math.Clamp(faithfulness, 0.0, 1.0), Math.Clamp(relevancy, 0.0, 1.0)));
    }

    private static List<string> Tokenize(string text)
        => [.. TokenizerRegex().Split(text.ToLowerInvariant()).Where(t => t.Length > 1)];

    [GeneratedRegex(@"[\s\p{P}\p{S}]+")]
    private static partial Regex TokenizerRegex();
}
