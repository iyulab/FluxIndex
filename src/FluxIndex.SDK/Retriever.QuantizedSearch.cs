using FluxGuard.Remote.RAG;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services.Base;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using DocumentChunkEntity = FluxIndex.Core.Domain.Entities.DocumentChunk;
using DocumentChunkModel = FluxIndex.Core.Domain.Models.CacheDocumentChunk;
using RankedResultCore = FluxIndex.Core.Domain.Models.RankedResult;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK;

public partial class Retriever
{

    /// <summary>
    /// 양자화 벡터를 사용한 빠른 근사 검색
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="maxResults">최대 결과 수</param>
    /// <param name="minScore">최소 유사도 점수</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>검색 결과 목록</returns>
    /// <exception cref="InvalidOperationException">양자화가 지원되지 않는 경우</exception>
    public async Task<IEnumerable<VectorSearchResult>> SearchQuantizedAsync(
        string query,
        int maxResults = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        return await SearchQuantizedAsync(query, null, maxResults, minScore, cancellationToken);
    }

    /// <summary>
    /// 양자화 벡터를 사용한 빠른 근사 검색 (진행률 모니터링 지원)
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> SearchQuantizedAsync(
        string query,
        IProgress<SearchProgress>? progress,
        int maxResults = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (_quantizedVectorStore == null || !SupportsQuantization)
        {
            throw new InvalidOperationException(
                "Quantized search is not supported. Use a vector store that implements IQuantizedVectorStore.");
        }

        var quantizer = Quantizer ?? throw new InvalidOperationException(
            "No quantizer available. Configure a IVectorQuantizer for quantized search.");

        var queryId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            SearchStarted?.Invoke(this, new SearchStartedEventArgs
            {
                QueryId = queryId,
                Query = query,
                SearchType = "QuantizedVector",
                TopK = maxResults,
                StartedAt = startTime
            });

            LogQuantizedSearch(_logger, query);

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 0,
                TotalSteps = 4,
                ProgressPercentage = 0,
                Status = "Starting",
                Message = "Generating query embedding"
            });

            // Generate query embedding
            var queryEmbedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 1,
                TotalSteps = 4,
                ProgressPercentage = 25,
                Status = "Quantizing",
                Message = "Quantizing query embedding"
            });

            // Quantize query
            var quantizedQuery = await quantizer.QuantizeAsync(queryEmbedding, cancellationToken);

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 2,
                TotalSteps = 4,
                ProgressPercentage = 50,
                Status = "Searching",
                Message = "Searching with quantized vectors"
            });

            // Search with quantized vectors
            var results = await _quantizedVectorStore.SearchQuantizedAsync(
                quantizedQuery, maxResults, minScore, cancellationToken);

            var searchResults = results.Select((r, index) => new VectorSearchResult
            {
                DocumentChunk = r.Chunk,
                Score = r.Score,
                Rank = index + 1,
                Distance = 1 - r.Score,
                Metadata = r.Chunk.Metadata ?? new()
            }).ToList();

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 4,
                TotalSteps = 4,
                ProgressPercentage = 100,
                Status = "Completed",
                Message = $"Found {searchResults.Count} results",
                ResultsFound = searchResults.Count
            });

            SearchCompleted?.Invoke(this, new SearchCompletedEventArgs
            {
                QueryId = queryId,
                Query = query,
                SearchType = "QuantizedVector",
                ResultsFound = searchResults.Count,
                RequestedTopK = maxResults,
                ProcessingTime = DateTime.UtcNow - startTime
            });

            return searchResults;
        }
        catch (Exception ex)
        {
            SearchFailed?.Invoke(this, new SearchFailedEventArgs
            {
                QueryId = queryId,
                Query = query,
                ErrorMessage = ex.Message,
                Exception = ex
            });
            throw;
        }
    }

    /// <summary>
    /// 양자화 후보 선택 + 원본 벡터 리랭킹 검색
    /// 양자화로 빠르게 후보군을 선택한 후, 원본 벡터로 정확하게 리랭킹
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="maxResults">최대 결과 수</param>
    /// <param name="candidateMultiplier">후보군 배수 (기본 3배)</param>
    /// <param name="minScore">최소 유사도 점수</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>리랭킹된 검색 결과</returns>
    public async Task<IEnumerable<VectorSearchResult>> SearchWithRerankAsync(
        string query,
        int maxResults = 10,
        int candidateMultiplier = 3,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        return await SearchWithRerankAsync(query, null, maxResults, candidateMultiplier, minScore, cancellationToken);
    }

    /// <summary>
    /// 양자화 후보 선택 + 원본 벡터 리랭킹 검색 (진행률 모니터링 지원)
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> SearchWithRerankAsync(
        string query,
        IProgress<SearchProgress>? progress,
        int maxResults = 10,
        int candidateMultiplier = 3,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        if (_quantizedVectorStore == null || !SupportsQuantization)
        {
            throw new InvalidOperationException(
                "Quantized search with rerank is not supported. Use a vector store that implements IQuantizedVectorStore.");
        }

        var quantizer = Quantizer ?? throw new InvalidOperationException(
            "No quantizer available. Configure a IVectorQuantizer for quantized search.");

        var queryId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            SearchStarted?.Invoke(this, new SearchStartedEventArgs
            {
                QueryId = queryId,
                Query = query,
                SearchType = "QuantizedWithRerank",
                TopK = maxResults,
                StartedAt = startTime,
                Parameters = new Dictionary<string, object>
                {
                    ["candidateMultiplier"] = candidateMultiplier
                }
            });

            LogQuantizedSearchWithRerank(_logger, query);

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 0,
                TotalSteps = 5,
                ProgressPercentage = 0,
                Status = "Starting",
                Message = "Generating query embedding"
            });

            // Generate query embedding
            var queryEmbedding = await GetOrCreateEmbeddingAsync(query, cancellationToken);

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 1,
                TotalSteps = 5,
                ProgressPercentage = 20,
                Status = "Quantizing",
                Message = "Quantizing query embedding"
            });

            // Quantize query
            var quantizedQuery = await quantizer.QuantizeAsync(queryEmbedding, cancellationToken);

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 2,
                TotalSteps = 5,
                ProgressPercentage = 40,
                Status = "Searching",
                Message = $"Searching with quantized vectors (candidates: {maxResults * candidateMultiplier})"
            });

            // Search with rerank
            var results = await _quantizedVectorStore.SearchWithRerankAsync(
                queryEmbedding, quantizedQuery, maxResults, candidateMultiplier, minScore, cancellationToken);

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 4,
                TotalSteps = 5,
                ProgressPercentage = 80,
                Status = "Reranking",
                Message = "Reranking with original vectors"
            });

            var searchResults = results.Select((r, index) => new VectorSearchResult
            {
                DocumentChunk = r.Chunk,
                Score = r.Score,
                Rank = index + 1,
                Distance = 1 - r.Score,
                Metadata = r.Chunk.Metadata ?? new()
            }).ToList();

            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 5,
                TotalSteps = 5,
                ProgressPercentage = 100,
                Status = "Completed",
                Message = $"Found {searchResults.Count} results",
                ResultsFound = searchResults.Count
            });

            SearchCompleted?.Invoke(this, new SearchCompletedEventArgs
            {
                QueryId = queryId,
                Query = query,
                SearchType = "QuantizedWithRerank",
                ResultsFound = searchResults.Count,
                RequestedTopK = maxResults,
                ProcessingTime = DateTime.UtcNow - startTime,
                Metadata = new Dictionary<string, object>
                {
                    ["candidateMultiplier"] = candidateMultiplier,
                    ["rerankingApplied"] = true
                }
            });

            return searchResults;
        }
        catch (Exception ex)
        {
            SearchFailed?.Invoke(this, new SearchFailedEventArgs
            {
                QueryId = queryId,
                Query = query,
                ErrorMessage = ex.Message,
                Exception = ex
            });
            throw;
        }
    }

    /// <summary>
    /// 양자화 저장소 통계 조회
    /// </summary>
    public async Task<QuantizedStorageStats?> GetQuantizedStatsAsync(CancellationToken cancellationToken = default)
    {
        if (_quantizedVectorStore == null || !SupportsQuantization)
        {
            return null;
        }

        return await _quantizedVectorStore.GetQuantizedStatsAsync(cancellationToken);
    }

    /// <summary>
    /// 쿼리 임베딩을 가져오거나 생성
    /// </summary>
    private async Task<float[]> GetOrCreateEmbeddingAsync(string query, CancellationToken cancellationToken)
    {
        lock (_embeddingCacheLock)
        {
            if (_embeddingCache.TryGetValue(query, out var cachedEmbedding))
            {
                LogEmbeddingCacheHit(_logger, query);
                return cachedEmbedding;
            }
        }

        var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        lock (_embeddingCacheLock)
        {
            if (!_embeddingCache.ContainsKey(query))
            {
                _embeddingCache[query] = embedding;
                LogCachedEmbedding(_logger, query);

                if (_embeddingCache.Count > 1000)
                {
                    var oldestKey = _embeddingCache.Keys.First();
                    _embeddingCache.Remove(oldestKey);
                }
            }
        }

        return embedding;
    }

}
