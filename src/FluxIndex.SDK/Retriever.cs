using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using FluxIndex.Domain.Models;
using DocumentChunkEntity = FluxIndex.Domain.Entities.DocumentChunk;
using DocumentChunkModel = FluxIndex.Domain.Models.DocumentChunk;
using RankedResultCore = FluxIndex.Core.Application.Interfaces.RankedResult;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        _rankFusionService = rankFusionService; // ?? new RankFusionService();
        _logger = logger ?? new NullLogger<Retriever>();
    }

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
        _logger.LogInformation("Searching for: {Query}", query);

        // Check cache first
        if (_cacheService != null)
        {
            var cacheKey = GenerateCacheKey(query, maxResults, minScore, filter);
            var cachedResults = await _cacheService.GetAsync<List<VectorSearchResult>>(cacheKey, cancellationToken);
            if (cachedResults != null)
            {
                _logger.LogDebug("Cache hit for query: {Query}", query);
                return cachedResults;
            }
        }

        // Generate embedding for query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Search in vector store
        var searchResults = await _vectorStore.SearchAsync(
            queryEmbedding,
            maxResults,
            minScore,
            cancellationToken);

        // Convert DocumentChunks to VectorSearchResults
        var results = searchResults.Select(chunk => new VectorSearchResult
        {
            DocumentChunk = ConvertToModelChunk(chunk),
            Score = 1.0f, // Default score since IVectorStore doesn't provide it
            Rank = 0,
            Distance = 0,
            Metadata = chunk.Metadata
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
    public async Task<IEnumerable<VectorSearchResult>> HybridSearchAsync(
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
        IEnumerable<VectorSearchResult> combinedResults;
        
        if (Math.Abs(vectorWeight - 0.5f) < 0.01f) // Equal weights, use RRF
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
            var resultSets = new Dictionary<string, (IEnumerable<RankedResultCore> results, float weight)>
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
    public async Task<IEnumerable<VectorSearchResult>> KeywordSearchAsync(
        string keyword,
        int maxResults = 10,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Keyword search: {Keyword}", keyword);

        // Search in document repository
        var documents = await _documentRepository.SearchByKeywordAsync(keyword, maxResults, cancellationToken);
        
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
                DocumentChunk = ConvertToModelChunk(chunk),
                Score = CalculateKeywordScore(chunk.Content, keyword),
                Rank = 0,
                Distance = 0,
                Metadata = doc.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
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
                DocumentChunk = ConvertToModelChunk(chunk),
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
        float vectorWeight)
    {
        var keywordWeight = 1 - vectorWeight;
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
        return DocumentChunkModel.Create(
            entityChunk.DocumentId,
            entityChunk.Content,
            entityChunk.ChunkIndex,
            entityChunk.TotalChunks,
            entityChunk.Embedding,
            0f, // score
            entityChunk.TokenCount,
            entityChunk.Metadata
        );
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
            DocumentChunk = ConvertToModelChunk(new DocumentChunkEntity
            {
                Id = r.ChunkId,
                DocumentId = r.DocumentId,
                Content = r.Content,
                ChunkIndex = 0,
                TokenCount = 0,
                Metadata = r.Metadata ?? new Dictionary<string, object>()
            }),
            Score = (float)r.Score,
            Rank = r.Rank,
            Distance = 0,
            Metadata = r.Metadata ?? new Dictionary<string, object>()
        });
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