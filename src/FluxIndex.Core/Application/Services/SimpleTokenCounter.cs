using FluxIndex.Core.Application.Interfaces;
using TokenEstimation;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// Character-ratio approximation token counter: each kind of character (Latin letters, Hangul, Kana, Han, digits,
/// punctuation, whitespace runs) has its own characters-per-token ratio, fitted to o200k_base. English, Korean, mixed
/// prose, code and JSON each come within a few percent of the real count.
/// </summary>
public class SimpleTokenCounter : ITokenCounter
{
    /// <inheritdoc />
    public int Count(string text) => CharTokenEstimator.Count(text, CharTokenRatios.O200k);

    /// <inheritdoc />
    public int Count(IEnumerable<string> texts) => texts.Sum(Count);

    /// <inheritdoc />
    public bool SupportsModel(string modelId) => false;

    /// <inheritdoc />
    public bool IsApproximate(string modelId) => true;
}
