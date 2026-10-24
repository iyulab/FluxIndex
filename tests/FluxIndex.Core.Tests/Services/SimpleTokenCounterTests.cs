using FluxIndex.Core.Application.Services;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// The default token estimate behind chunk token counts and the indexer's oversized-chunk split. Each kind of character has
/// its own ratio (fitted to o200k_base): Latin letters 7 characters per token, Hangul 1.35, Han 1.2, Kana 1.5, digits 1.3,
/// punctuation 1.25, a whitespace run 2.75.
/// </summary>
public class SimpleTokenCounterTests
{
    private readonly SimpleTokenCounter _counter = new();

    [Theory]
    [InlineData("", 0)]
    [InlineData("Hello World", 2)]          // 10 / 7 + 1 / 2.75 = 1.79
    [InlineData("안녕하세요", 4)]           // 5 / 1.35 = 3.70
    [InlineData("회의 notes", 3)]           // 2 / 1.35 + 1 / 2.75 + 5 / 7 = 2.56
    [InlineData("会议记录", 4)]             // 4 / 1.2 = 3.33
    [InlineData("{\"a\": 1}", 6)]           // 5 / 1.25 + 1 / 7 + 1 / 2.75 + 1 / 1.3 = 5.28
    public void Count_ChargesEachKindOfCharacterAtItsOwnRatio(string text, int expected)
    {
        Assert.Equal(expected, _counter.Count(text));
    }

    [Fact]
    public void KoreanText_IsNotCountedAsIfItWereEnglish()
    {
        // 200 Hangul syllables: o200k_base needs about 148 tokens; four characters per token would say 50.
        var korean = string.Concat(Enumerable.Repeat("가나다라마바사아자차", 20));

        Assert.Equal(149, _counter.Count(korean)); // 200 / 1.35 = 148.1
    }

    [Fact]
    public void Count_OfSeveralTexts_IsTheirSum()
    {
        Assert.Equal(6, _counter.Count(new[] { "Hello World", "안녕하세요" }));
    }
}
