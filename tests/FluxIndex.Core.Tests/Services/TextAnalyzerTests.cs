using AwesomeAssertions;
using FluxIndex.Core.Application.Services.KeywordSearch;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// The two built-in <c>ITextAnalyzer</c>s. <see cref="DefaultTextAnalyzer"/> must be exactly what
/// the keyword index did before the seam existed (an existing index keeps matching);
/// <see cref="CjkBigramTextAnalyzer"/> is the opt-in that lets a bare CJK stem match its inflected
/// forms — the defect measured on a production corpus where <c>규정에</c> (115) outnumbered
/// <c>규정</c> (106) as separate terms.
/// </summary>
public class TextAnalyzerTests
{
    [Fact]
    public void Default_SplitsOnNonWord_LowerCases_DropsShortTokensAndEnglishStopWords()
    {
        DefaultTextAnalyzer.Instance.Tokenize("The Quick brown-fox, is at a 42nd Base!")
            .Should().Equal("quick", "brown", "fox", "42nd", "base");
    }

    [Fact]
    public void Default_KeepsACjkWordAsOneTerm()
    {
        // The behaviour a CJK consumer hit: the inflected form and the stem are unrelated terms.
        DefaultTextAnalyzer.Instance.Tokenize("규정에 따라 규정 적용").Should().Equal("규정에", "따라", "규정", "적용");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Both_ReturnNothingForBlankInput(string? text)
    {
        DefaultTextAnalyzer.Instance.Tokenize(text!).Should().BeEmpty();
        CjkBigramTextAnalyzer.Instance.Tokenize(text!).Should().BeEmpty();
    }

    [Fact]
    public void CjkBigram_EmitsOverlappingBigramsForAKoreanRun()
    {
        CjkBigramTextAnalyzer.Instance.Tokenize("규정에").Should().Equal("규정", "정에");
    }

    [Fact]
    public void CjkBigram_BareStemMatchesItsInflectedForms_ByASharedBigram()
    {
        var indexed = new[] { "규정에", "규정을", "규정이", "규정은", "규정의" }
            .SelectMany(CjkBigramTextAnalyzer.Instance.Tokenize)
            .ToHashSet();
        var query = CjkBigramTextAnalyzer.Instance.Tokenize("규정").ToList();

        query.Should().Equal("규정");
        indexed.Should().Contain("규정", "every inflected form shares the stem bigram with the query");
    }

    [Fact]
    public void CjkBigram_SingleCharacterRun_StandsAlone()
    {
        // A one-syllable token (a unit, a counter) must not vanish under a minimum-length rule.
        CjkBigramTextAnalyzer.Instance.Tokenize("개 3 마리").Should().Equal("개", "마리");
    }

    [Fact]
    public void CjkBigram_LatinTokens_FollowTheDefaultRules()
    {
        CjkBigramTextAnalyzer.Instance.Tokenize("The Budget is Final").Should().Equal("budget", "final");
    }

    [Fact]
    public void CjkBigram_MixedScriptWord_IsSplitIntoRuns()
    {
        CjkBigramTextAnalyzer.Instance.Tokenize("iso27001인증서").Should().Equal("iso27001", "인증", "증서");
    }

    [Fact]
    public void CjkBigram_HandlesJapaneseAndChinese()
    {
        CjkBigramTextAnalyzer.Instance.Tokenize("東京都 データ").Should().Equal("東京", "京都", "デー", "ータ");
    }

    [Fact]
    public void CjkBigram_SupplementaryPlaneIdeograph_IsOneCharacter()
    {
        // U+20000 is a surrogate pair in UTF-16; it must not be split into two half-bigrams.
        var ideograph = char.ConvertFromUtf32(0x20000);
        CjkBigramTextAnalyzer.Instance.Tokenize(ideograph + "字").Should().Equal(ideograph + "字");
    }
}
