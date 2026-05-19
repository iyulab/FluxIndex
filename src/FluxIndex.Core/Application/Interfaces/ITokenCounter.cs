namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Token counter interface for FluxIndex query analysis.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Counts tokens in a text string.</summary>
    int Count(string text);

    /// <summary>Counts tokens across a sequence of text strings.</summary>
    int Count(IEnumerable<string> texts);

    /// <summary>Returns true if this counter supports the given model.</summary>
    bool SupportsModel(string modelId);

    /// <summary>Returns true if this counter produces approximate results for the given model.</summary>
    bool IsApproximate(string modelId) => false;
}
