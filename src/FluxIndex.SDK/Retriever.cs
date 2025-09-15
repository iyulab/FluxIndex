using FluxIndex.Application.Interfaces;
using FluxIndex.Application.Services;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreSearchResult = FluxIndex.Application.Interfaces.SearchResult;

namespace FluxIndex.SDK;

/// <summary>
/// Retriever - 벡터 검색 및 문서 조회 담당
/// </summary>
public class Retriever
{
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly ICacheService? _cacheService;
    private readonly IRankFusionService _rankFusionService;
    private readonly ILogger<Retriever> _logger;
    private readonly RetrieverOptions _options;

    public Retriever(
        IVectorStore vectorStore,
        IDocumentRepository documentRepository,
        IEmbeddingService embeddingService,
        RetrieverOptions options,
        ICacheService? cacheService = null,
        IRankFusionService? rankFusionService = null,
        ILogger<Retriever>? logger = null)
    {
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _embeddingService = embeddingService;
        _options = options;
        _cacheService = cacheService;
        _rankFusionService = rankFusionService ?? new RankFusionService();
        _logger = logger ?? new NullLogger<Retriever>();
    }

    /// <summary>
    /// 벡터 유사도 검색
    /// </summary>
    public async Task<IEnumerable<CoreSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        float minScore = 0.5f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching for: {Query}", query);

        // Check cache first
        if (_cacheService != null)
        {
            var cacheKey = GenerateCacheKey(query, maxResults, minScore, filter);
            var cachedResults = await _cacheService.GetAsync<List<CoreSearchResult>>(cacheKey, cancellationToken);
            if (cachedResults != null)
            {
                _logger.LogDebug("Cache hit for query: {Query}", query);
                return cachedResults;
            }
        }

        // Generate embedding for query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Search in vector store
        var chunks = await _vectorStore.SearchAsync(
            queryEmbedding,
            maxResults,
            minScore,
            cancellationToken);

        // Convert to search results
        var results = chunks.Select(chunk => new CoreSearchResult
        {
            Chunk = chunk,
            Score = chunk.Score ?? 0,
            FileName = chunk.DocumentId,
            Metadata = new FluxIndex.Domain.Entities.DocumentMetadata()
        }).ToList();

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

