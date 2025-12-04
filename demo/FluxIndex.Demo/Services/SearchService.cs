using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using IEmbeddingService = FluxIndex.Core.Application.Interfaces.IEmbeddingService;
using IVectorStore = FluxIndex.Core.Application.Interfaces.IVectorStore;
using FluxIndexDocumentChunk = FluxIndex.Core.Domain.Entities.DocumentChunk;

namespace FluxIndex.Demo.Services;

/// <summary>
/// Service for searching indexed documents with optional reranking
/// </summary>
public class SearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IReranker? _reranker;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        ILogger<SearchService> logger,
        IReranker? reranker = null)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _reranker = reranker;
        _logger = logger;
    }

    public async Task<SearchResults> SearchAsync(string query, int topK = 10, bool useReranker = true)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Generate query embedding
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);

            // Vector search - returns IEnumerable<DocumentChunk>
            var vectorResults = await _vectorStore.SearchAsync(queryEmbedding, topK * 2);

            var results = vectorResults.Select(chunk => new SearchResult
            {
                ChunkId = chunk.Id,
                Content = chunk.Content,
                Score = chunk.Score ?? 0.0,
                Metadata = chunk.Metadata ?? new Dictionary<string, object>(),
                Source = chunk.Metadata?.TryGetValue("source", out var src) == true ? src?.ToString() : null
            }).ToList();

            // Apply reranking if available and requested
            if (useReranker && _reranker != null && results.Count > 0)
            {
                try
                {
                    // Convert to RetrievalCandidate format
                    var candidates = results.Select((r, index) => new RetrievalCandidate
                    {
                        Id = r.ChunkId,
                        ChunkId = r.ChunkId,
                        Content = r.Content,
                        InitialScore = (float)r.Score,
                        InitialRank = index + 1,
                        Metadata = r.Metadata
                    });

                    var rerankOptions = new RerankOptions
                    {
                        TopN = topK,
                        ScoreThreshold = 0.0f
                    };

                    var rerankedResults = await _reranker.RerankAsync(query, candidates, rerankOptions);

                    // Update scores based on reranking
                    var rerankedList = new List<SearchResult>();
                    foreach (var rerankResult in rerankedResults)
                    {
                        var original = results.FirstOrDefault(r => r.ChunkId == rerankResult.ChunkId);
                        if (original != null)
                        {
                            rerankedList.Add(original with
                            {
                                RerankedScore = rerankResult.RerankScore,
                                WasReranked = true
                            });
                        }
                    }

                    results = rerankedList.OrderByDescending(r => r.RerankedScore ?? r.Score).ToList();
                    _logger.LogInformation("Reranked {Count} results", results.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reranking failed, using vector scores");
                }
            }

            // Limit to topK
            results = results.Take(topK).ToList();

            stopwatch.Stop();

            return new SearchResults
            {
                Query = query,
                Results = results,
                TotalResults = results.Count,
                SearchTimeMs = stopwatch.ElapsedMilliseconds,
                UsedReranker = useReranker && _reranker != null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", query);
            stopwatch.Stop();

            return new SearchResults
            {
                Query = query,
                Results = new List<SearchResult>(),
                TotalResults = 0,
                SearchTimeMs = stopwatch.ElapsedMilliseconds,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Search with MCP-compatible output format
    /// </summary>
    public async Task<McpSearchResults> SearchWithMcpFormatAsync(
        string query,
        int topK = 10,
        bool useReranker = true,
        bool includeMetadata = true,
        int maxTokens = 5000)
    {
        var searchResults = await SearchAsync(query, topK, useReranker);

        // Limit results based on maxTokens (approximate: 1 token ≈ 4 characters)
        var limitedResults = new List<McpSearchResult>();
        int currentTokens = 0;
        int resultsIncluded = 0;

        foreach (var r in searchResults.Results)
        {
            // Estimate tokens (rough approximation: 1 token ≈ 4 chars for English, 2 chars for Korean)
            var contentTokens = EstimateTokens(r.Content);

            if (currentTokens + contentTokens > maxTokens && limitedResults.Count > 0)
            {
                // Don't add more results if we've exceeded token limit
                break;
            }

            limitedResults.Add(new McpSearchResult
            {
                Id = r.ChunkId,
                Content = r.Content,
                Score = r.RerankedScore ?? r.Score,
                VectorScore = r.Score,
                RerankedScore = r.RerankedScore,
                WasReranked = r.WasReranked,
                Metadata = includeMetadata ? r.Metadata : null,
                Source = r.Source
            });

            currentTokens += contentTokens;
            resultsIncluded++;
        }

        return new McpSearchResults
        {
            ToolName = "fluxindex_search",
            Query = query,
            Parameters = new McpParameters
            {
                TopK = topK,
                UseReranker = useReranker,
                IncludeMetadata = includeMetadata,
                MaxTokens = maxTokens
            },
            Results = limitedResults,
            Metadata = new McpResultMetadata
            {
                TotalResults = searchResults.TotalResults,
                ResultsReturned = resultsIncluded,
                EstimatedTokens = currentTokens,
                SearchTimeMs = searchResults.SearchTimeMs,
                UsedReranker = searchResults.UsedReranker,
                Error = searchResults.Error
            }
        };
    }

    /// <summary>
    /// Estimate token count for content (mixed Korean/English)
    /// </summary>
    private static int EstimateTokens(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;

        // Count Korean characters (each typically 2-3 tokens)
        int koreanChars = content.Count(c => c >= 0xAC00 && c <= 0xD7A3);
        int otherChars = content.Length - koreanChars;

        // Approximate: Korean ~2 chars/token, English ~4 chars/token
        return (koreanChars / 2) + (otherChars / 4) + 1;
    }
}

public record SearchResults
{
    public string Query { get; init; } = "";
    public List<SearchResult> Results { get; init; } = new();
    public int TotalResults { get; init; }
    public long SearchTimeMs { get; init; }
    public bool UsedReranker { get; init; }
    public string? Error { get; init; }
}

public record SearchResult
{
    public string ChunkId { get; init; } = "";
    public string Content { get; init; } = "";
    public double Score { get; init; }
    public double? RerankedScore { get; init; }
    public bool WasReranked { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    public string? Source { get; init; }
}

// MCP-compatible result format
public record McpSearchResults
{
    public string ToolName { get; init; } = "fluxindex_search";
    public string Query { get; init; } = "";
    public McpParameters Parameters { get; init; } = new();
    public List<McpSearchResult> Results { get; init; } = new();
    public McpResultMetadata Metadata { get; init; } = new();
}

public record McpParameters
{
    public int TopK { get; init; }
    public bool UseReranker { get; init; }
    public bool IncludeMetadata { get; init; }
    public int MaxTokens { get; init; }
}

public record McpSearchResult
{
    public string Id { get; init; } = "";
    public string Content { get; init; } = "";
    public double Score { get; init; }
    public double VectorScore { get; init; }
    public double? RerankedScore { get; init; }
    public bool WasReranked { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public string? Source { get; init; }
}

public record McpResultMetadata
{
    public int TotalResults { get; init; }
    public int ResultsReturned { get; init; }
    public int EstimatedTokens { get; init; }
    public long SearchTimeMs { get; init; }
    public bool UsedReranker { get; init; }
    public string? Error { get; init; }
}
