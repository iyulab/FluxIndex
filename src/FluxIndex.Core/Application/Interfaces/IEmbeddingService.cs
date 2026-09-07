using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 임베딩 생성 서비스 인터페이스
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
    int GetEmbeddingDimension();
    string GetModelName();
    int GetMaxTokens();
    Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 이 서비스가 생성하는 임베딩의 Identity를 반환한다.
    /// Provider + Model + Dimension 조합으로 벡터 공간을 고유하게 식별하며,
    /// 파이프라인의 수치 변경으로 같은 모델이 비교 불가능한 벡터를 내게 된 경우
    /// <see cref="EmbeddingIdentity.Revision"/>이 그 구분을 함께 나른다.
    /// </summary>
    EmbeddingIdentity GetIdentity();
}