namespace FluxIndex.Core.Constants;

/// <summary>
/// 임베딩 관련 기본값. 프로바이더/모델에 따라 다를 수 있으므로
/// 가능한 한 설정에서 명시적으로 지정하는 것을 권장한다.
/// </summary>
public static class EmbeddingDefaults
{
    /// <summary>
    /// 기본 임베딩 벡터 차원.
    /// 프로바이더별 실제 차원: OpenAI text-embedding-3-small=1536, Qwen3=1024, LMSupply=384~768.
    /// </summary>
    public const int DefaultVectorDimension = 1536;
}
