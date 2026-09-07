using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.ValueObjects;

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

    /// <summary>
    /// 제공자 이름을 반환한다. 하위 클래스에서 override하여 자신의 provider를 선언한다.
    /// </summary>
    protected abstract string GetProviderName();

    /// <summary>
    /// 이 서비스가 만드는 임베딩의 파이프라인 리비전. 생성 시점에만 설정할 수 있다
    /// (<c>new SomeEmbeddingService(...) { Revision = "r2" }</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GetRevision"/>을 override할 수 없는 <c>sealed</c> 구현체 — 이 리포가 제공하는
    /// provider들이 전부 그렇다 — 를 위한 경로다. 이것이 없으면 seam이 기반 클래스에만 있고
    /// 정작 소비자가 쓰는 구현체에서는 닿지 않는다.
    /// </para>
    /// <para>
    /// <c>init</c>인 것은 의도다. 리비전은 벡터 공간의 이름을 정하므로, 서비스가 저장소에
    /// 바인딩된 뒤 바뀌면 이미 쓰인 벡터와 앞으로 쓸 벡터가 다른 이름 아래 갈라진다.
    /// </para>
    /// </remarks>
    public string? Revision { get; init; }

    /// <summary>
    /// 이 서비스가 만드는 임베딩의 파이프라인 리비전을 반환한다.
    /// 기본은 <see cref="Revision"/>(설정하지 않았으면 <c>null</c>).
    /// </summary>
    /// <remarks>
    /// 같은 Provider + Model이 이전과 «비교 불가능한» 벡터를 내게 됐을 때만 올린다 —
    /// 토크나이저 수정, 풀링/정규화 변경, 양자화 전환, ONNX 재export 같은 수치 변경이 그 경우다.
    /// 올리면 <see cref="EmbeddingIdentity.Fingerprint"/>가 바뀌고, 지문으로 이름을 짓는
    /// 컬렉션/테이블이 갈라져 기존 벡터와 섞이지 않는다.
    /// </remarks>
    protected virtual string? GetRevision() => Revision;

    /// <inheritdoc />
    public virtual EmbeddingIdentity GetIdentity() => new()
    {
        Provider = GetProviderName(),
        Model = GetModelName(),
        Dimension = GetEmbeddingDimension(),
        Revision = GetRevision()
    };

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