        _logger.LogInformation("Found {Count} results for query: {Query}", results.Count, query);
        return results;
    }

    /// <summary>
    /// 하이브리드 검색 (키워드 + 벡터) with RRF fusion
    /// </summary>
    public async Task<IEnumerable<CoreSearchResult>> HybridSearchAsync(
        string keyword,
        string query,
        int maxResults = 10,
        float vectorWeight = 0.7f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Hybrid search - keyword: {Keyword}, query: {Query}", keyword, query);

        // Perform vector search
        var vectorResults = await SearchAsync(query, maxResults * 2, 0, filter, cancellationToken);

        // Perform keyword search
        var keywordResults = await KeywordSearchAsync(keyword, maxResults * 2, filter, cancellationToken);

        // Use RRF fusion if weights are equal, otherwise use weighted fusion
        IEnumerable<CoreSearchResult> combinedResults;
        
        if (Math.Abs(vectorWeight - 0.5f) < 0.01f) // Equal weights, use RRF
        {
            var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
            {
                ["vector"] = ConvertToRankedResults(vectorResults, "vector"),
                ["keyword"] = ConvertToRankedResults(keywordResults, "keyword")
            };
            
            var fusedResults = _rankFusionService.FuseWithRRF(resultSets, k: 60, topN: maxResults);
            combinedResults = ConvertFromRankedResults(fusedResults);
        }
        else // Use weighted fusion
        {
            var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, float weight)>
            {
                ["vector"] = (ConvertToRankedResults(vectorResults, "vector"), vectorWeight),
                ["keyword"] = (ConvertToRankedResults(keywordResults, "keyword"), 1 - vectorWeight)
            };
            
            var fusedResults = _rankFusionService.FuseWithWeights(resultSets, topN: maxResults);
            combinedResults = ConvertFromRankedResults(fusedResults);
        }

        return combinedResults;
    }

    /// <summary>
    /// 키워드 기반 검색
    /// </summary>
    public async Task<IEnumerable<CoreSearchResult>> KeywordSearchAsync(
        string keyword,
        int maxResults = 10,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Keyword search: {Keyword}", keyword);

        // Search in document repository
        var documents = await _documentRepository.SearchByKeywordAsync(keyword, maxResults, cancellationToken);
        
        var results = new List<CoreSearchResult>();
        foreach (var doc in documents)
        {
            // Get chunks for each document
            var chunks = await _vectorStore.GetByDocumentIdAsync(doc.Id, cancellationToken);
            
            // Filter chunks containing keyword
            var matchingChunks = chunks.Where(c => 
                c.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            results.AddRange(matchingChunks.Select(chunk => new CoreSearchResult
            {
                Chunk = chunk,
                Score = CalculateKeywordScore(chunk.Content, keyword),
                FileName = doc.FileName,
                Metadata = doc.Metadata
            }));
        }

        // Apply filter if provided
        if (filter != null && filter.Any())
        {
            results = ApplyFilter(results, filter);
        }

        return results.OrderByDescending(r => r.Score).Take(maxResults);
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
    public async Task<DocumentChunk?> GetChunkAsync(
        string chunkId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting chunk: {ChunkId}", chunkId);
        return await _vectorStore.GetAsync(chunkId, cancellationToken);
    }

    /// <summary>
    /// 유사 문서 찾기
    /// </summary>
    public async Task<IEnumerable<CoreSearchResult>> FindSimilarAsync(
        string documentId,
        int maxResults = 10,
        float minScore = 0.5f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Finding similar documents to: {DocumentId}", documentId);

        // Get document chunks
        var chunks = await _vectorStore.GetByDocumentIdAsync(documentId, cancellationToken);
        if (!chunks.Any())
            return Enumerable.Empty<CoreSearchResult>();

        // Use first chunk's embedding for similarity search
        var firstChunk = chunks.First();
        if (firstChunk.Embedding == null)
            return Enumerable.Empty<CoreSearchResult>();

        // Search for similar chunks
        var similarChunks = await _vectorStore.SearchAsync(
            firstChunk.Embedding,
            maxResults + chunks.Count(), // Get extra to filter out same document
            minScore,
            cancellationToken);

        // Filter out chunks from the same document
        var results = similarChunks
            .Where(c => c.DocumentId != documentId)
            .Select(chunk => new CoreSearchResult
            {
                Chunk = chunk,
                Score = chunk.Score ?? 0,
                FileName = chunk.DocumentId,
                Metadata = new FluxIndex.Domain.Entities.DocumentMetadata()
            })
            .Take(maxResults);

        return results;
    }

    /// <summary>
    /// 통계 정보 조회
    /// </summary>
    public async Task<RetrievalStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var docCount = await _documentRepository.GetCountAsync(cancellationToken);
        var chunkCount = await _vectorStore.GetCountAsync(cancellationToken);
        
        return new RetrievalStatistics
        {
            TotalDocuments = docCount,
            TotalChunks = chunkCount,
            AverageChunksPerDocument = docCount > 0 ? (double)chunkCount / docCount : 0,
            CacheEnabled = _cacheService != null,
            VectorStoreProvider = _vectorStore.GetType().Name
        };
    }

    private List<CoreSearchResult> ApplyFilter(List<CoreSearchResult> results, Dictionary<string, object> filter)
    {
        return results.Where(r =>
        {
            if (r.Metadata == null || r.Metadata.Properties == null) return false;

            foreach (var kvp in filter)
            {
                if (!r.Metadata.Properties.ContainsKey(kvp.Key) ||
                    !r.Metadata.Properties[kvp.Key].Equals(kvp.Value))
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

    private IEnumerable<CoreSearchResult> CombineResults(
        IEnumerable<CoreSearchResult> vectorResults,
        IEnumerable<CoreSearchResult> keywordResults,
        float vectorWeight)
    {
        var keywordWeight = 1 - vectorWeight;
        var combined = new Dictionary<string, CoreSearchResult>();

        // Add vector results
        foreach (var result in vectorResults)
        {
            var key = $"{result.DocumentId}:{result.ChunkId}";
            combined[key] = result;
            combined[key].Score *= vectorWeight;
        }

        // Combine with keyword results
        foreach (var result in keywordResults)
        {
            var key = $"{result.DocumentId}:{result.ChunkId}";
            if (combined.ContainsKey(key))
            {
                combined[key].Score += result.Score * keywordWeight;
            }
            else
            {
                result.Score *= keywordWeight;
                combined[key] = result;
            }
        }

        return combined.Values.OrderByDescending(r => r.Score);
    }

    private string GenerateCacheKey(string query, int maxResults, float minScore, Dictionary<string, object>? filter)
    {
        var filterStr = filter != null ? string.Join(",", filter.Select(kvp => $"{kvp.Key}:{kvp.Value}")) : "";
        return $"search:{query}:{maxResults}:{minScore}:{filterStr}";
    }

    private IEnumerable<RankedResult> ConvertToRankedResults(IEnumerable<CoreSearchResult> searchResults, string source)
    {
        return searchResults.Select((r, index) => new RankedResult
        {
            Id = r.ChunkId,
            DocumentId = r.DocumentId,
            ChunkId = r.ChunkId,
            Content = r.Content,
            Score = r.Score,
            Rank = index + 1,
            Source = source,
            Metadata = r.Metadata.Properties
        });
    }

    private IEnumerable<CoreSearchResult> ConvertFromRankedResults(IEnumerable<RankedResult> rankedResults)
    {
        return rankedResults.Select(r => new CoreSearchResult
        {
            Chunk = new DocumentChunk(r.Content, 0)
            {
                Id = r.ChunkId,
                DocumentId = r.DocumentId
            },
            Score = r.Score,
            FileName = r.DocumentId,
            Metadata = CreateMetadataFromDictionary(r.Metadata)
        });
    }

    private FluxIndex.Domain.Entities.DocumentMetadata CreateMetadataFromDictionary(Dictionary<string, object>? metadata)
    {
        var result = new FluxIndex.Domain.Entities.DocumentMetadata();

        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                if (kvp.Value is string stringValue)
                {
                    result.AddCustomField(kvp.Key, stringValue);
                }
                else
                {
                    result.Properties[kvp.Key] = kvp.Value;
                }
            }
        }

        return result;
    }
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