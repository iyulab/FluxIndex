using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Character-ratio approximation token counter.
/// Uses ~4 chars/token for Latin text, ~2 chars/token for Korean (CJK).
/// </summary>
public class SimpleTokenCounter : ITokenCounter
{
    /// <inheritdoc />
    public int Count(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var koreanChars = text.Count(c => c >= 0xAC00 && c <= 0xD7A3);
        var otherChars = text.Length - koreanChars;

        return (koreanChars / 2) + (otherChars / 4) + 1;
    }

    /// <inheritdoc />
    public int Count(IEnumerable<string> texts) => texts.Sum(Count);

    /// <inheritdoc />
    public bool SupportsModel(string modelId) => false;

    /// <inheritdoc />
    public bool IsApproximate(string modelId) => true;
}
