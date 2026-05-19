using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

// Use types from Models namespace for token-aware search
using SearchStrategy = FluxIndex.Core.Application.Models.SearchStrategy;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 토큰 예산 기반 검색 서비스
/// </summary>
public partial class TokenAwareSearchService : ITokenAwareSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQueryAnalysisService _queryAnalysis;
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<TokenAwareSearchService> _logger;

    public TokenAwareSearchService(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IQueryAnalysisService queryAnalysis,
        ITokenCounter tokenCounter,
        ILogger<TokenAwareSearchService> logger)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _queryAnalysis = queryAnalysis;
        _tokenCounter = tokenCounter;
        _logger = logger;
    }

    public Task<TokenAwareSearchResult> SearchAsync(
        string query,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        return SearchAsync(new TokenAwareSearchRequest
        {
            Query = query,
            MaxTokens = maxTokens
        }, cancellationToken);
    }

    public async Task<TokenAwareSearchResult> SearchAsync(
        TokenAwareSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. 질의 분석
        var analysis = await _queryAnalysis.AnalyzeAsync(request.Query, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
            LogTokenAwareSearch2(_logger, request.Query, analysis.Intent, analysis.Complexity);

        // 2. 검색 전략 결정
        var strategy = request.ForceStrategy ?? analysis.RecommendedStrategy;

        // 3. 검색 수행
        var searchResults = await ExecuteSearchAsync(
            request.Query, strategy, analysis, request.Filters, cancellationToken);

        // 4. 토큰 예산 내 청크 선택
        var selectedChunks = SelectChunksWithinBudget(
            searchResults, request.MaxTokens, request.MinScore, request.DiversityWeight);

        stopwatch.Stop();

        // 5. 결과 구성
        var result = new TokenAwareSearchResult
        {
            Query = request.Query,
            RequestedTokens = request.MaxTokens,
            UsedTokens = selectedChunks.Sum(c => c.TokenCount),
            Chunks = selectedChunks,
            TotalRetrieved = searchResults.Count,
            TruncatedCount = searchResults.Count - selectedChunks.Count,
            Analysis = analysis,
            Strategy = strategy,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
        };

        if (_logger.IsEnabled(LogLevel.Information))
            LogTokenAwareSearch1(_logger, selectedChunks.Count, searchResults.Count, result.UsedTokens, request.MaxTokens, stopwatch.ElapsedMilliseconds);

        return result;
    }

    private async Task<List<SearchResultItem>> ExecuteSearchAsync(
        string query,
        SearchStrategy strategy,
        Models.QueryAnalysis analysis,
        Dictionary<string, object>? filters,
        CancellationToken cancellationToken)
    {
        // 임베딩 생성
        var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // TopK 결정 (충분한 후보 확보)
        var topK = Math.Max(analysis.RecommendedTopK * 3, 50);

        // 벡터 검색 수행
        var results = await _vectorStore.SearchAsync(
            embedding, topK, 0.0f, filters: null, cancellationToken);

        return results.Select(r => new SearchResultItem
        {
            ChunkId = r.Id,
            DocumentId = r.DocumentId,
            Content = r.Content,
            Score = r.Score ?? 0.9,
            TokenCount = r.TokenCount > 0 ? r.TokenCount : _tokenCounter.Count(r.Content),
            ChunkIndex = r.ChunkIndex,
            Metadata = r.Metadata ?? new Dictionary<string, object>()
        }).ToList();
    }

    private static List<SelectedChunk> SelectChunksWithinBudget(
        List<SearchResultItem> candidates,
        int tokenBudget,
        double minScore,
        double diversityWeight)
    {
        var selected = new List<SelectedChunk>();
        var usedTokens = 0;
        var usedDocuments = new HashSet<string>();

        // 점수 순 정렬
        var sortedCandidates = candidates
            .Where(c => c.Score >= minScore)
            .OrderByDescending(c => c.Score)
            .ToList();

        foreach (var candidate in sortedCandidates)
        {
            // 토큰 예산 확인
            if (usedTokens + candidate.TokenCount > tokenBudget)
            {
                // 남은 예산이 충분하면 작은 청크라도 추가
                if (tokenBudget - usedTokens < 100)
                    break;
                continue;
            }

            // 다양성 고려 (같은 문서에서 너무 많이 가져오지 않음)
            if (diversityWeight > 0 && usedDocuments.Contains(candidate.DocumentId))
            {
                var sameDocCount = selected.Count(s => s.DocumentId == candidate.DocumentId);
                if (sameDocCount >= 3) // 같은 문서에서 최대 3개
                    continue;
            }

            // 청크 선택
            usedTokens += candidate.TokenCount;
            usedDocuments.Add(candidate.DocumentId);

            selected.Add(new SelectedChunk
            {
                ChunkId = candidate.ChunkId,
                DocumentId = candidate.DocumentId,
                Content = candidate.Content,
                Score = candidate.Score,
                TokenCount = candidate.TokenCount,
                CumulativeTokens = usedTokens,
                ChunkIndex = candidate.ChunkIndex,
                Metadata = candidate.Metadata,
                Highlights = ExtractHighlights(candidate.Content, candidate.Metadata)
            });
        }

        return selected;
    }

    private static List<string> ExtractHighlights(string content, Dictionary<string, object> metadata)
    {
        var highlights = new List<string>();

        // 메타데이터에서 키워드 추출
        if (metadata.TryGetValue("keywords", out var keywords) && keywords is IEnumerable<string> keywordList)
        {
            highlights.AddRange(keywordList.Take(5));
        }

        // 메타데이터에서 토픽 추출
        if (metadata.TryGetValue("topics", out var topics) && topics is IEnumerable<string> topicList)
        {
            highlights.AddRange(topicList.Take(3));
        }

        return highlights.Distinct().ToList();
    }

    /// <summary>
    /// 검색 결과 아이템 (내부용)
    /// </summary>
    private sealed class SearchResultItem
    {
        public string ChunkId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
        public int TokenCount { get; set; }
        public int ChunkIndex { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Query analysis complete: {Query} -> {Intent}, {Complexity}")]
    private static partial void LogTokenAwareSearch2(ILogger logger, string query, Models.QueryIntent intent, Models.QueryComplexityLevel complexity);
    [LoggerMessage(Level = LogLevel.Information, Message = "Search complete: {Selected}/{Total} chunks, {UsedTokens}/{RequestedTokens} tokens, {Elapsed}ms")]
    private static partial void LogTokenAwareSearch1(ILogger logger, int selected, int total, int usedTokens, int requestedTokens, long elapsed);

    #endregion
}
