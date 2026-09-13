using FluxIndex.Core.Services;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// The default splitter behind <c>IndexerOptions.ChunkSize</c>/<c>ChunkOverlap</c>. Until 0.38.0 it had
/// no caller (the SDK injected it and never invoked it), so none of this was ever exercised.
/// </summary>
public class SimpleChunkingServiceTests
{
    private readonly SimpleChunkingService _splitter = new();

    [Fact]
    public void TextThatFits_IsReturnedVerbatim()
    {
        const string text = "Short  text\nwith\tirregular whitespace.";

        var chunks = _splitter.ChunkText(text, chunkSize: 100, chunkOverlap: 10).ToList();

        Assert.Equal([text], chunks);
    }

    [Fact]
    public void Windows_OverlapByCharacters()
    {
        var text = string.Concat(Enumerable.Range(0, 10).Select(_ => "0123456789")); // 100 chars, no break points

        var chunks = _splitter.ChunkText(text, chunkSize: 30, chunkOverlap: 5).ToList();

        Assert.True(chunks.Count > 1);
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.StartsWith(chunks[i - 1][^5..], chunks[i]);
        }
        Assert.EndsWith(text[^10..], chunks[^1]);
    }

    [Fact]
    public void PrefersASentenceBoundary_OverAHardCut()
    {
        const string text = "Alpha beta gamma delta. Epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho.";

        var chunks = _splitter.ChunkText(text, chunkSize: 40, chunkOverlap: 0).ToList();

        Assert.Equal("Alpha beta gamma delta. ", chunks[0]);
    }

    [Fact]
    public void ANaturalBreakRightAfterTheStart_StillAdvances()
    {
        // The only sentence boundary in the first window sits two characters in. With a 40-character
        // overlap the "next window starts overlap before this one ended" rule would move backwards and
        // never terminate; the splitter must advance to the end of the short piece instead.
        var text = "A. " + new string('x', 300);

        var chunks = _splitter.ChunkText(text, chunkSize: 50, chunkOverlap: 40).Take(1_000).ToList();

        Assert.True(chunks.Count < 1_000, "the splitter must terminate");
        Assert.Equal("A. ", chunks[0]);
        // After the short first piece the window starts at 3 and advances by (50 - 40) = 10 characters:
        // starts 3, 13, …, 253 — 26 full windows of 50 characters, the last of which ends the text.
        Assert.Equal(27, chunks.Count);
        Assert.All(chunks.Skip(1), c => Assert.Equal(50, c.Length));
        Assert.EndsWith(chunks[^1], text);
    }

    [Fact]
    public void OverlapNotSmallerThanChunkSize_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => _splitter.ChunkText("some text long enough to split", chunkSize: 10, chunkOverlap: 10).ToList());

        Assert.Equal("chunkOverlap", ex.ParamName);
    }
}
