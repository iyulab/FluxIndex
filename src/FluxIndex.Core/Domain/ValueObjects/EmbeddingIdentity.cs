using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace FluxIndex.Core.Domain.ValueObjects;

/// <summary>
/// 임베딩 공간의 동일성을 식별하는 값 객체.
/// 같은 Identity = 같은 벡터 공간 = 비교 가능한 벡터.
/// </summary>
/// <remarks>
/// <para>
/// 차원수만으로는 임베딩 모델의 동일성을 보장할 수 없다.
/// 동일 차원의 다른 모델(예: OpenAI 1536d vs Cohere 1536d)은 완전히 다른 벡터 공간을 생성한다.
/// 이 값 객체는 Provider + Model 조합으로 임베딩 공간을 고유하게 식별한다.
/// </para>
/// <para>
/// <see cref="Fingerprint"/>는 컬렉션/테이블 네이밍에 사용되는 결정론적 짧은 해시로,
/// SHA256("{Provider}:{Model}") 앞 8자 hex이다.
/// </para>
/// </remarks>
public sealed record EmbeddingIdentity
{
    /// <summary>
    /// 임베딩 제공자 이름 (예: "OpenAI", "LMSupply", "GPUStack").
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// 모델 식별자 (예: "text-embedding-3-small", "all-MiniLM-L6-v2").
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// 벡터 차원수. 모델에서 파생되는 값이지만, 검증용으로 보존한다.
    /// </summary>
    public int Dimension { get; init; }

    /// <summary>
    /// 결정론적 짧은 해시 — 컬렉션/테이블 네이밍에 사용.
    /// SHA256("{Provider}:{Model}") → 앞 8자 hex (소문자).
    /// </summary>
    [JsonIgnore]
    public string Fingerprint => ComputeFingerprint();

    private string ComputeFingerprint()
    {
        var input = $"{Provider.ToLowerInvariant()}:{Model.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    /// <inheritdoc />
    public override string ToString()
        => $"{Provider}:{Model} ({Dimension}d) [{Fingerprint}]";
}
