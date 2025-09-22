using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Domain.Models;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 벡터 인덱스 벤치마킹 서비스 인터페이스
/// </summary>
public interface IVectorIndexBenchmark
{
    /// <summary>
    /// HNSW 인덱스 성능 벤치마크 실행
    /// </summary>
    /// <param name="options">벤치마크 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>벤치마크 결과</returns>
    Task<HnswBenchmarkResult> BenchmarkHnswIndexAsync(
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 HNSW 매개변수 조합 벤치마크
    /// </summary>
    /// <param name="parameterCombinations">매개변수 조합 목록</param>
    /// <param name="options">벤치마크 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>벤치마크 결과 목록</returns>
    Task<IReadOnlyList<HnswBenchmarkResult>> BenchmarkParameterCombinationsAsync(
        IReadOnlyList<HnswParameters> parameterCombinations,
        HnswBenchmarkOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 자동 매개변수 튜닝 실행
    /// </summary>
    /// <param name="tuningOptions">튜닝 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>최적 매개변수</returns>
    Task<HnswParameters> AutoTuneParametersAsync(
        HnswAutoTuningOptions tuningOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 인덱스 성능 메트릭 수집
    /// </summary>
    /// <param name="indexName">인덱스 이름</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>성능 메트릭</returns>
    Task<IndexPerformanceMetrics> CollectPerformanceMetricsAsync(
        string indexName,
        CancellationToken cancellationToken = default);
}

