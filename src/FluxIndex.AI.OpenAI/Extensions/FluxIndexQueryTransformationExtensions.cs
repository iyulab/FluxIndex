using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Options;
using FluxIndex.Core.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.AI.OpenAI.Extensions;

/// <summary>
/// FluxIndexClient를 위한 쿼리 변환 확장 메서드
/// HyDE, QuOTE 등을 활용한 고급 검색 기능 제공
/// </summary>
public static class FluxIndexQueryTransformationExtensions
{
    /// <summary>
    /// HyDE를 사용한 검색 (가상 문서 기반)
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="query">검색 쿼리</param>
    /// <param name="hydeOptions">HyDE 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>검색 결과</returns>
    public static async Task<IReadOnlyList<DocumentChunk>> SearchWithHyDEAsync(
        this FluxIndexClient client,
        string query,
        HyDEOptions? hydeOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var queryTransformationService = GetQueryTransformationService(client);
        if (queryTransformationService == null)
        {
            // 쿼리 변환 서비스가 없으면 일반 검색으로 폴백
            return await client.Retriever.SearchAsync(query, cancellationToken);
        }

        try
        {
            // HyDE로 가상 문서 생성
            var hydeResult = await queryTransformationService.GenerateHypotheticalDocumentAsync(
                query, hydeOptions, cancellationToken);

            if (hydeResult.IsSuccessful)
            {
                // 가상 문서로 검색
                var results = await client.Retriever.SearchAsync(hydeResult.HypotheticalDocument, cancellationToken);

                // 원본 쿼리로도 검색하여 결합
                var originalResults = await client.Retriever.SearchAsync(query, cancellationToken);

                // 결과 병합 및 중복 제거
                return MergeAndDeduplicateResults(results, originalResults);
            }
            else
            {
                // HyDE 실패 시 원본 쿼리로 검색
                return await client.Retriever.SearchAsync(query, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            var logger = GetLogger<FluxIndexClient>(client);
            logger?.LogWarning(ex, "HyDE search failed for query '{Query}', falling back to normal search", query);

            // 예외 발생 시 일반 검색으로 폴백
            return await client.Retriever.SearchAsync(query, cancellationToken);
        }
    }

    /// <summary>
    /// QuOTE를 사용한 검색 (질문 지향 확장)
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="query">검색 쿼리</param>
    /// <param name="quoteOptions">QuOTE 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>검색 결과</returns>
    public static async Task<IReadOnlyList<DocumentChunk>> SearchWithQuOTEAsync(
        this FluxIndexClient client,
        string query,
        QuOTEOptions? quoteOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var queryTransformationService = GetQueryTransformationService(client);
        if (queryTransformationService == null)
        {
            return await client.Retriever.SearchAsync(query, cancellationToken);
        }

        try
        {
            // QuOTE로 쿼리 확장
            var quoteResult = await queryTransformationService.GenerateQuestionOrientedEmbeddingAsync(
                query, quoteOptions, cancellationToken);

            if (quoteResult.IsSuccessful)
            {
                var allResults = new List<DocumentChunk>();

                // 원본 쿼리로 검색
                var originalResults = await client.Retriever.SearchAsync(query, cancellationToken);
                allResults.AddRange(originalResults);

                // 확장된 쿼리들로 검색
                foreach (var expandedQuery in quoteResult.ExpandedQueries)
                {
                    var expandedResults = await client.Retriever.SearchAsync(expandedQuery, cancellationToken);
                    allResults.AddRange(expandedResults);
                }

                // 가중치를 적용한 결과 병합
                return ApplyWeightedMerging(allResults, quoteResult.QueryWeights, query);
            }
            else
            {
                return await client.Retriever.SearchAsync(query, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            var logger = GetLogger<FluxIndexClient>(client);
            logger?.LogWarning(ex, "QuOTE search failed for query '{Query}', falling back to normal search", query);

            return await client.Retriever.SearchAsync(query, cancellationToken);
        }
    }

    /// <summary>
    /// 다중 쿼리를 사용한 검색 (여러 표현으로 검색)
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="query">검색 쿼리</param>
    /// <param name="queryCount">생성할 쿼리 개수</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>검색 결과</returns>
    public static async Task<IReadOnlyList<DocumentChunk>> SearchWithMultipleQueriesAsync(
        this FluxIndexClient client,
        string query,
        int queryCount = 3,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var queryTransformationService = GetQueryTransformationService(client);
        if (queryTransformationService == null)
        {
            return await client.Retriever.SearchAsync(query, cancellationToken);
        }

        try
        {
            // 다중 쿼리 생성
            var multipleQueries = await queryTransformationService.GenerateMultipleQueriesAsync(
                query, queryCount, cancellationToken);

            if (multipleQueries.Count == 0)
            {
                return await client.Retriever.SearchAsync(query, cancellationToken);
            }

            var allResults = new List<DocumentChunk>();

            // 원본 쿼리로 검색
            var originalResults = await client.Retriever.SearchAsync(query, cancellationToken);
            allResults.AddRange(originalResults);

            // 생성된 쿼리들로 검색
            foreach (var generatedQuery in multipleQueries)
            {
                var results = await client.Retriever.SearchAsync(generatedQuery, cancellationToken);
                allResults.AddRange(results);
            }

            // 결과 병합 및 중복 제거
            return MergeAndDeduplicateResults(allResults);
        }
        catch (Exception ex)
        {
            var logger = GetLogger<FluxIndexClient>(client);
            logger?.LogWarning(ex, "Multiple queries search failed for query '{Query}', falling back to normal search", query);

            return await client.Retriever.SearchAsync(query, cancellationToken);
        }
    }

    /// <summary>
    /// 하이브리드 검색 (HyDE + QuOTE 조합)
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="query">검색 쿼리</param>
    /// <param name="hydeOptions">HyDE 옵션</param>
    /// <param name="quoteOptions">QuOTE 옵션</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>검색 결과</returns>
    public static async Task<IReadOnlyList<DocumentChunk>> SearchHybridAsync(
        this FluxIndexClient client,
        string query,
        HyDEOptions? hydeOptions = null,
        QuOTEOptions? quoteOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var queryTransformationService = GetQueryTransformationService(client);
        if (queryTransformationService == null)
        {
            return await client.Retriever.SearchAsync(query, cancellationToken);
        }

        try
        {
            var allResults = new List<DocumentChunk>();

            // 1. 원본 쿼리로 검색
            var originalResults = await client.Retriever.SearchAsync(query, cancellationToken);
            allResults.AddRange(originalResults);

            // 2. HyDE 검색
            var hydeTask = queryTransformationService.GenerateHypotheticalDocumentAsync(
                query, hydeOptions, cancellationToken);

            // 3. QuOTE 검색
            var quoteTask = queryTransformationService.GenerateQuestionOrientedEmbeddingAsync(
                query, quoteOptions, cancellationToken);

            // 병렬 실행
            await Task.WhenAll(hydeTask, quoteTask);

            // HyDE 결과 처리
            var hydeResult = await hydeTask;
            if (hydeResult.IsSuccessful)
            {
                var hydeResults = await client.Retriever.SearchAsync(hydeResult.HypotheticalDocument, cancellationToken);
                allResults.AddRange(hydeResults);
            }

            // QuOTE 결과 처리
            var quoteResult = await quoteTask;
            if (quoteResult.IsSuccessful)
            {
                foreach (var expandedQuery in quoteResult.ExpandedQueries)
                {
                    var expandedResults = await client.Retriever.SearchAsync(expandedQuery, cancellationToken);
                    allResults.AddRange(expandedResults);
                }
            }

            // 종합적인 결과 병합
            return MergeAndDeduplicateResults(allResults);
        }
        catch (Exception ex)
        {
            var logger = GetLogger<FluxIndexClient>(client);
            logger?.LogWarning(ex, "Hybrid search failed for query '{Query}', falling back to normal search", query);

            return await client.Retriever.SearchAsync(query, cancellationToken);
        }
    }

    /// <summary>
    /// 쿼리 분해 기반 검색 (복합 쿼리를 단계별로 처리)
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="query">복합 검색 쿼리</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>검색 결과</returns>
    public static async Task<IReadOnlyList<DocumentChunk>> SearchWithDecompositionAsync(
        this FluxIndexClient client,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        var queryTransformationService = GetQueryTransformationService(client);
        if (queryTransformationService == null)
        {
            return await client.Retriever.SearchAsync(query, cancellationToken);
        }

        try
        {
            // 쿼리 분해
            var decompositionResult = await queryTransformationService.DecomposeQueryAsync(query, cancellationToken);

            if (!decompositionResult.IsSuccessful)
            {
                return await client.Retriever.SearchAsync(query, cancellationToken);
            }

            var allResults = new List<DocumentChunk>();

            // 원본 쿼리로 검색
            var originalResults = await client.Retriever.SearchAsync(query, cancellationToken);
            allResults.AddRange(originalResults);

            // 하위 쿼리들로 검색
            foreach (var subQuery in decompositionResult.SubQueries)
            {
                var subResults = await client.Retriever.SearchAsync(subQuery.Text, cancellationToken);

                // 중요도에 따른 가중치 적용 (Score 조정)
                foreach (var result in subResults)
                {
                    if (result.Score.HasValue)
                    {
                        result.Score = result.Score.Value * subQuery.Importance;
                    }
                }

                allResults.AddRange(subResults);
            }

            // 관계 타입에 따른 결과 처리
            return ProcessDecomposedResults(allResults, decompositionResult.Relationship);
        }
        catch (Exception ex)
        {
            var logger = GetLogger<FluxIndexClient>(client);
            logger?.LogWarning(ex, "Decomposition search failed for query '{Query}', falling back to normal search", query);

            return await client.Retriever.SearchAsync(query, cancellationToken);
        }
    }

    /// <summary>
    /// 쿼리 변환 서비스 상태 확인
    /// </summary>
    /// <param name="client">FluxIndex 클라이언트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>서비스 상태</returns>
    public static async Task<bool> IsQueryTransformationHealthyAsync(
        this FluxIndexClient client,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        var queryTransformationService = GetQueryTransformationService(client);
        if (queryTransformationService == null)
            return false;

        return await queryTransformationService.IsHealthyAsync(cancellationToken);
    }

    /// <summary>
    /// 결과 병합 및 중복 제거
    /// </summary>
    private static IReadOnlyList<DocumentChunk> MergeAndDeduplicateResults(
        params IReadOnlyList<DocumentChunk>[] resultSets)
    {
        var allResults = resultSets.SelectMany(results => results).ToList();
        return MergeAndDeduplicateResults(allResults);
    }

    /// <summary>
    /// 결과 병합 및 중복 제거 (단일 리스트)
    /// </summary>
    private static IReadOnlyList<DocumentChunk> MergeAndDeduplicateResults(List<DocumentChunk> results)
    {
        // ID 기준으로 중복 제거하면서 최고 점수 유지
        var uniqueResults = results
            .GroupBy(chunk => chunk.Id)
            .Select(group => group.OrderByDescending(chunk => chunk.Score ?? 0).First())
            .OrderByDescending(chunk => chunk.Score ?? 0)
            .ToList();

        return uniqueResults;
    }

    /// <summary>
    /// 가중치 적용 결과 병합
    /// </summary>
    private static IReadOnlyList<DocumentChunk> ApplyWeightedMerging(
        List<DocumentChunk> results,
        IReadOnlyDictionary<string, float> queryWeights,
        string originalQuery)
    {
        // 가중치 적용 로직 구현
        // 현재는 단순 병합, 실제로는 각 쿼리의 가중치를 점수에 반영
        return MergeAndDeduplicateResults(results);
    }

    /// <summary>
    /// 분해된 결과 처리
    /// </summary>
    private static IReadOnlyList<DocumentChunk> ProcessDecomposedResults(
        List<DocumentChunk> results,
        QueryRelationshipType relationship)
    {
        // 관계 타입에 따른 처리 로직
        return relationship switch
        {
            QueryRelationshipType.Conjunction => results.Where(r => (r.Score ?? 0) > 0.7f).ToList(), // AND 조건
            QueryRelationshipType.Disjunction => MergeAndDeduplicateResults(results), // OR 조건
            QueryRelationshipType.Sequential => results.OrderBy(r => r.ChunkIndex).ToList(), // 순서 유지
            _ => MergeAndDeduplicateResults(results) // 기본 처리
        };
    }

    /// <summary>
    /// FluxIndexClient에서 쿼리 변환 서비스 추출
    /// </summary>
    private static IQueryTransformationService? GetQueryTransformationService(FluxIndexClient client)
    {
        var serviceProvider = GetServiceProvider(client);
        return serviceProvider?.GetService<IQueryTransformationService>();
    }

    /// <summary>
    /// FluxIndexClient에서 서비스 프로바이더 추출
    /// </summary>
    private static IServiceProvider? GetServiceProvider(FluxIndexClient client)
    {
        // FluxIndexClient의 내부 서비스 프로바이더에 접근
        var clientType = client.GetType();
        var serviceProviderField = clientType.GetField("_serviceProvider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return serviceProviderField?.GetValue(client) as IServiceProvider;
    }

    /// <summary>
    /// 로거 추출
    /// </summary>
    private static ILogger<T>? GetLogger<T>(FluxIndexClient client)
    {
        var serviceProvider = GetServiceProvider(client);
        return serviceProvider?.GetService<ILogger<T>>();
    }
}