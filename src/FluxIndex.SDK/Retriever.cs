using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
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

/// <summary>
/// Retriever - 벡터 검색 및 문서 조회 담당 (Phase 3: DX 개선으로 이벤트 및 진행률 모니터링 지원)
/// </summary>
public class Retriever
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly ICacheService? _cacheService;
    private readonly IRankFusionService _rankFusionService;
    private readonly IVectorQuantizer? _vectorQuantizer;
    private readonly ILogger<Retriever> _logger;
    private readonly RetrieverOptions _options;

    // Phase 7.3: In-memory embedding cache for same-query optimization
    private readonly Dictionary<string, float[]> _embeddingCache = new();
    private readonly object _embeddingCacheLock = new();

    // Quantization support
    private readonly IQuantizedVectorStore? _quantizedVectorStore;

    // Phase 3: 이벤트 기반 모니터링
    /// <summary>
    /// 검색 시작 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<SearchStartedEventArgs>? SearchStarted;

    /// <summary>
    /// 검색 완료 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<SearchCompletedEventArgs>? SearchCompleted;

    /// <summary>
    /// 검색 실패 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<SearchFailedEventArgs>? SearchFailed;

    public Retriever(
        IVectorStore vectorStore,
        IDocumentRepository documentRepository,
        IEmbeddingService embeddingService,
        RetrieverOptions options,
        ICacheService? cacheService = null,
        IRankFusionService? rankFusionService = null,
        IVectorQuantizer? vectorQuantizer = null,
        ILogger<Retriever>? logger = null)
    {
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _embeddingService = embeddingService;
        _options = options;
        _cacheService = cacheService;
        _rankFusionService = rankFusionService; // ?? new RankFusionService();
        _vectorQuantizer = vectorQuantizer;
        _logger = logger ?? new NullLogger<Retriever>();

        // Check if vector store supports quantization
        _quantizedVectorStore = vectorStore as IQuantizedVectorStore;
    }

    /// <summary>
    /// 양자화 지원 여부 확인
    /// </summary>
    public bool SupportsQuantization => _quantizedVectorStore?.SupportsQuantization ?? false;

    /// <summary>
    /// 양자화기 인스턴스 (읽기 전용)
    /// </summary>
    public IVectorQuantizer? Quantizer => _vectorQuantizer ?? _quantizedVectorStore?.Quantizer;

    /// <summary>
    /// 벡터 유사도 검색
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        float minScore = 0.2f, // Lowered from 0.5f for better recall
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        return await SearchAsync(query, null, maxResults, minScore, filter, cancellationToken);
    }

    /// <summary>
    /// 벡터 유사도 검색 (Phase 3: 진행률 모니터링 지원)
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> SearchAsync(
        string query,
        IProgress<SearchProgress>? progress,
        int maxResults = 10,
        float minScore = 0.2f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var queryId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            // Phase 3: 이벤트 발생 - 검색 시작
            SearchStarted?.Invoke(this, new SearchStartedEventArgs
            {
                QueryId = queryId,
                Query = query,
                SearchType = "Vector",
                TopK = maxResults,
                StartedAt = startTime
            });

            _logger.LogInformation("Searching for: {Query}", query);

            // Phase 3: 진행률 보고 - 시작 (0%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 0,
                TotalSteps = 5,
                ProgressPercentage = 0,
                Status = "Starting",
                Message = "Checking cache"
            });

            // Check cache first
            if (_cacheService != null)
            {
                var cacheKey = GenerateCacheKey(query, maxResults, minScore, filter);
                var cachedResults = await _cacheService.GetAsync<List<VectorSearchResult>>(cacheKey, cancellationToken);
                if (cachedResults != null)
                {
                    _logger.LogDebug("Cache hit for query: {Query}", query);

                    // Phase 3: 진행률 보고 - 캐시 히트 (100%)
                    progress?.Report(new SearchProgress
                    {
                        QueryId = queryId,
                        Query = query,
                        CurrentStep = 5,
                        TotalSteps = 5,
                        ProgressPercentage = 100,
                        Status = "Completed",
                        Message = "Retrieved from cache",
                        ResultsFound = cachedResults.Count
                    });

                    // Phase 3: 이벤트 발생 - 검색 완료 (캐시)
                    SearchCompleted?.Invoke(this, new SearchCompletedEventArgs
                    {
                        QueryId = queryId,
                        Query = query,
                        SearchType = "Vector",
                        ResultsFound = cachedResults.Count,
                        RequestedTopK = maxResults,
                        ProcessingTime = DateTime.UtcNow - startTime
                    });

                    return cachedResults;
                }
            }

            // Phase 3: 진행률 보고 - 임베딩 생성 (25%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 1,
                TotalSteps = 5,
                ProgressPercentage = 25,
                Status = "Processing",
                Message = "Generating query embedding"
            });

            // Phase 7.3: Check embedding cache first
            float[] queryEmbedding;
            lock (_embeddingCacheLock)
            {
                if (_embeddingCache.TryGetValue(query, out var cachedEmbedding))
                {
                    queryEmbedding = cachedEmbedding;
                    _logger.LogDebug("Embedding cache hit for query: {Query}", query);
                }
                else
                {
                    queryEmbedding = null!;
                }
            }

            // Generate embedding for query if not cached
            if (queryEmbedding == null)
            {
                queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

                // Phase 7.3: Cache the embedding
                lock (_embeddingCacheLock)
                {
                    if (!_embeddingCache.ContainsKey(query))
                    {
                        _embeddingCache[query] = queryEmbedding;
                        _logger.LogDebug("Cached embedding for query: {Query}", query);

                        // Limit cache size to prevent memory issues (keep last 1000 queries)
                        if (_embeddingCache.Count > 1000)
                        {
                            var oldestKey = _embeddingCache.Keys.First();
                            _embeddingCache.Remove(oldestKey);
                        }
                    }
                }
            }

            // Phase 3: 진행률 보고 - 벡터 검색 (50%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 2,
                TotalSteps = 5,
                ProgressPercentage = 50,
                Status = "Searching",
                Message = "Searching vector store"
            });

            // Search in vector store
            var searchResults = await _vectorStore.SearchAsync(
                queryEmbedding,
                maxResults,
                minScore,
                cancellationToken);

            // Convert DocumentChunks to VectorSearchResults
            var results = searchResults.Select(chunk => new VectorSearchResult
            {
                DocumentChunk = chunk, // Use entity directly
                Score = 1.0f, // Default score since IVectorStore doesn't provide it
                Rank = 0,
                Distance = 0,
                Metadata = chunk.Metadata
            }).ToList();

            // Phase 3: 진행률 보고 - 필터링 (75%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 3,
                TotalSteps = 5,
                ProgressPercentage = 75,
                Status = "Filtering",
                Message = "Applying metadata filters",
                ResultsFound = results.Count
            });

            // Apply metadata filter if provided
            if (filter != null && filter.Any())
            {
                results = ApplyFilter(results, filter);
            }

            // Cache results
            if (_cacheService != null && results.Any())
            {
                var cacheKey = GenerateCacheKey(query, maxResults, minScore, filter);
                await _cacheService.SetAsync(cacheKey, results, _options.CacheDuration, cancellationToken);
            }

            // Phase 3: 진행률 보고 - 완료 (100%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = query,
                CurrentStep = 5,
                TotalSteps = 5,
                ProgressPercentage = 100,
                Status = "Completed",
                Message = $"Found {results.Count} results",
                ResultsFound = results.Count
            });

            _logger.LogInformation("Found {Count} results for query: {Query}", results.Count, query);

            // Phase 3: 이벤트 발생 - 검색 완료
            SearchCompleted?.Invoke(this, new SearchCompletedEventArgs
            {
                QueryId = queryId,
                Query = query,
                SearchType = "Vector",
                ResultsFound = results.Count,
                RequestedTopK = maxResults,
                ProcessingTime = DateTime.UtcNow - startTime
            });

            return results;
        }
        catch (Exception ex)
        {
            // Phase 3: 이벤트 발생 - 검색 실패
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
    /// 하이브리드 검색 (키워드 + 벡터) with RRF fusion
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> HybridSearchAsync(
        string keyword,
        string query,
        int maxResults = 10,
        double vectorWeight = 0.7,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        return await HybridSearchAsync(keyword, query, null, maxResults, vectorWeight, filter, cancellationToken);
    }

    /// <summary>
    /// 하이브리드 검색 (키워드 + 벡터) with RRF fusion (Phase 3: 진행률 모니터링 지원)
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> HybridSearchAsync(
        string keyword,
        string query,
        IProgress<SearchProgress>? progress,
        int maxResults = 10,
        double vectorWeight = 0.7,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var queryId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            // Phase 3: 이벤트 발생 - 검색 시작
            SearchStarted?.Invoke(this, new SearchStartedEventArgs
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                SearchType = "Hybrid",
                TopK = maxResults,
                StartedAt = startTime,
                Parameters = new Dictionary<string, object>
                {
                    ["keyword"] = keyword,
                    ["query"] = query,
                    ["vectorWeight"] = vectorWeight
                }
            });

            _logger.LogInformation("Hybrid search - keyword: {Keyword}, query: {Query}", keyword, query);

            // Phase 3: 진행률 보고 - 시작 (0%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                CurrentStep = 0,
                TotalSteps = 4,
                ProgressPercentage = 0,
                Status = "Starting",
                Message = "Starting hybrid search"
            });

            // Phase 3: 진행률 보고 - 벡터 검색 (25%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                CurrentStep = 1,
                TotalSteps = 4,
                ProgressPercentage = 25,
                Status = "Vector Search",
                Message = "Performing vector similarity search"
            });

            // Perform vector search
            var vectorResults = await SearchAsync(query, maxResults * 2, 0, filter, cancellationToken);

            // Phase 3: 진행률 보고 - 키워드 검색 (50%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                CurrentStep = 2,
                TotalSteps = 4,
                ProgressPercentage = 50,
                Status = "Keyword Search",
                Message = "Performing keyword search"
            });

            // Perform keyword search
            var keywordResults = await KeywordSearchAsync(keyword, maxResults * 2, filter, cancellationToken);

            // Phase 3: 진행률 보고 - 결과 통합 (75%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                CurrentStep = 3,
                TotalSteps = 4,
                ProgressPercentage = 75,
                Status = "Fusion",
                Message = "Fusing results with RRF/weighted fusion"
            });

            // Use RRF fusion if weights are equal, otherwise use weighted fusion
            IEnumerable<VectorSearchResult> combinedResults;

            if (Math.Abs(vectorWeight - 0.5) < 0.01) // Equal weights, use RRF
            {
                var resultSets = new Dictionary<string, IEnumerable<RankedResultCore>>
                {
                    ["vector"] = ConvertToRankedResults(vectorResults, "vector"),
                    ["keyword"] = ConvertToRankedResults(keywordResults, "keyword")
                };

                var fusedResults = _rankFusionService.FuseWithRRF(resultSets, k: 60, topN: maxResults);
                combinedResults = ConvertFromRankedResults(fusedResults);
            }
            else // Use weighted fusion
            {
                var resultSets = new Dictionary<string, (IEnumerable<RankedResultCore> results, double weight)>
                {
                    ["vector"] = (ConvertToRankedResults(vectorResults, "vector"), vectorWeight),
                    ["keyword"] = (ConvertToRankedResults(keywordResults, "keyword"), 1.0 - vectorWeight)
                };

                var fusedResults = _rankFusionService.FuseWithWeights(resultSets, topN: maxResults);
                combinedResults = ConvertFromRankedResults(fusedResults);
            }

            var finalResults = combinedResults.ToList();

            // Phase 3: 진행률 보고 - 완료 (100%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                CurrentStep = 4,
                TotalSteps = 4,
                ProgressPercentage = 100,
                Status = "Completed",
                Message = $"Found {finalResults.Count} hybrid results",
                ResultsFound = finalResults.Count
            });

            // Phase 3: 이벤트 발생 - 검색 완료
            SearchCompleted?.Invoke(this, new SearchCompletedEventArgs
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                SearchType = "Hybrid",
                ResultsFound = finalResults.Count,
                RequestedTopK = maxResults,
                ProcessingTime = DateTime.UtcNow - startTime,
                Metadata = new Dictionary<string, object>
                {
                    ["vectorResults"] = vectorResults.Count(),
                    ["keywordResults"] = keywordResults.Count(),
                    ["vectorWeight"] = vectorWeight
                }
            });

            return finalResults;
        }
        catch (Exception ex)
        {
            // Phase 3: 이벤트 발생 - 검색 실패
            SearchFailed?.Invoke(this, new SearchFailedEventArgs
            {
                QueryId = queryId,
                Query = $"Hybrid: {keyword} | {query}",
                ErrorMessage = ex.Message,
                Exception = ex
            });
            throw;
        }
    }

    /// <summary>
    /// 키워드 기반 검색
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> KeywordSearchAsync(
        string keyword,
        int maxResults = 10,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        return await KeywordSearchAsync(keyword, null, maxResults, filter, cancellationToken);
    }

    /// <summary>
    /// 키워드 기반 검색 (Phase 3: 진행률 모니터링 지원)
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> KeywordSearchAsync(
        string keyword,
        IProgress<SearchProgress>? progress,
        int maxResults = 10,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var queryId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            // Phase 3: 이벤트 발생 - 검색 시작
            SearchStarted?.Invoke(this, new SearchStartedEventArgs
            {
                QueryId = queryId,
                Query = keyword,
                SearchType = "Keyword",
                TopK = maxResults,
                StartedAt = startTime
            });

            _logger.LogInformation("Keyword search: {Keyword}", keyword);

            // Phase 3: 진행률 보고 - 시작 (0%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = keyword,
                CurrentStep = 0,
                TotalSteps = 5,
                ProgressPercentage = 0,
                Status = "Starting",
                Message = "Starting keyword search"
            });

            // Phase 3: 진행률 보고 - 문서 검색 (25%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = keyword,
                CurrentStep = 1,
                TotalSteps = 5,
                ProgressPercentage = 25,
                Status = "Searching",
                Message = "Searching document repository"
            });

            // Search in document repository
            var documents = await _documentRepository.SearchByKeywordAsync(keyword, maxResults, cancellationToken);

            // Phase 3: 진행률 보고 - 청크 처리 (50%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = keyword,
                CurrentStep = 2,
                TotalSteps = 5,
                ProgressPercentage = 50,
                Status = "Processing",
                Message = $"Processing {documents.Count()} documents for matching chunks"
            });

            var results = new List<VectorSearchResult>();
            foreach (var doc in documents)
            {
                // Get chunks for each document
                var chunks = await _vectorStore.GetByDocumentIdAsync(doc.Id, cancellationToken);

                // Filter chunks containing keyword
                var matchingChunks = chunks.Where(c =>
                    c.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                results.AddRange(matchingChunks.Select(chunk => new VectorSearchResult
                {
                    DocumentChunk = chunk, // Use entity directly
                    Score = CalculateKeywordScore(chunk.Content, keyword),
                    Rank = 0,
                    Distance = 0,
                    Metadata = doc.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                }));
            }

            // Phase 3: 진행률 보고 - 필터링 (75%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = keyword,
                CurrentStep = 3,
                TotalSteps = 5,
                ProgressPercentage = 75,
                Status = "Filtering",
                Message = "Applying metadata filters and sorting",
                ResultsFound = results.Count
            });

            // Apply filter if provided
            if (filter != null && filter.Any())
            {
                results = ApplyFilter(results, filter);
            }

            var finalResults = results.OrderByDescending(r => r.Score).Take(maxResults).ToList();

            // Phase 3: 진행률 보고 - 완료 (100%)
            progress?.Report(new SearchProgress
            {
                QueryId = queryId,
                Query = keyword,
                CurrentStep = 5,
                TotalSteps = 5,
                ProgressPercentage = 100,
                Status = "Completed",
                Message = $"Found {finalResults.Count} keyword matches",
                ResultsFound = finalResults.Count
            });

            // Phase 3: 이벤트 발생 - 검색 완료
            SearchCompleted?.Invoke(this, new SearchCompletedEventArgs
            {
                QueryId = queryId,
                Query = keyword,
                SearchType = "Keyword",
                ResultsFound = finalResults.Count,
                RequestedTopK = maxResults,
                ProcessingTime = DateTime.UtcNow - startTime
            });

            return finalResults;
        }
        catch (Exception ex)
        {
            // Phase 3: 이벤트 발생 - 검색 실패
            SearchFailed?.Invoke(this, new SearchFailedEventArgs
            {
                QueryId = queryId,
                Query = keyword,
                ErrorMessage = ex.Message,
                Exception = ex
            });
            throw;
        }
    }

    /// <summary>
    /// 문서 ID로 조회
    /// </summary>
    public async Task<Document?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting document: {DocumentId}", documentId);
        
        // Check cache
        if (_cacheService != null)
        {
            var cachedDoc = await _cacheService.GetAsync<Document>($"doc:{documentId}", cancellationToken);
            if (cachedDoc != null)
                return cachedDoc;
        }

        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);

        if (document != null)
        {
            // Get chunks
            var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
            foreach (var chunk in chunks.OrderBy(c => c.ChunkIndex))
            {
                document.AddChunk(chunk);
            }

            // Cache document
            if (_cacheService != null)
            {
                await _cacheService.SetAsync($"doc:{documentId}", document, _options.CacheDuration, cancellationToken);
            }
        }

        return document;
    }

    /// <summary>
    /// 청크 ID로 조회
    /// </summary>
    public async Task<DocumentChunkEntity?> GetChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting chunk: {ChunkId}", chunkId);
        return await _vectorStore.GetByIdAsync(chunkId, cancellationToken);
    }

    /// <summary>
    /// 유사 문서 찾기
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> FindSimilarAsync(
        string documentId,
        int maxResults = 10,
        float minScore = 0.5f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Finding similar documents to: {DocumentId}", documentId);

        // Get document chunks
        var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        if (!chunks.Any())
            return Enumerable.Empty<VectorSearchResult>();

        // Use first chunk's embedding for similarity search
        var firstChunk = chunks.First();
        if (firstChunk.Embedding == null)
            return Enumerable.Empty<VectorSearchResult>();

        // Search for similar chunks
        var similarDocumentChunks = await _vectorStore.SearchAsync(
            firstChunk.Embedding,
            maxResults + chunks.Count(), // Get extra to filter out same document
            minScore,
            cancellationToken);

        // Convert to VectorSearchResult and filter out chunks from the same document
        var results = similarDocumentChunks
            .Where(c => c.DocumentId != documentId)
            .Take(maxResults)
            .Select(chunk => new VectorSearchResult
            {
                DocumentChunk = chunk, // Use entity directly
                Score = 1.0f, // Default score
                Rank = 0,
                Distance = 0,
                Metadata = chunk.Metadata
            });

        return results;
    }

    /// <summary>
    /// 통계 정보 조회
    /// </summary>
    public async Task<RetrievalStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var docCount = await _documentRepository.GetCountAsync(cancellationToken);
        var chunkCount = await _vectorStore.CountAsync(cancellationToken);

        return new RetrievalStatistics
        {
            TotalDocuments = docCount,
            TotalChunks = chunkCount,
            AverageChunksPerDocument = docCount > 0 ? (double)chunkCount / docCount : 0,
            CacheEnabled = _cacheService != null,
            VectorStoreProvider = _vectorStore.GetType().Name
        };
    }

    private List<VectorSearchResult> ApplyFilter(List<VectorSearchResult> results, Dictionary<string, object> filter)
    {
        return results.Where(r =>
        {
            if (r.Metadata == null) return false;

            foreach (var kvp in filter)
            {
                if (!r.Metadata.ContainsKey(kvp.Key) ||
                    !r.Metadata[kvp.Key].Equals(kvp.Value))
                {
                    return false;
                }
            }
            return true;
        }).ToList();
    }

    private float CalculateKeywordScore(string content, string keyword)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += keyword.Length;
        }
        
        // Simple TF score
        return (float)count / content.Split(' ').Length;
    }

    private IEnumerable<VectorSearchResult> CombineResults(
        IEnumerable<VectorSearchResult> vectorResults,
        IEnumerable<VectorSearchResult> keywordResults,
        double vectorWeight)
    {
        var keywordWeight = 1.0 - vectorWeight;
        var combined = new Dictionary<string, VectorSearchResult>();

        // Add vector results
        foreach (var result in vectorResults)
        {
            var key = $"{result.DocumentChunk.DocumentId}:{result.DocumentChunk.Id}";
            combined[key] = result;
        }

        // Combine with keyword results
        foreach (var result in keywordResults)
        {
            var key = $"{result.DocumentChunk.DocumentId}:{result.DocumentChunk.Id}";
            if (combined.ContainsKey(key))
            {
                // Create new result with combined score
                combined[key] = new VectorSearchResult
                {
                    DocumentChunk = combined[key].DocumentChunk,
                    Score = combined[key].Score * vectorWeight + result.Score * keywordWeight,
                    Rank = combined[key].Rank,
                    Distance = combined[key].Distance,
                    Metadata = combined[key].Metadata
                };
            }
            else
            {
                combined[key] = new VectorSearchResult
                {
                    DocumentChunk = result.DocumentChunk,
                    Score = result.Score * keywordWeight,
                    Rank = result.Rank,
                    Distance = result.Distance,
                    Metadata = result.Metadata
                };
            }
        }

        return combined.Values.OrderByDescending(r => r.Score);
    }

    private string GenerateCacheKey(string query, int maxResults, float minScore, Dictionary<string, object>? filter)
    {
        var filterStr = filter != null ? string.Join(",", filter.Select(kvp => $"{kvp.Key}:{kvp.Value}")) : "";
        return $"search:{query}:{maxResults}:{minScore}:{filterStr}";
    }

    private DocumentChunkModel ConvertToModelChunk(DocumentChunkEntity entityChunk)
    {
        var modelChunk = DocumentChunkModel.Create(
            entityChunk.DocumentId,
            entityChunk.Content,
            entityChunk.ChunkIndex,
            entityChunk.TotalChunks,
            entityChunk.Embedding,
            0f, // score
            entityChunk.TokenCount,
            entityChunk.Metadata
        );

        // Preserve the Id from the entity
        return new DocumentChunkModel
        {
            Id = entityChunk.Id,
            DocumentId = modelChunk.DocumentId,
            Content = modelChunk.Content,
            ChunkIndex = modelChunk.ChunkIndex,
            TotalChunks = modelChunk.TotalChunks,
            Embedding = modelChunk.Embedding,
            Score = modelChunk.Score,
            TokenCount = modelChunk.TokenCount,
            Metadata = modelChunk.Metadata,
            CreatedAt = modelChunk.CreatedAt
        };
    }

    private IEnumerable<RankedResultCore> ConvertToRankedResults(IEnumerable<VectorSearchResult> searchResults, string source)
    {
        return searchResults.Select((r, index) => new RankedResultCore
        {
            Id = r.DocumentChunk.Id,
            DocumentId = r.DocumentChunk.DocumentId,
            ChunkId = r.DocumentChunk.Id,
            Content = r.DocumentChunk.Content,
            Score = (float)r.Score,
            Rank = index + 1,
            Source = source,
            Metadata = r.Metadata
        });
    }

    private IEnumerable<VectorSearchResult> ConvertFromRankedResults(IEnumerable<RankedResultCore> rankedResults)
    {
        return rankedResults.Select(r => new VectorSearchResult
        {
            DocumentChunk = new DocumentChunkEntity  // Use entity directly
            {
                Id = r.ChunkId,
                DocumentId = r.DocumentId,
                Content = r.Content,
                ChunkIndex = 0,
                TokenCount = 0,
                Metadata = r.Metadata ?? new Dictionary<string, object>()
            },
            Score = (float)r.Score,
            Rank = r.Rank,
            Distance = 0,
            Metadata = r.Metadata ?? new Dictionary<string, object>()
        });
    }

    #region Quantized Search Methods

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

            _logger.LogInformation("Quantized search for: {Query}", query);

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
                Metadata = r.Chunk.Metadata
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

            _logger.LogInformation("Quantized search with rerank for: {Query}", query);

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
                Metadata = r.Chunk.Metadata
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
                _logger.LogDebug("Embedding cache hit for query: {Query}", query);
                return cachedEmbedding;
            }
        }

        var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        lock (_embeddingCacheLock)
        {
            if (!_embeddingCache.ContainsKey(query))
            {
                _embeddingCache[query] = embedding;
                _logger.LogDebug("Cached embedding for query: {Query}", query);

                if (_embeddingCache.Count > 1000)
                {
                    var oldestKey = _embeddingCache.Keys.First();
                    _embeddingCache.Remove(oldestKey);
                }
            }
        }

        return embedding;
    }

    #endregion
}


/// <summary>
/// Retriever 옵션
/// </summary>
public class RetrieverOptions
{
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);
    public int DefaultMaxResults { get; set; } = 10;
    public float DefaultMinScore { get; set; } = 0.5f;
}

/// <summary>
/// 통계 정보
/// </summary>
public class RetrievalStatistics
{
    public int TotalDocuments { get; set; }
    public int TotalChunks { get; set; }
    public double AverageChunksPerDocument { get; set; }
    public bool CacheEnabled { get; set; }
    public string VectorStoreProvider { get; set; } = string.Empty;
}