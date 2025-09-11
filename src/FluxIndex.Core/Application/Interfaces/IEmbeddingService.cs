using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 임베딩 생성 서비스 인터페이스
/// </summary>
public interface IEmbeddingService
{
    Task<EmbeddingVector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IEnumerable<EmbeddingVector>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
    int GetEmbeddingDimension();
    string GetModelName();
    int GetMaxTokens();
    Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default);
}