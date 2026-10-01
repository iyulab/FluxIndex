using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 임베딩 생성 서비스 인터페이스
/// </summary>
/// <remarks>
/// <para>
/// Two roles reach an embedder: the text that is <b>stored</b> (documents, chunks, summaries —
/// <see cref="GenerateEmbeddingAsync"/> and <see cref="GenerateEmbeddingsBatchAsync"/>) and the <b>search query</b>
/// compared against it (<see cref="GenerateQueryEmbeddingAsync"/>). FluxIndex calls the query method wherever it
/// embeds a query to search stored vectors.
/// </para>
/// <para>
/// A symmetric model embeds both the same way and needs nothing: the query method defaults to
/// <see cref="GenerateEmbeddingAsync"/>. An asymmetric model (E5 <c>query: </c>/<c>passage: </c>, Qwen3-Embedding's
/// query instruction, BGE's query instruction) overrides it to apply its query convention. Only change the query
/// side when the stored side already follows the model's document convention — a query prefix against documents
/// embedded without theirs mixes two conventions. A service that wraps another one forwards both methods.
/// </para>
/// </remarks>
public interface IEmbeddingService
{
    /// <summary>Embeds text that is stored and searched against (a document, chunk or summary).</summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embeds several stored texts; same role as <see cref="GenerateEmbeddingAsync"/>.</summary>
    Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Embeds a search query, to be compared against vectors from <see cref="GenerateEmbeddingAsync"/>.
    /// Defaults to <see cref="GenerateEmbeddingAsync"/> (a symmetric model); an asymmetric model overrides it.
    /// </summary>
    Task<float[]> GenerateQueryEmbeddingAsync(string query, CancellationToken cancellationToken = default) =>
        GenerateEmbeddingAsync(query, cancellationToken);

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