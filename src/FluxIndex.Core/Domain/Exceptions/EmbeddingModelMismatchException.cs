using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Domain.Exceptions;

/// <summary>
/// 벡터 저장소에 바인딩된 임베딩 모델과 현재 사용 중인 모델이 다를 때 발생하는 예외.
/// 같은 차원이라도 모델이 다르면 벡터 공간이 호환되지 않으므로, 혼합 저장을 방지한다.
/// </summary>
public class EmbeddingModelMismatchException : InvalidOperationException
{
    /// <summary>
    /// 저장소에 기록된 임베딩 Identity.
    /// </summary>
    public EmbeddingIdentity StoredIdentity { get; }

    /// <summary>
    /// 현재 사용 중인 임베딩 Identity.
    /// </summary>
    public EmbeddingIdentity CurrentIdentity { get; }

    public EmbeddingModelMismatchException(EmbeddingIdentity stored, EmbeddingIdentity current)
        : base(FormatMessage(stored, current))
    {
        StoredIdentity = stored;
        CurrentIdentity = current;
    }

    private static string FormatMessage(EmbeddingIdentity stored, EmbeddingIdentity current)
        => $"Embedding model mismatch: store was created with {stored}, " +
           $"but current model is {current}. " +
           $"Vectors from different models are incompatible even with the same dimension. " +
           $"Rebuild the knowledge base or use a different collection.";
}
