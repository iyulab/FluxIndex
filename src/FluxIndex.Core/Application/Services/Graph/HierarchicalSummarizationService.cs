using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace FluxIndex.Core.Application.Services.Graph;

/// <summary>
/// Hierarchical summarization service implementing map-reduce pattern
/// for community-level summarization supporting GraphRAG global search.
/// </summary>
public partial class HierarchicalSummarizationService : IHierarchicalSummarizationService
{
    private static readonly char[] SentenceSplitSeparators = ['.', '!', '?'];

    private readonly ITextCompletionService? _llmService;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<HierarchicalSummarizationService>? _logger;

    private const string CacheKeyPrefix = "HierarchicalSummary_";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HierarchicalSummarizationService(
        ITextCompletionService? llmService = null,
        IEmbeddingService? embeddingService = null,
        IMemoryCache? cache = null,
        ILogger<HierarchicalSummarizationService>? logger = null)
    {
        _llmService = llmService;
        _embeddingService = embeddingService;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HierarchicalSummaryResult> GenerateHierarchicalSummariesAsync(
        CommunityHierarchy hierarchy,
        IEnumerable<DocumentChunk> chunks,
        HierarchicalSummarizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new HierarchicalSummarizationOptions();
        var stopwatch = Stopwatch.StartNew();

        var chunkLookup = chunks.ToDictionary(c => c.Id, c => c);
        var summariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>();
        var statistics = new SummarizationStatisticsBuilder();

        if (_logger is not null)
            LogStartingSummarization(_logger, hierarchy.Id, hierarchy.LevelCount);

        // Determine which levels to summarize
        var levelsToProcess = options.LevelsToSummarize ??
            Enumerable.Range(0, hierarchy.LevelCount).ToArray();

        var mapPhaseStart = stopwatch.ElapsedMilliseconds;

        // Process each level (from finest to coarsest for proper hierarchy)
        foreach (var level in levelsToProcess.OrderBy(l => l))
        {
            if (level >= hierarchy.LevelCount)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var levelCommunities = hierarchy.Levels[level].Communities;
            var levelSummaries = new List<CommunitySummary>();

            if (options.ParallelGeneration && _llmService != null)
            {
                levelSummaries = await GenerateSummariesInParallelAsync(
                    levelCommunities,
                    level,
                    chunkLookup,
                    summariesByLevel,
                    options,
                    statistics,
                    cancellationToken);
            }
            else
            {
                foreach (var community in levelCommunities)
                {
                    var summary = await GenerateCommunitySummaryAsync(
                        community,
                        level,
                        chunkLookup,
                        summariesByLevel,
                        options,
                        statistics,
                        cancellationToken);

                    if (summary != null)
                    {
                        levelSummaries.Add(summary);
                    }
                }
            }

            summariesByLevel[level] = levelSummaries;
            statistics.AddLevelSummaries(level, levelSummaries.Count,
                levelSummaries.Average(s => s.Confidence));
        }

        var mapPhaseTime = stopwatch.ElapsedMilliseconds - mapPhaseStart;

        // Link parent-child relationships
        LinkSummaryHierarchy(summariesByLevel);

        stopwatch.Stop();

        var result = new HierarchicalSummaryResult
        {
            HierarchyId = hierarchy.Id,
            SummariesByLevel = summariesByLevel,
            TotalCommunitiesSummarized = summariesByLevel.Values.Sum(s => s.Count),
            Options = options,
            Statistics = statistics.Build(stopwatch.ElapsedMilliseconds, mapPhaseTime),
            Hierarchy = hierarchy,
            ChunkLookup = chunkLookup
        };

        if (_logger is not null)
            LogSummarizationComplete(_logger, result.TotalCommunitiesSummarized, summariesByLevel.Count, stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <inheritdoc />
    public async Task<GlobalSearchResult> GlobalSearchAsync(
        string query,
        HierarchicalSummaryResult summaryResult,
        GlobalSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GlobalSearchOptions();
        var stopwatch = Stopwatch.StartNew();

        if (_logger is not null) LogStartingGlobalSearch(_logger, query);

        // Get query embedding
        EmbeddingVector? queryEmbedding = null;
        if (_embeddingService != null)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
            if (embedding != null && embedding.Length > 0)
            {
                queryEmbedding = new EmbeddingVector(embedding, "query");
            }
        }

        // Find relevant communities at the specified level
        var searchLevel = Math.Min(options.SearchLevel, summaryResult.SummariesByLevel.Count - 1);
        searchLevel = Math.Max(0, searchLevel);

        if (!summaryResult.SummariesByLevel.TryGetValue(searchLevel, out var levelSummaries) ||
            levelSummaries.Count == 0)
        {
            return CreateEmptySearchResult(query, options, stopwatch.ElapsedMilliseconds);
        }

        var matchedCommunities = await FindRelevantCommunitiesAsync(
            query,
            queryEmbedding,
            levelSummaries,
            options,
            cancellationToken);

        // Include child communities if requested
        if (options.IncludeChildCommunities && matchedCommunities.Count > 0)
        {
            matchedCommunities = await ExpandWithChildCommunitiesAsync(
                matchedCommunities,
                summaryResult,
                searchLevel,
                options,
                cancellationToken);
        }

        // Synthesize answer from matched communities
        var relevantSummaries = matchedCommunities
            .Select(m => m.Summary)
            .ToList();

        var synthesizedAnswer = await SynthesizeAnswerAsync(
            query,
            relevantSummaries,
            new AnswerSynthesisOptions
            {
                MaxTokens = options.MaxAnswerTokens,
                Temperature = options.Temperature,
                IncludeCitations = options.IncludeSources
            },
            cancellationToken);

        stopwatch.Stop();

        return new GlobalSearchResult
        {
            Query = query,
            Answer = synthesizedAnswer,
            MatchedCommunities = matchedCommunities,
            SearchLevel = searchLevel,
            TotalCommunitiesSearched = levelSummaries.Count,
            ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
            UsedQueryExpansion = options.UseQueryExpansion
        };
    }

    /// <inheritdoc />
    public async Task<HierarchicalSummaryResult> UpdateSummariesAsync(
        HierarchicalSummaryResult existingResult,
        IEnumerable<DocumentChunk> newChunks,
        IEnumerable<string> affectedCommunityIds,
        CancellationToken cancellationToken = default)
    {
        var affectedIds = affectedCommunityIds.ToHashSet();
        if (affectedIds.Count == 0)
        {
            return existingResult;
        }

        if (_logger is not null) LogUpdatingCommunities(_logger, affectedIds.Count);

        // Merge new chunks into lookup
        var updatedChunkLookup = new Dictionary<string, DocumentChunk>(existingResult.ChunkLookup);
        foreach (var chunk in newChunks)
        {
            updatedChunkLookup[chunk.Id] = chunk;
        }

        // Find parent communities that need update
        var allAffectedIds = new HashSet<string>(affectedIds);
        foreach (var level in existingResult.SummariesByLevel.Values)
        {
            foreach (var summary in level)
            {
                if (summary.ChildSummaryIds.Any(id => affectedIds.Contains(id)))
                {
                    allAffectedIds.Add(summary.CommunityId);
                }
            }
        }

        // Regenerate affected summaries
        var updatedSummariesByLevel = new Dictionary<int, IReadOnlyList<CommunitySummary>>();
        var statistics = new SummarizationStatisticsBuilder();

        foreach (var (level, summaries) in existingResult.SummariesByLevel)
        {
            var updatedSummaries = new List<CommunitySummary>();

            foreach (var summary in summaries)
            {
                if (allAffectedIds.Contains(summary.CommunityId))
                {
                    // Regenerate this summary
                    var community = existingResult.Hierarchy?.Levels[level].Communities
                        .FirstOrDefault(c => c.Id == summary.CommunityId);

                    if (community != null)
                    {
                        var newSummary = await GenerateCommunitySummaryAsync(
                            community,
                            level,
                            updatedChunkLookup,
                            updatedSummariesByLevel,
                            existingResult.Options,
                            statistics,
                            cancellationToken);

                        updatedSummaries.Add(newSummary ?? summary);
                    }
                    else
                    {
                        updatedSummaries.Add(summary);
                    }
                }
                else
                {
                    updatedSummaries.Add(summary);
                }
            }

            updatedSummariesByLevel[level] = updatedSummaries;
        }

        // Invalidate cache for affected summaries
        if (_cache != null)
        {
            foreach (var id in allAffectedIds)
            {
                _cache.Remove(CacheKeyPrefix + id);
            }
        }

        return new HierarchicalSummaryResult
        {
            Id = Guid.NewGuid().ToString(),
            HierarchyId = existingResult.HierarchyId,
            SummariesByLevel = updatedSummariesByLevel,
            TotalCommunitiesSummarized = updatedSummariesByLevel.Values.Sum(s => s.Count),
            Options = existingResult.Options,
            Statistics = statistics.Build(0, 0),
            Hierarchy = existingResult.Hierarchy,
            ChunkLookup = updatedChunkLookup
        };
    }

    /// <inheritdoc />
    public async Task<SynthesizedAnswer> SynthesizeAnswerAsync(
        string query,
        IEnumerable<CommunitySummary> relevantSummaries,
        AnswerSynthesisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AnswerSynthesisOptions();

        var summaryList = relevantSummaries
            .Where(s => s.Confidence >= options.MinSummaryConfidence)
            .ToList();

        if (summaryList.Count == 0)
        {
            return new SynthesizedAnswer
            {
                Text = "No relevant information found to answer this query.",
                Confidence = 0,
                SourceCommunityCount = 0,
                IsComplete = false
            };
        }

        // Without LLM, return concatenated summaries
        if (_llmService == null)
        {
            return CreateFallbackAnswer(query, summaryList, options);
        }

        // Build synthesis prompt
        var prompt = BuildSynthesisPrompt(query, summaryList, options);

        try
        {
            var response = await _llmService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = options.MaxTokens, Temperature = options.Temperature }, cancellationToken);

            return ParseSynthesizedAnswer(response, summaryList, options);
        }
        catch (Exception ex)
        {
            if (_logger is not null) LogSynthesizeFailed(_logger, ex);
            return CreateFallbackAnswer(query, summaryList, options);
        }
    }

