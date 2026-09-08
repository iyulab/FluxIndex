using System.Text;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Services.KeywordSearch;

/// <summary>
/// Dependency-free analyzer for text that mixes Latin script with Korean, Japanese or Chinese.
/// Latin tokens are handled exactly as <see cref="DefaultTextAnalyzer"/> does; a run of CJK
/// characters is emitted as overlapping character bigrams (a single character stands alone), so a
/// bare stem in the query matches its inflected forms in the index by their shared bigrams
/// (<c>규정</c> ⊂ <c>규정에</c>, <c>규정을</c>, <c>규정이</c>).
/// </summary>
/// <remarks>
/// This is the recall-first baseline that needs no morphological analyzer. It over-generates
/// (bigrams that cross a morpheme boundary are indexed too), which BM25's IDF weighting tolerates
/// well in practice; a consumer that wants linguistic precision plugs in a real tokenizer through
/// <see cref="ITextAnalyzer"/>. Opt-in: nothing changes for consumers that do not register it.
/// Scans by Unicode scalar, not UTF-16 code unit, so a supplementary-plane ideograph is one
/// character and is never split into the halves of its surrogate pair.
/// </remarks>
public sealed class CjkBigramTextAnalyzer : ITextAnalyzer
{
    /// <summary>Shared instance; the analyzer holds no state.</summary>
    public static CjkBigramTextAnalyzer Instance { get; } = new();

    /// <inheritdoc />
    public IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        // One pass over scalars: a run is a maximal sequence of word scalars of one script class
        // (CJK or not). A separator or a script change ends the run; each run is emitted by its
        // script's rule.
        var run = new List<string>();
        var runIsCjk = false;

        foreach (var rune in text.ToLowerInvariant().EnumerateRunes())
        {
            var isWord = Rune.IsLetterOrDigit(rune) || rune.Value == '_';
            if (!isWord)
            {
                foreach (var term in EmitRun(run, runIsCjk)) yield return term;
                run.Clear();
                continue;
            }

            var cjk = IsCjk(rune);
            if (run.Count > 0 && cjk != runIsCjk)
            {
                foreach (var term in EmitRun(run, runIsCjk)) yield return term;
                run.Clear();
            }

            runIsCjk = cjk;
            run.Add(rune.ToString());
        }

        foreach (var term in EmitRun(run, runIsCjk)) yield return term;
    }

    private static string[] EmitRun(List<string> run, bool cjk)
    {
        if (run.Count == 0)
            return [];

        if (!cjk)
        {
            var word = string.Concat(run);
            if (word.Length <= 1 || DefaultTextAnalyzer.StopWords.Contains(word))
                return [];
            return [word];
        }

        if (run.Count == 1)
            return [run[0]];

        var bigrams = new string[run.Count - 1];
        for (var i = 0; i < bigrams.Length; i++)
            bigrams[i] = run[i] + run[i + 1];
        return bigrams;
    }

    /// <summary>
    /// Hangul (syllables and jamo), CJK unified ideographs (base block, extension A, the
    /// compatibility block, and the supplementary-plane extensions B–I), Hiragana, Katakana.
    /// </summary>
    private static bool IsCjk(Rune r) => r.Value switch
    {
        >= 0xAC00 and <= 0xD7A3 => true,     // Hangul syllables
        >= 0x1100 and <= 0x11FF => true,     // Hangul jamo
        >= 0x3130 and <= 0x318F => true,     // Hangul compatibility jamo
        >= 0x4E00 and <= 0x9FFF => true,     // CJK unified ideographs
        >= 0x3400 and <= 0x4DBF => true,     // CJK extension A
        >= 0xF900 and <= 0xFAFF => true,     // CJK compatibility ideographs
        >= 0x3040 and <= 0x309F => true,     // Hiragana
        >= 0x30A0 and <= 0x30FF => true,     // Katakana
        >= 0x20000 and <= 0x3134F => true,   // CJK extensions B–I (supplementary planes)
        _ => false,
    };
}
