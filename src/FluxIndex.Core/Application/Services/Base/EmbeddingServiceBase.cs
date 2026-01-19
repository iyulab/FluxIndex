using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.Core.Application.Services.Base;

/// <summary>
/// Base class for embedding services.
/// Provides default implementations for optional methods.
/// Consumers implementing AI providers (LMSupply, OpenAI, etc.) should extend this class.
/// </summary>
/// <example>
/// // LMSupply implementation (~10 lines):
/// public class LMSupplyEmbedder : EmbeddingServiceBase
/// {
///     private readonly IEmbeddingModel _model;
///     public LMSupplyEmbedder(IEmbeddingModel model) => _model = model;
///
///     protected override Task&lt;float[]&gt; EmbedCoreAsync(string text, CancellationToken ct)
///         => _model.EmbedAsync(text, ct);
///     public override int GetEmbeddingDimension() => _model.Dimensions;
///     public override string GetModelName() => _model.ModelId;
/// }
///
/// // OpenAI implementation (~15 lines):
/// public class OpenAIEmbedder : EmbeddingServiceBase
/// {
///     private readonly OpenAIClient _client;
///     public OpenAIEmbedder(OpenAIClient client) => _client = client;
///
///     protected override async Task&lt;float[]&gt; EmbedCoreAsync(string text, CancellationToken ct)
///     {
///         var response = await _client.GetEmbeddingsAsync(text, ct);
///         return response.Value.Data[0].Embedding.ToArray();
///     }
///     public override int GetEmbeddingDimension() => 1536; // text-embedding-3-small
///     public override string GetModelName() => "text-embedding-3-small";
/// }
/// </example>
public abstract class EmbeddingServiceBase : IEmbeddingService
{
    /// <summary>
    /// Core embedding method to implement. Called by GenerateEmbeddingAsync after validation.
    /// </summary>
    protected abstract Task<float[]> EmbedCoreAsync(string text, CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return await EmbedCoreAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation processes texts sequentially.
    /// Override for batch optimization if the underlying provider supports batch embedding.
    /// </remarks>
    public virtual async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await GenerateEmbeddingAsync(text, cancellationToken));
        }
        return results;
    }

    /// <inheritdoc />
    public abstract int GetEmbeddingDimension();

    /// <inheritdoc />
    public abstract string GetModelName();

    /// <inheritdoc />
    /// <remarks>Default: 512 tokens. Override if your model has different limits.</remarks>
    public virtual int GetMaxTokens() => 512;

    /// <inheritdoc />
    /// <remarks>
    /// Default: rough approximation (length / 4 for English, 1:1 for CJK).
    /// Override for accurate tokenization if your provider has a tokenizer.
    /// </remarks>
    public virtual Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(0);

        // Rough approximation: CJK = 1 token per char, others = 4 chars per token
        var cjkCount = text.Count(IsCjkCharacter);
        var otherCount = text.Length - cjkCount;
        var tokenCount = cjkCount + (otherCount / 4) + 2; // +2 for special tokens

        return Task.FromResult(tokenCount);
    }

    private static bool IsCjkCharacter(char c) =>
        (c >= '\u4E00' && c <= '\u9FFF') ||   // CJK Unified Ideographs
        (c >= '\u3400' && c <= '\u4DBF') ||   // CJK Extension A
        (c >= '\uAC00' && c <= '\uD7AF') ||   // Hangul Syllables
        (c >= '\u3040' && c <= '\u309F') ||   // Hiragana
        (c >= '\u30A0' && c <= '\u30FF');     // Katakana
}