    /// <inheritdoc />
    public Task InvalidateSummariesAsync(
        IEnumerable<string> communityIds,
        bool cascadeToParents = true,
        CancellationToken cancellationToken = default)
    {
        if (_cache == null)
        {
            return Task.CompletedTask;
        }

        foreach (var id in communityIds)
        {
            _cache.Remove(CacheKeyPrefix + id);
        }

        var invalidatedCount = communityIds.Count();
        if (_logger is not null)
            LogInvalidatedSummaries(_logger, invalidatedCount);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CommunitySummary?> GetCachedSummaryAsync(
        string communityId,
        CancellationToken cancellationToken = default)
    {
        if (_cache == null)
        {
            return Task.FromResult<CommunitySummary?>(null);
        }

        if (_cache.TryGetValue<CommunitySummary>(CacheKeyPrefix + communityId, out var summary))
        {
            return Task.FromResult<CommunitySummary?>(summary);
        }

        return Task.FromResult<CommunitySummary?>(null);
    }

    #region Private Methods

    private async Task<List<CommunitySummary>> GenerateSummariesInParallelAsync(
        IReadOnlyList<LeidenCommunity> communities,
        int level,
        Dictionary<string, DocumentChunk> chunkLookup,
        Dictionary<int, IReadOnlyList<CommunitySummary>> existingSummaries,
        HierarchicalSummarizationOptions options,
        SummarizationStatisticsBuilder statistics,
        CancellationToken cancellationToken)
    {
        var summaries = new ConcurrentBag<CommunitySummary>();
        var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);

        var tasks = communities.Select(async community =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var summary = await GenerateCommunitySummaryAsync(
                    community, level, chunkLookup, existingSummaries,
                    options, statistics, cancellationToken);

                if (summary != null)
                {
                    summaries.Add(summary);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return summaries.OrderBy(s => s.CommunityId).ToList();
    }

    private async Task<CommunitySummary?> GenerateCommunitySummaryAsync(
        LeidenCommunity community,
        int level,
        Dictionary<string, DocumentChunk> chunkLookup,
        Dictionary<int, IReadOnlyList<CommunitySummary>> existingSummaries,
        HierarchicalSummarizationOptions options,
        SummarizationStatisticsBuilder statistics,
        CancellationToken cancellationToken)
    {
        // Check cache first
        if (options.EnableCaching && _cache != null)
        {
            if (_cache.TryGetValue<CommunitySummary>(CacheKeyPrefix + community.Id, out var cached))
            {
                if (cached != null && (cached.ExpiresAt == null || cached.ExpiresAt > DateTime.UtcNow))
                {
                    statistics.IncrementCacheHit();
                    return new CommunitySummary
                    {
                        CommunityId = cached.CommunityId,
                        Level = cached.Level,
                        Summary = cached.Summary,
                        Title = cached.Title,
                        Themes = cached.Themes,
                        Entities = cached.Entities,
                        Claims = cached.Claims,
                        Embedding = cached.Embedding,
                        Confidence = cached.Confidence,
                        SourceChunkCount = cached.SourceChunkCount,
                        SourceChunkIds = cached.SourceChunkIds,
                        ChildSummaryIds = cached.ChildSummaryIds,
                        ParentSummaryId = cached.ParentSummaryId,
                        GeneratedAt = cached.GeneratedAt,
                        ExpiresAt = cached.ExpiresAt,
                        IsCached = true
                    };
                }
            }
        }

        statistics.IncrementCacheMiss();

        // Get community content
        var chunkContents = GetCommunityChunkContents(community, chunkLookup, options);

        if (chunkContents.Count == 0)
        {
            return null;
        }

        // For higher levels, use child summaries if available
        string summaryText;
        var entities = new List<ExtractedSummaryEntity>();
        var claims = new List<ExtractedClaim>();

        if (level > 0 && existingSummaries.TryGetValue(level - 1, out var childSummaries))
        {
            // Reduce phase: combine child summaries
            var relevantChildSummaries = childSummaries
                .Where(s => community.ChildCommunityIds.Contains(s.CommunityId) ||
                           community.ChunkIds.Any(id => s.SourceChunkIds.Contains(id)))
                .ToList();

            if (relevantChildSummaries.Count > 0)
            {
                summaryText = await GenerateReduceSummaryAsync(
                    relevantChildSummaries,
                    level,
                    options,
                    cancellationToken);

                // Aggregate entities from children
                entities = AggregateEntities(relevantChildSummaries);
                claims = AggregateClaims(relevantChildSummaries);
            }
            else
            {
                summaryText = await GenerateMapSummaryAsync(
                    chunkContents,
                    community.Keywords,
                    options,
                    cancellationToken);
            }
        }
        else
        {
            // Map phase: summarize chunks directly
            summaryText = await GenerateMapSummaryAsync(
                chunkContents,
                community.Keywords,
                options,
                cancellationToken);

            if (options.ExtractEntities)
            {
                entities = ExtractEntitiesFromContent(chunkContents);
            }

            if (options.ExtractClaims)
            {
                claims = ExtractClaimsFromContent(chunkContents, community.ChunkIds.ToList());
            }
        }

        statistics.IncrementLLMCall();

        // Generate embedding for the summary
        EmbeddingVector? embedding = null;
        if (_embeddingService != null && !string.IsNullOrEmpty(summaryText))
        {
            var embeddingValues = await _embeddingService.GenerateEmbeddingAsync(
                summaryText, cancellationToken);
            if (embeddingValues != null && embeddingValues.Length > 0)
            {
                embedding = new EmbeddingVector(embeddingValues, "summary");
            }
        }

        var summary = new CommunitySummary
        {
            CommunityId = community.Id,
            Level = level,
            Summary = summaryText,
            Title = GenerateTitle(community.Keywords, summaryText),
            Themes = community.Keywords.ToList(),
            Entities = entities,
            Claims = claims,
            Embedding = embedding,
            Confidence = CalculateConfidence(chunkContents.Count, summaryText.Length),
            SourceChunkCount = community.ChunkIds.Count,
            SourceChunkIds = community.ChunkIds.ToList(),
            ExpiresAt = options.EnableCaching ? DateTime.UtcNow.Add(options.CacheExpiration) : null
        };

        // Cache the summary
        if (options.EnableCaching && _cache != null)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.CacheExpiration
            };
            _cache.Set(CacheKeyPrefix + community.Id, summary, cacheOptions);
        }

        return summary;
    }

