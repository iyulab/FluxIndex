using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Domain.Models;

namespace FluxIndex.Interfaces;

/// <summary>
/// 쿼리 변환 및 확장 서비스 인터페이스
/// HyDE, QuOTE 등 고급 검색 기법을 통한 쿼리 최적화
/// </summary>
public interface IQueryTransformationService
{
    /// <summary>
    /// HyDE: 가상의 답변 문서를 생성하여 검색 품질 향상
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="options">HyDE 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>가상 문서 변환 결과</returns>
    Task<HyDEResult> GenerateHypotheticalDocumentAsync(
        string query,
        HyDEOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// QuOTE: 질문 지향적 텍스트 임베딩을 위한 쿼리 확장
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="options">QuOTE 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>질문 지향 변환 결과</returns>
    Task<QuOTEResult> GenerateQuestionOrientedEmbeddingAsync(
        string query,
        QuOTEOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 다중 쿼리 생성 (쿼리의 다양한 표현 생성)
    /// </summary>
    /// <param name="query">원본 쿼리</param>
    /// <param name="count">생성할 쿼리 개수</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 쿼리 목록</returns>
    Task<IReadOnlyList<string>> GenerateMultipleQueriesAsync(
        string query,
        int count = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 분해 (복합 쿼리를 단순 쿼리들로 분해)
    /// </summary>
    /// <param name="query">복합 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>분해된 하위 쿼리들</returns>
    Task<QueryDecompositionResult> DecomposeQueryAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 쿼리 의도 분석
    /// </summary>
    /// <param name="query">분석할 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>쿼리 의도 분석 결과</returns>
    Task<QueryIntentResult> AnalyzeQueryIntentAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 서비스 상태 확인
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>서비스 상태</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

