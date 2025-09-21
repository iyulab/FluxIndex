namespace FluxIndex.Interfaces;

/// <summary>
/// 임베딩 서비스 인터페이스
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// 텍스트를 임베딩 벡터로 변환
    /// </summary>
    /// <param name="text">변환할 텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>임베딩 벡터</returns>
    Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 텍스트를 배치로 임베딩 벡터로 변환
    /// </summary>
    /// <param name="texts">변환할 텍스트 목록</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>임베딩 벡터 목록</returns>
    Task<float[][]> CreateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// 임베딩 벡터의 차원 수
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// 사용 중인 모델명
    /// </summary>
    string ModelName { get; }
}