    private static List<string> GetCommunityChunkContents(
        LeidenCommunity community,
        Dictionary<string, DocumentChunk> chunkLookup,
        HierarchicalSummarizationOptions options)
    {
        var contents = new List<string>();
        var chunkIds = community.RepresentativeChunkIds.Count > 0
            ? community.RepresentativeChunkIds
            : community.ChunkIds;

        foreach (var chunkId in chunkIds.Take(options.MaxChunksPerCommunity))
        {
            if (chunkLookup.TryGetValue(chunkId, out var chunk))
            {
                var content = chunk.Content;
                if (content.Length > options.MaxTokensPerChunk * 4) // Rough token estimation
                {
                    content = content[..(options.MaxTokensPerChunk * 4)];
                }
                contents.Add(content);
            }
        }

        return contents;
    }

    private async Task<string> GenerateMapSummaryAsync(
        List<string> contents,
        IReadOnlyList<string> keywords,
        HierarchicalSummarizationOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            // Fallback: simple extraction
            return GenerateFallbackSummary(contents, keywords);
        }

        var prompt = options.MapPromptTemplate ?? DefaultMapPrompt;
        prompt = prompt
            .Replace("{content}", string.Join("\n\n---\n\n", contents.Take(5)))
            .Replace("{keywords}", string.Join(", ", keywords.Take(10)))
            .Replace("{size}", contents.Count.ToString(CultureInfo.InvariantCulture));

