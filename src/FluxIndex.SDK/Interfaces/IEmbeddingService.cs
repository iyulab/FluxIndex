using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Interfaces;

/// <summary>
/// 임베딩 생성 서비스 인터페이스
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// 텍스트에서 임베딩 벡터 생성
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 여러 텍스트에서 임베딩 벡터 일괄 생성
    /// </summary>
    Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 임베딩 차원 수 조회
    /// </summary>
    int GetEmbeddingDimension();
    
    /// <summary>
    /// 모델 정보 조회
    /// </summary>
    EmbeddingModelInfo GetModelInfo();
    
    /// <summary>
    /// 텍스트 토큰 수 계산
    /// </summary>
    Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 최대 토큰 수 조회
    /// </summary>
    int GetMaxTokens();
}