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
/// <see cref="Revision"/>이 설정된 경우에만 SHA256("{Provider}:{Model}:{Revision}")가 된다.
/// </para>
/// <para>
/// Provider + Model 만으로는 «같은 모델이 다른 벡터 공간을 만드는» 경우를 표현할 수 없다 —
/// 임베딩 파이프라인의 수치 변경(토크나이저 수정, 풀링 변경, 양자화 전환, ONNX 재export)은
/// Provider도 Model도 바꾸지 않으면서 이전에 저장된 벡터와 비교 불가능한 벡터를 만든다.
/// 그 경우 소비자는 <see cref="Revision"/>을 올려 새 벡터 공간임을 선언한다.
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
    /// 임베딩 파이프라인 리비전 — 소비자가 정하는 불투명한 토큰(예: "r2", "2026-09-tokenizer-fix").
    /// 같은 Provider + Model이 이전과 비교 불가능한 벡터를 내게 됐을 때 올린다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 설정하지 않으면(기본) <see cref="Fingerprint"/>는 리비전 개념이 없던 시절과 완전히 동일하다 —
    /// 기존 소비자의 컬렉션/테이블이 업그레이드만으로 개명되지 않는다.
    /// </para>
    /// <para>
    /// 공백뿐인 값은 «미설정»으로 정규화한다. 설정 바인딩이 비어 있는 값을 넘겼다고 해서
    /// 벡터 공간이 갈라지면 안 되기 때문이다.
    /// </para>
    /// <para>
    /// Provider·Model과 달리 대소문자를 접지 않는다 — 이 값은 이름이 아니라 «구분»이 존재
    /// 이유인 불투명 토큰이라, 접으면 서로 다른 두 리비전이 한 지문을 공유하게 된다.
    /// </para>
    /// </remarks>
    public string? Revision
    {
        get => _revision;
        init => _revision = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private readonly string? _revision;

    /// <summary>
    /// 결정론적 짧은 해시 — 컬렉션/테이블 네이밍에 사용.
    /// SHA256("{Provider}:{Model}") → 앞 8자 hex (소문자).
    /// <see cref="Revision"/>이 설정된 경우 SHA256("{Provider}:{Model}:{Revision}").
    /// </summary>
    [JsonIgnore]
    public string Fingerprint => ComputeFingerprint();

    private string ComputeFingerprint()
    {
        var input = _revision is null
            ? $"{Provider.ToLowerInvariant()}:{Model.ToLowerInvariant()}"
            : $"{Provider.ToLowerInvariant()}:{Model.ToLowerInvariant()}:{_revision}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    /// <inheritdoc />
    public override string ToString()
        => _revision is null
            ? $"{Provider}:{Model} ({Dimension}d) [{Fingerprint}]"
            : $"{Provider}:{Model}@{_revision} ({Dimension}d) [{Fingerprint}]";
}