        try
        {
            return await _llmService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = options.MaxSummaryTokens, Temperature = options.Temperature }, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_logger is not null) LogMapPhaseFailed(_logger, ex);
            return GenerateFallbackSummary(contents, keywords);
        }
    }

    private async Task<string> GenerateReduceSummaryAsync(
        List<CommunitySummary> childSummaries,
        int level,
        HierarchicalSummarizationOptions options,
        CancellationToken cancellationToken)
    {
        if (_llmService == null)
        {
            return string.Join(" ", childSummaries.Select(s => s.Summary));
        }

        var prompt = options.ReducePromptTemplate ?? DefaultReducePrompt;
        prompt = prompt
            .Replace("{summaries}", string.Join("\n\n", childSummaries.Select(s =>
                $"[{s.Title ?? "Section"}]: {s.Summary}")))
            .Replace("{level}", level.ToString(CultureInfo.InvariantCulture))
            .Replace("{count}", childSummaries.Count.ToString(CultureInfo.InvariantCulture));

        try
        {
            return await _llmService.CompleteAsync(
                prompt, new Flux.Abstractions.TextCompletionOptions { MaxTokens = options.MaxSummaryTokens, Temperature = options.Temperature }, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_logger is not null) LogReducePhaseFailed(_logger, ex);
            return string.Join(" ", childSummaries.Select(s => s.Summary));
        }
    }

    private static string GenerateFallbackSummary(List<string> contents, IReadOnlyList<string> keywords)
    {
        var topKeywords = string.Join(", ", keywords.Take(5));
        var firstContent = contents.FirstOrDefault() ?? "";
        var preview = firstContent.Length > 200 ? firstContent[..200] + "..." : firstContent;

        return $"This section covers topics related to {topKeywords}. {preview}";
    }

    private static string? GenerateTitle(IReadOnlyList<string> keywords, string summary)
    {
        if (keywords.Count == 0)
        {
            return summary.Length > 50 ? summary[..50] + "..." : summary;
        }

        return string.Join(" & ", keywords.Take(3));
    }

    private static double CalculateConfidence(int sourceCount, int summaryLength)
    {
        // More sources and reasonable summary length = higher confidence
        var sourceScore = Math.Min(sourceCount / 10.0, 1.0);
        var lengthScore = summaryLength > 50 && summaryLength < 2000 ? 1.0 : 0.5;

        return (sourceScore + lengthScore) / 2.0;
    }

    private static List<ExtractedSummaryEntity> ExtractEntitiesFromContent(List<string> contents)
    {
        var entityCounts = new Dictionary<string, (string type, int count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var content in contents)
        {
            // Simple pattern-based extraction (proper NER would use the entity service)
            var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i].Trim('.', ',', '!', '?', ';', ':', '"', '\'');

                // Capitalized words that aren't sentence starters (crude entity detection)
                if (word.Length > 2 && char.IsUpper(word[0]) && i > 0)
                {
                    var type = "UNKNOWN";
                    if (entityCounts.TryGetValue(word, out var existing))
                    {
                        entityCounts[word] = (existing.type, existing.count + 1);
                    }
                    else
                    {
                        entityCounts[word] = (type, 1);
                    }
                }
            }
        }

        return entityCounts
            .OrderByDescending(kvp => kvp.Value.count)
            .Take(10)
            .Select(kvp => new ExtractedSummaryEntity
            {
                Text = kvp.Key,
                Type = kvp.Value.type,
                MentionCount = kvp.Value.count,
                Importance = Math.Min(kvp.Value.count / 10.0, 1.0)
            })
            .ToList();
    }

    private static List<ExtractedClaim> ExtractClaimsFromContent(
        List<string> contents,
        List<string> chunkIds)
    {
        var claims = new List<ExtractedClaim>();

        // Simple extraction: sentences with certain patterns
        var claimPatterns = new[] { "is", "are", "was", "were", "has", "have", "can", "will" };

        foreach (var content in contents.Take(5))
        {
            var sentences = content.Split(SentenceSplitSeparators, StringSplitOptions.RemoveEmptyEntries);

            foreach (var sentence in sentences.Take(10))
            {
                var trimmed = sentence.Trim();
                if (trimmed.Length > 20 && trimmed.Length < 200 &&
                    claimPatterns.Any(p => trimmed.Contains($" {p} ", StringComparison.OrdinalIgnoreCase)))
                {
                    claims.Add(new ExtractedClaim
                    {
                        Text = trimmed,
                        Confidence = 0.6,
                        SupportingChunkIds = chunkIds.Take(2).ToList(),
                        Type = "fact"
                    });

                    if (claims.Count >= 5)
                    {
                        break;
                    }
                }
            }

            if (claims.Count >= 5)
            {
                break;
            }
        }

        return claims;
    }

    private static List<ExtractedSummaryEntity> AggregateEntities(List<CommunitySummary> summaries)
    {
        var aggregated = new Dictionary<string, ExtractedSummaryEntity>(StringComparer.OrdinalIgnoreCase);

        foreach (var summary in summaries)
        {
            foreach (var entity in summary.Entities)
            {
                if (aggregated.TryGetValue(entity.Text, out var existing))
                {
                    aggregated[entity.Text] = new ExtractedSummaryEntity
                    {
                        Text = existing.Text,
                        Type = existing.Type,
                        MentionCount = existing.MentionCount + entity.MentionCount,
                        Importance = Math.Max(existing.Importance, entity.Importance)
                    };
                }
                else
                {
                    aggregated[entity.Text] = entity;
                }
            }
        }

        return aggregated.Values
            .OrderByDescending(e => e.Importance)
            .Take(15)
            .ToList();
    }

    private static List<ExtractedClaim> AggregateClaims(List<CommunitySummary> summaries)
    {
        return summaries
            .SelectMany(s => s.Claims)
            .OrderByDescending(c => c.Confidence)
            .Take(10)
            .ToList();
    }

    private static void LinkSummaryHierarchy(Dictionary<int, IReadOnlyList<CommunitySummary>> summariesByLevel)
    {
        var levels = summariesByLevel.Keys.OrderBy(l => l).ToList();

        for (int i = 0; i < levels.Count - 1; i++)
        {
            var finerLevel = levels[i];
            var coarserLevel = levels[i + 1];

            var finerSummaries = summariesByLevel[finerLevel].ToDictionary(s => s.CommunityId);
            var coarserSummaries = summariesByLevel[coarserLevel];

            // This would need mutable summaries or a separate linking structure
            // For now, the relationship is implicit through the hierarchy
        }
    }

    private static async Task<List<MatchedCommunity>> FindRelevantCommunitiesAsync(
        string query,
        EmbeddingVector? queryEmbedding,
        IReadOnlyList<CommunitySummary> summaries,
        GlobalSearchOptions options,
        CancellationToken cancellationToken)
    {
        var matches = new List<MatchedCommunity>();

        foreach (var summary in summaries)
        {
            double similarity = 0;

            if (queryEmbedding != null && summary.Embedding != null)
            {
                similarity = CalculateCosineSimilarity(queryEmbedding, summary.Embedding);
            }
            else
            {
                // Keyword-based matching fallback
                var queryWords = query.ToLowerInvariant().Split(' ');
                var matchedThemes = summary.Themes.Count(t =>
                    queryWords.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase)));
                similarity = matchedThemes / (double)Math.Max(queryWords.Length, 1);
            }

            if (similarity >= options.MinSimilarityThreshold)
            {
                matches.Add(new MatchedCommunity
                {
                    CommunityId = summary.CommunityId,
                    Summary = summary,
                    Similarity = similarity,
                    RelevanceScore = similarity * summary.Confidence
                });
            }
        }

        var rankedMatches = matches
            .OrderByDescending(m => m.RelevanceScore)
            .Take(options.MaxCommunities)
            .Select((m, i) => new MatchedCommunity
            {
                CommunityId = m.CommunityId,
                Summary = m.Summary,
                Similarity = m.Similarity,
                RelevanceScore = m.RelevanceScore,
                Rank = i + 1
            })
            .ToList();

        return rankedMatches;
    }

    private static async Task<List<MatchedCommunity>> ExpandWithChildCommunitiesAsync(
        List<MatchedCommunity> parentMatches,
        HierarchicalSummaryResult summaryResult,
        int searchLevel,
        GlobalSearchOptions options,
        CancellationToken cancellationToken)
    {
        if (searchLevel <= 0)
        {
            return parentMatches;
        }

        var expanded = new List<MatchedCommunity>(parentMatches);
        var childLevel = searchLevel - 1;

        if (!summaryResult.SummariesByLevel.TryGetValue(childLevel, out var childSummaries))
        {
            return expanded;
        }

        foreach (var parent in parentMatches)
        {
            var relevantChildren = childSummaries
                .Where(s => s.ParentSummaryId == parent.CommunityId ||
                           parent.Summary.SourceChunkIds.Any(id => s.SourceChunkIds.Contains(id)))
                .Take(3)
                .Select((s, i) => new MatchedCommunity
                {
                    CommunityId = s.CommunityId,
                    Summary = s,
                    Similarity = parent.Similarity * 0.8, // Inherit parent similarity with decay
                    RelevanceScore = parent.RelevanceScore * 0.8,
                    Rank = expanded.Count + i + 1
                });

            expanded.AddRange(relevantChildren);
        }

        return expanded;
    }

    private static string BuildSynthesisPrompt(
        string query,
        List<CommunitySummary> summaries,
        AnswerSynthesisOptions options)
    {
        var prompt = options.PromptTemplate ?? DefaultSynthesisPrompt;

        var summaryTexts = new StringBuilder();
        for (int i = 0; i < summaries.Count; i++)
        {
            var summary = summaries[i];
            summaryTexts.AppendLine(CultureInfo.InvariantCulture, $"[Source {i + 1}: {summary.Title ?? "Section"}]");
            summaryTexts.AppendLine(summary.Summary);
            summaryTexts.AppendLine();
        }

        return prompt
            .Replace("{query}", query)
            .Replace("{summaries}", summaryTexts.ToString())
            .Replace("{count}", summaries.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static SynthesizedAnswer ParseSynthesizedAnswer(
        string response,
        List<CommunitySummary> summaries,
        AnswerSynthesisOptions options)
    {
        var citations = new List<AnswerCitation>();

        if (options.IncludeCitations)
        {
            for (int i = 0; i < summaries.Count; i++)
            {
                var summary = summaries[i];
                citations.Add(new AnswerCitation
                {
                    Index = i + 1,
                    CommunityId = summary.CommunityId,
                    CommunityTitle = summary.Title,
                    Excerpt = summary.Summary.Length > 100
                        ? summary.Summary[..100] + "..."
                        : summary.Summary,
                    Relevance = summary.Confidence
                });
            }
        }

        return new SynthesizedAnswer
        {
            Text = response.Trim(),
            Confidence = summaries.Average(s => s.Confidence),
            SourceCommunityCount = summaries.Count,
            Citations = citations,
            IsComplete = response.Length >= 50
        };
    }

    private static SynthesizedAnswer CreateFallbackAnswer(
        string query,
        List<CommunitySummary> summaries,
        AnswerSynthesisOptions options)
    {
        var combinedText = string.Join("\n\n",
            summaries.Select((s, i) => $"[{i + 1}] {s.Summary}"));

        return new SynthesizedAnswer
        {
            Text = $"Based on the available information:\n\n{combinedText}",
            Confidence = summaries.Average(s => s.Confidence) * 0.7,
            SourceCommunityCount = summaries.Count,
            IsComplete = false,
            Citations = summaries.Select((s, i) => new AnswerCitation
            {
                Index = i + 1,
                CommunityId = s.CommunityId,
                CommunityTitle = s.Title,
                Excerpt = s.Summary,
                Relevance = s.Confidence
            }).ToList()
        };
    }

    private static GlobalSearchResult CreateEmptySearchResult(
        string query,
        GlobalSearchOptions options,
        double processingTime)
    {
        return new GlobalSearchResult
        {
            Query = query,
            Answer = new SynthesizedAnswer
            {
                Text = "No relevant communities found for this query.",
                Confidence = 0,
                IsComplete = false
            },
            MatchedCommunities = Array.Empty<MatchedCommunity>(),
            SearchLevel = options.SearchLevel,
            ProcessingTimeMs = processingTime
        };
    }

    private static double CalculateCosineSimilarity(EmbeddingVector a, EmbeddingVector b)
    {
        var values1 = a.Values;
        var values2 = b.Values;

        if (values1.Length != values2.Length)
        {
            return 0;
        }

        double dot = 0, mag1 = 0, mag2 = 0;

        for (int i = 0; i < values1.Length; i++)
        {
            dot += values1[i] * values2[i];
            mag1 += values1[i] * values1[i];
            mag2 += values2[i] * values2[i];
        }

        var denominator = Math.Sqrt(mag1) * Math.Sqrt(mag2);
        return denominator > 0 ? dot / denominator : 0;
    }

    private const string DefaultMapPrompt = """
        Summarize the following content in 2-3 sentences, focusing on the main topics and key information.

        Keywords: {keywords}
        Number of source documents: {size}

        Content:
        {content}

        Provide a clear, informative summary that captures the essential points.
        """;

    private const string DefaultReducePrompt = """
        Combine the following section summaries into a coherent overview.
        This is level {level} of the hierarchy, combining {count} subsections.

        Section Summaries:
        {summaries}

        Create a unified summary that:
        1. Identifies the main theme connecting these sections
        2. Highlights key points from each section
        3. Maintains factual accuracy

        Provide a comprehensive summary in 3-4 sentences.
        """;

    private const string DefaultSynthesisPrompt = """
        Based on the following information sources, answer this question:

        Question: {query}

        Sources ({count} total):
        {summaries}

        Instructions:
        1. Synthesize information from multiple sources
        2. Cite sources using [Source N] format when appropriate
        3. If information is incomplete or uncertain, acknowledge this
        4. Provide a clear, direct answer to the question

        Answer:
        """;

    #endregion

    #region Statistics Builder

    private sealed class SummarizationStatisticsBuilder
    {
        private int _cacheHits;
        private int _cacheMisses;
        private int _llmCalls;
        private int _failures;
        private readonly Dictionary<int, int> _summariesByLevel = new();
        private readonly Dictionary<int, double> _confidenceByLevel = new();
        private readonly List<string> _errors = new();

        public void IncrementCacheHit() => Interlocked.Increment(ref _cacheHits);
        public void IncrementCacheMiss() => Interlocked.Increment(ref _cacheMisses);
        public void IncrementLLMCall() => Interlocked.Increment(ref _llmCalls);
        public void IncrementFailure() => Interlocked.Increment(ref _failures);
        public void AddError(string error) => _errors.Add(error);

        public void AddLevelSummaries(int level, int count, double avgConfidence)
        {
            _summariesByLevel[level] = count;
            _confidenceByLevel[level] = avgConfidence;
        }

        public SummarizationStatistics Build(double totalTime, double mapPhaseTime)
        {
            return new SummarizationStatistics
            {
                TotalProcessingTimeMs = totalTime,
                MapPhaseTimeMs = mapPhaseTime,
                ReducePhaseTimeMs = totalTime - mapPhaseTime,
                TotalLLMCalls = _llmCalls,
                CacheHits = _cacheHits,
                CacheMisses = _cacheMisses,
                SummariesByLevel = _summariesByLevel,
                AverageConfidenceByLevel = _confidenceByLevel,
                FailedSummarizations = _failures,
                Errors = _errors
            };
        }
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting hierarchical summarization for hierarchy {Id} with {Levels} levels")]
    private static partial void LogStartingSummarization(ILogger logger, string id, int levels);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hierarchical summarization complete: {Count} summaries across {Levels} levels in {Time}ms")]
    private static partial void LogSummarizationComplete(ILogger logger, int count, int levels, long time);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting global search for query: {Query}")]
    private static partial void LogStartingGlobalSearch(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updating {Count} affected communities")]
    private static partial void LogUpdatingCommunities(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to synthesize answer, using fallback")]
    private static partial void LogSynthesizeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalidated {Count} cached summaries")]
    private static partial void LogInvalidatedSummaries(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map phase LLM call failed, using fallback")]
    private static partial void LogMapPhaseFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reduce phase LLM call failed, using fallback")]
    private static partial void LogReducePhaseFailed(ILogger logger, Exception exception);

    #endregion
}
