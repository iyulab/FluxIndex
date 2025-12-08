using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Graph;

/// <summary>
/// Full GraphRAG pipeline service that orchestrates entity graph, community detection,
/// and hierarchical summarization for comprehensive retrieval-augmented generation.
/// Supports both local (entity-centric) and global (community-level) search strategies.
/// </summary>
public class GraphRAGService : IGraphRAGService
{
    private readonly IEntityGraphService _entityGraphService;
    private readonly ILeidenCommunityService _leidenCommunityService;
    private readonly IHierarchicalSummarizationService _summarizationService;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ITextCompletionService? _textCompletionService;
    private readonly ILogger<GraphRAGService> _logger;

    // Query scope detection patterns
    private static readonly string[] LocalQueryIndicators = new[]
    {
        "who", "what is", "where is", "when did", "how does", "which",
        "define", "describe", "explain", "tell me about",
        "find", "locate", "identify", "name"
    };

    private static readonly string[] GlobalQueryIndicators = new[]
    {
        "summarize", "overview", "main themes", "key topics",
        "what are all", "list all", "compare", "contrast",
        "overall", "in general", "broadly", "comprehensively",
        "trends", "patterns", "analysis", "synthesis"
    };

    private static readonly string[] HybridQueryIndicators = new[]
    {
        "how does X relate to Y", "relationship between", "connection",
        "impact of", "effect on", "influence", "role of",
        "in context of", "with respect to"
    };

    public GraphRAGService(
        IEntityGraphService entityGraphService,
        ILeidenCommunityService leidenCommunityService,
        IHierarchicalSummarizationService summarizationService,
        IEmbeddingService? embeddingService = null,
        ITextCompletionService? textCompletionService = null,
        ILogger<GraphRAGService>? logger = null)
    {
        _entityGraphService = entityGraphService ?? throw new ArgumentNullException(nameof(entityGraphService));
        _leidenCommunityService = leidenCommunityService ?? throw new ArgumentNullException(nameof(leidenCommunityService));
        _summarizationService = summarizationService ?? throw new ArgumentNullException(nameof(summarizationService));
        _embeddingService = embeddingService;
        _textCompletionService = textCompletionService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GraphRAGService>.Instance;
    }

    /// <inheritdoc />
    public async Task<GraphRAGIndex> BuildIndexAsync(
        IEnumerable<DocumentChunk> chunks,
        GraphRAGBuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new GraphRAGBuildOptions();

        var chunkList = chunks.ToList();
        if (options.MaxChunks.HasValue && chunkList.Count > options.MaxChunks.Value)
        {
            chunkList = chunkList.Take(options.MaxChunks.Value).ToList();
        }

        _logger.LogInformation("Building GraphRAG index for {ChunkCount} chunks", chunkList.Count);

        // Phase 1: Build entity graph
        var entityGraphTask = BuildEntityGraphAsync(chunkList, options, cancellationToken);

        // Phase 2: Build community hierarchy (in parallel if possible)
        var communityTask = BuildCommunityHierarchyAsync(chunkList, options, cancellationToken);

        EntityGraphResult entityGraph;
        CommunityHierarchy communityHierarchy;

        if (options.ParallelProcessing)
        {
            await Task.WhenAll(entityGraphTask, communityTask);
            entityGraph = await entityGraphTask;
            communityHierarchy = await communityTask;
        }
        else
        {
            entityGraph = await entityGraphTask;
            communityHierarchy = await communityTask;
        }

        // Phase 3: Generate hierarchical summaries
        var summaries = await GenerateSummariesAsync(
            communityHierarchy,
            chunkList,
            options,
            cancellationToken);

        sw.Stop();

        var chunkLookup = chunkList.ToDictionary(c => c.Id);

        var stats = new GraphRAGIndexStats
        {
            TotalChunks = chunkList.Count,
            TotalEntities = entityGraph.Entities.Count,
            TotalRelationships = entityGraph.Relations.Count,
            TotalCommunities = communityHierarchy.Levels.Sum(l => l.CommunityCount),
            HierarchyLevels = communityHierarchy.LevelCount,
            TotalSummaries = summaries.TotalCommunitiesSummarized,
            BuildTimeMs = sw.Elapsed.TotalMilliseconds
        };

        _logger.LogInformation(
            "GraphRAG index built: {Entities} entities, {Communities} communities, {Summaries} summaries in {TimeMs:F0}ms",
            stats.TotalEntities, stats.TotalCommunities, stats.TotalSummaries, stats.BuildTimeMs);

        return new GraphRAGIndex
        {
            EntityGraph = entityGraph,
            CommunityHierarchy = communityHierarchy,
            Summaries = summaries,
            Chunks = chunkLookup,
            Stats = stats,
            Options = options
        };
    }

    /// <inheritdoc />
    public async Task<GraphRAGQueryResult> QueryAsync(
        string query,
        GraphRAGIndex index,
        GraphRAGQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new GraphRAGQueryOptions();

        _logger.LogDebug("GraphRAG query: {Query}", query);

        // Detect query scope (or use forced scope)
        var scopeResult = options.ForceScope.HasValue
            ? new QueryScopeResult
            {
                Scope = options.ForceScope.Value,
                Confidence = 1.0,
                Reasoning = "Forced by options"
            }
            : await DetectQueryScopeAsync(query, cancellationToken);

        var scopeDetectionTime = sw.Elapsed.TotalMilliseconds;

        // Execute appropriate search strategy
        QueryScope usedScope = scopeResult.Scope;
        List<GraphRAGDocument> documents;
        List<GraphRAGEntity> relatedEntities = new();
        List<GraphRAGCommunity> relatedCommunities = new();
        double localSearchTime = 0;
        double globalSearchTime = 0;

        switch (scopeResult.Scope)
        {
            case QueryScope.Local:
                var localResult = await LocalSearchAsync(query, index,
                    new LocalSearchOptions
                    {
                        MaxEntities = options.MaxResults,
                        MaxHops = 2,
                        UseEntityEmbeddings = true,
                        MinEntityScore = options.MinConfidence
                    }, cancellationToken);
                localSearchTime = localResult.ProcessingTimeMs;
                documents = localResult.Documents.ToList();
                relatedEntities = localResult.MatchedEntities.ToList();
                break;

            case QueryScope.Global:
                var globalResult = await GlobalSearchAsync(query, index,
                    new GlobalSearchOptions
                    {
                        MaxCommunities = options.MaxResults,
                        MinSimilarityThreshold = options.MinConfidence
                    }, cancellationToken);
                globalSearchTime = globalResult.ProcessingTimeMs;
                documents = ConvertGlobalToDocuments(globalResult);
                relatedCommunities = globalResult.MatchedCommunities
                    .Select(mc => new GraphRAGCommunity
                    {
                        Id = mc.CommunityId,
                        Title = mc.Summary?.Title,
                        Summary = mc.Summary?.Summary ?? "",
                        Relevance = mc.RelevanceScore,
                        Level = mc.Summary?.Level ?? 0
                    }).ToList();
                break;

            case QueryScope.Hybrid:
            default:
                var hybridResult = await HybridSearchAsync(query, index,
                    new HybridGraphSearchOptions
                    {
                        MaxResults = options.MaxResults,
                        LocalWeight = 0.6,
                        GlobalWeight = 0.4,
                        FusionStrategy = GraphFusionStrategy.ReciprocalRankFusion
                    }, cancellationToken);
                localSearchTime = hybridResult.LocalResult.ProcessingTimeMs;
                globalSearchTime = hybridResult.GlobalResult.ProcessingTimeMs;
                documents = hybridResult.FusedDocuments.ToList();
                relatedEntities = hybridResult.LocalResult.MatchedEntities.ToList();
                relatedCommunities = hybridResult.GlobalResult.MatchedCommunities
                    .Select(mc => new GraphRAGCommunity
                    {
                        Id = mc.CommunityId,
                        Title = mc.Summary?.Title,
                        Summary = mc.Summary?.Summary ?? "",
                        Relevance = mc.RelevanceScore,
                        Level = mc.Summary?.Level ?? 0
                    }).ToList();
                usedScope = QueryScope.Hybrid;
                break;
        }

        // Generate answer if LLM is available
        var answer = "";
        var citations = new List<AnswerCitation>();
        double answerTime = 0;
        double confidence = 0;

        if (_textCompletionService != null && documents.Any())
        {
            var answerSw = Stopwatch.StartNew();
            var (generatedAnswer, generatedCitations) = await GenerateAnswerAsync(
                query, documents, relatedCommunities, options, cancellationToken);
            answer = generatedAnswer;
            citations = generatedCitations;
            answerTime = answerSw.Elapsed.TotalMilliseconds;
            confidence = CalculateAnswerConfidence(documents, relatedEntities, relatedCommunities);
        }
        else
        {
            // If no LLM, construct answer from top documents
            answer = ConstructFallbackAnswer(query, documents);
            confidence = documents.Any() ? documents.Average(d => d.Score) : 0;
        }

        sw.Stop();

        return new GraphRAGQueryResult
        {
            Query = query,
            Answer = answer,
            Confidence = confidence,
            UsedScope = usedScope,
            ScopeDetection = scopeResult,
            Documents = documents.Take(options.MaxResults).ToList(),
            RelatedEntities = relatedEntities,
            RelatedCommunities = relatedCommunities,
            Citations = citations,
            Stats = new QueryStats
            {
                TotalTimeMs = sw.Elapsed.TotalMilliseconds,
                ScopeDetectionTimeMs = scopeDetectionTime,
                LocalSearchTimeMs = localSearchTime,
                GlobalSearchTimeMs = globalSearchTime,
                AnswerGenerationTimeMs = answerTime,
                EntitiesMatched = relatedEntities.Count,
                CommunitiesMatched = relatedCommunities.Count,
                DocumentsRetrieved = documents.Count
            }
        };
    }

    /// <inheritdoc />
    public async Task<LocalSearchResult> LocalSearchAsync(
        string query,
        GraphRAGIndex index,
        LocalSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new LocalSearchOptions();

        _logger.LogDebug("Local search: {Query}", query);

        // Use entity graph service for entity-centric search
        var entitySearchOptions = new EntitySearchOptions
        {
            TopK = options.MaxEntities,
            UsePersonalizedPageRank = true,
            MinScore = options.MinEntityScore,
            IncludeExplanation = true
        };

        var entitySearchResult = await _entityGraphService.SearchByEntitiesAsync(
            query, index.EntityGraph, entitySearchOptions, cancellationToken);

        // Convert to GraphRAG document format
        var documents = entitySearchResult.Hits
            .Select(hit => new GraphRAGDocument
            {
                ChunkId = hit.ChunkId,
                DocumentId = GetDocumentId(hit.ChunkId, index),
                Content = hit.Content,
                Score = hit.Score,
                Source = "entity",
                RelatedEntityIds = hit.Entities.Select(e => e.Id).ToList()
            })
            .Take(options.MaxDocsPerEntity * options.MaxEntities)
            .ToList();

        // Get entity relationships
        var relationships = new List<EntityRelationInfo>();
        if (entitySearchResult.QueryEntities.Any())
        {
            var traversalResult = await _entityGraphService.TraverseEntityRelationsAsync(
                entitySearchResult.QueryEntities.Select(e => e.Id),
                index.EntityGraph,
                new EntityTraversalOptions { MaxHops = options.MaxHops },
                cancellationToken);

            foreach (var path in traversalResult.Paths)
            {
                foreach (var relation in path.Relations)
                {
                    relationships.Add(new EntityRelationInfo
                    {
                        SourceEntityId = relation.SourceEntityId,
                        TargetEntityId = relation.TargetEntityId,
                        RelationType = relation.Label,
                        Strength = relation.Weight
                    });
                }
            }
        }

        // Generate answer if LLM is available
        string? answer = null;
        if (_textCompletionService != null && documents.Any())
        {
            var context = string.Join("\n\n", documents.Take(5).Select(d => d.Content));
            answer = await GenerateLocalAnswerAsync(query, context, cancellationToken);
        }

        sw.Stop();

        return new LocalSearchResult
        {
            Query = query,
            MatchedEntities = entitySearchResult.QueryEntities
                .Select(e => new GraphRAGEntity
                {
                    Id = e.Id,
                    Text = e.Name,
                    Type = e.Type.ToString(),
                    Relevance = e.ImportanceScore
                }).ToList(),
            Documents = documents,
            Relationships = relationships,
            Answer = answer,
            Confidence = documents.Any() ? documents.Average(d => d.Score) : 0,
            ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
        };
    }

    /// <inheritdoc />
    public async Task<GlobalSearchResult> GlobalSearchAsync(
        string query,
        GraphRAGIndex index,
        GlobalSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new GlobalSearchOptions();

        _logger.LogDebug("Global search: {Query}", query);

        // Use hierarchical summarization service for global search
        var globalResult = await _summarizationService.GlobalSearchAsync(
            query,
            index.Summaries,
            options,
            cancellationToken);

        sw.Stop();

        return new GlobalSearchResult
        {
            Query = query,
            Answer = globalResult.Answer,
            MatchedCommunities = globalResult.MatchedCommunities,
            SearchLevel = globalResult.SearchLevel,
            TotalCommunitiesSearched = globalResult.TotalCommunitiesSearched,
            ProcessingTimeMs = sw.Elapsed.TotalMilliseconds,
            UsedQueryExpansion = globalResult.UsedQueryExpansion,
            ExpandedQueries = globalResult.ExpandedQueries
        };
    }

    /// <inheritdoc />
    public async Task<HybridGraphSearchResult> HybridSearchAsync(
        string query,
        GraphRAGIndex index,
        HybridGraphSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new HybridGraphSearchOptions();

        _logger.LogDebug("Hybrid search: {Query}", query);

        // Execute local and global searches in parallel
        var localTask = LocalSearchAsync(query, index, options.LocalOptions, cancellationToken);
        var globalTask = GlobalSearchAsync(query, index, options.GlobalOptions, cancellationToken);

        await Task.WhenAll(localTask, globalTask);

        var localResult = await localTask;
        var globalResult = await globalTask;

        // Fuse results based on strategy
        var fusedDocuments = FuseResults(
            localResult.Documents,
            ConvertGlobalToDocuments(globalResult),
            options);

        // Generate combined answer
        var answer = "";
        double confidence = 0;

        if (_textCompletionService != null)
        {
            answer = await GenerateHybridAnswerAsync(
                query, localResult, globalResult, cancellationToken);
            confidence = CalculateHybridConfidence(localResult, globalResult, fusedDocuments);
        }
        else
        {
            answer = localResult.Answer ?? globalResult.Answer.Text;
            confidence = fusedDocuments.Any() ? fusedDocuments.Average(d => d.Score) : 0;
        }

        sw.Stop();

        return new HybridGraphSearchResult
        {
            Query = query,
            LocalResult = localResult,
            GlobalResult = globalResult,
            FusedDocuments = fusedDocuments,
            Answer = answer,
            FusionStrategy = options.FusionStrategy,
            Confidence = confidence,
            ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
        };
    }

    /// <inheritdoc />
    public Task<QueryScopeResult> DetectQueryScopeAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var lowerQuery = query.ToLowerInvariant();

        // Calculate indicator scores
        double localScore = LocalQueryIndicators.Count(i => lowerQuery.Contains(i)) / (double)LocalQueryIndicators.Length;
        double globalScore = GlobalQueryIndicators.Count(i => lowerQuery.Contains(i)) / (double)GlobalQueryIndicators.Length;
        double hybridScore = HybridQueryIndicators.Count(i => lowerQuery.Contains(i)) / (double)HybridQueryIndicators.Length;

        // Calculate specificity (shorter queries with specific terms are more local)
        double specificityScore = CalculateSpecificityScore(lowerQuery);

        // Calculate thematic score (queries about concepts/themes are more global)
        double thematicScore = CalculateThematicScore(lowerQuery);

        // Entity mention detection (proper nouns, capitalized words in original)
        var detectedEntities = DetectEntityMentions(query);
        double entityMentionScore = Math.Min(detectedEntities.Count / 3.0, 1.0);

        // Aggregation indicators
        bool hasAggregation = lowerQuery.Contains("all") || lowerQuery.Contains("every") ||
                             lowerQuery.Contains("most") || lowerQuery.Contains("many");
        double aggregationScore = hasAggregation ? 0.7 : 0.0;

        // Comparative indicators
        bool hasComparison = lowerQuery.Contains("compare") || lowerQuery.Contains("versus") ||
                            lowerQuery.Contains(" vs ") || lowerQuery.Contains("difference");
        double comparativeScore = hasComparison ? 0.6 : 0.0;

        // Calculate final scope
        double localWeight = localScore + specificityScore + entityMentionScore;
        double globalWeight = globalScore + thematicScore + aggregationScore;
        double hybridWeight = hybridScore + comparativeScore;

        QueryScope scope;
        double confidence;
        string reasoning;

        if (hybridWeight > Math.Max(localWeight, globalWeight) * 0.8)
        {
            scope = QueryScope.Hybrid;
            confidence = hybridWeight / (localWeight + globalWeight + hybridWeight + 0.1);
            reasoning = "Query involves relationships or comparisons between specific and general concepts";
        }
        else if (localWeight > globalWeight * 1.2)
        {
            scope = QueryScope.Local;
            confidence = localWeight / (localWeight + globalWeight + 0.1);
            reasoning = "Query is specific, mentions entities, or asks for factual information";
        }
        else if (globalWeight > localWeight * 1.2)
        {
            scope = QueryScope.Global;
            confidence = globalWeight / (localWeight + globalWeight + 0.1);
            reasoning = "Query is broad, asks for summaries, or requires thematic understanding";
        }
        else
        {
            // Balanced - use hybrid
            scope = QueryScope.Hybrid;
            confidence = 0.6;
            reasoning = "Query has both specific and broad aspects";
        }

        var result = new QueryScopeResult
        {
            Scope = scope,
            Confidence = Math.Min(confidence, 1.0),
            Reasoning = reasoning,
            DetectedEntities = detectedEntities,
            DetectedThemes = DetectThemes(lowerQuery),
            Indicators = new QueryIndicators
            {
                SpecificityScore = specificityScore,
                EntityMentionScore = entityMentionScore,
                ThematicScore = thematicScore,
                AggregationScore = aggregationScore,
                ComparativeScore = comparativeScore
            }
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<GraphRAGIndex> UpdateIndexAsync(
        GraphRAGIndex index,
        IEnumerable<DocumentChunk> newChunks,
        GraphRAGUpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GraphRAGUpdateOptions();
        var chunkList = newChunks.ToList();

        _logger.LogInformation("Updating GraphRAG index with {NewChunkCount} chunks", chunkList.Count);

        // Build entity graph for new chunks
        var newEntityGraph = await _entityGraphService.BuildEntityGraphAsync(
            chunkList,
            index.Options.EntityGraphOptions,
            cancellationToken);

        // Merge with existing entity graph
        EntityGraphResult mergedEntityGraph;
        if (options.MergeEntities)
        {
            mergedEntityGraph = await _entityGraphService.MergeEntityGraphsAsync(
                new[] { index.EntityGraph, newEntityGraph },
                cancellationToken: cancellationToken);
        }
        else
        {
            // Simple merge without deduplication
            mergedEntityGraph = new EntityGraphResult
            {
                Id = index.EntityGraph.Id,
                Entities = index.EntityGraph.Entities.Concat(newEntityGraph.Entities).ToList(),
                Relations = index.EntityGraph.Relations.Concat(newEntityGraph.Relations).ToList(),
                ChunkMappings = index.EntityGraph.ChunkMappings.Concat(newEntityGraph.ChunkMappings).ToList(),
                SourceChunkIds = index.EntityGraph.SourceChunkIds.Concat(newEntityGraph.SourceChunkIds).ToList()
            };
        }

        // Update community hierarchy if needed
        CommunityHierarchy updatedHierarchy;
        if (options.RebuildCommunities)
        {
            var leidenChunks = index.Chunks.Values.Concat(chunkList)
                .Where(c => c.Embedding != null)
                .Select(c => new LeidenChunk
                {
                    Id = c.Id,
                    Content = c.Content,
                    DocumentId = c.DocumentId,
                    Embedding = new EmbeddingVector(c.Embedding!, c.Id)
                });

            updatedHierarchy = await _leidenCommunityService.DetectHierarchicalCommunitiesAsync(
                leidenChunks,
                index.Options.CommunityOptions,
                cancellationToken);
        }
        else
        {
            var leidenChunks = chunkList
                .Where(c => c.Embedding != null)
                .Select(c => new LeidenChunk
                {
                    Id = c.Id,
                    Content = c.Content,
                    DocumentId = c.DocumentId,
                    Embedding = new EmbeddingVector(c.Embedding!, c.Id)
                });

            updatedHierarchy = await _leidenCommunityService.UpdateHierarchyAsync(
                index.CommunityHierarchy,
                leidenChunks,
                cancellationToken: cancellationToken);
        }

        // Update summaries if needed
        HierarchicalSummaryResult updatedSummaries;
        if (options.UpdateSummaries)
        {
            var affectedCommunityIds = updatedHierarchy.Levels
                .SelectMany(l => l.Communities)
                .Where(c => c.ChunkIds.Any(id => chunkList.Any(chunk => chunk.Id == id)))
                .Select(c => c.Id);

            updatedSummaries = await _summarizationService.UpdateSummariesAsync(
                index.Summaries,
                chunkList,
                affectedCommunityIds,
                cancellationToken);
        }
        else
        {
            updatedSummaries = index.Summaries;
        }

        // Create updated chunk lookup
        var updatedChunks = new Dictionary<string, DocumentChunk>(index.Chunks);
        foreach (var chunk in chunkList)
        {
            updatedChunks[chunk.Id] = chunk;
        }

        return new GraphRAGIndex
        {
            Id = index.Id,
            EntityGraph = mergedEntityGraph,
            CommunityHierarchy = updatedHierarchy,
            Summaries = updatedSummaries,
            Chunks = updatedChunks,
            CreatedAt = index.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Stats = new GraphRAGIndexStats
            {
                TotalChunks = updatedChunks.Count,
                TotalEntities = mergedEntityGraph.Entities.Count,
                TotalRelationships = mergedEntityGraph.Relations.Count,
                TotalCommunities = updatedHierarchy.Levels.Sum(l => l.CommunityCount),
                HierarchyLevels = updatedHierarchy.LevelCount,
                TotalSummaries = updatedSummaries.TotalCommunitiesSummarized
            },
            Options = index.Options
        };
    }

    #region Private Helper Methods

    private async Task<EntityGraphResult> BuildEntityGraphAsync(
        List<DocumentChunk> chunks,
        GraphRAGBuildOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building entity graph from {Count} chunks", chunks.Count);

        return await _entityGraphService.BuildEntityGraphAsync(
            chunks,
            options.EntityGraphOptions,
            cancellationToken);
    }

    private async Task<CommunityHierarchy> BuildCommunityHierarchyAsync(
        List<DocumentChunk> chunks,
        GraphRAGBuildOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building community hierarchy from {Count} chunks", chunks.Count);

        // Ensure all chunks have embeddings
        var chunksWithEmbeddings = chunks.Where(c => c.Embedding != null).ToList();

        if (!chunksWithEmbeddings.Any())
        {
            // Generate embeddings if service is available
            if (_embeddingService != null && options.GenerateSummaryEmbeddings)
            {
                chunksWithEmbeddings = await GenerateChunkEmbeddingsAsync(chunks, cancellationToken);
            }
            else
            {
                _logger.LogWarning("No chunks with embeddings found and embedding service unavailable");
                return new CommunityHierarchy();
            }
        }

        var leidenChunks = chunksWithEmbeddings
            .Select(c => new LeidenChunk
            {
                Id = c.Id,
                Content = c.Content,
                DocumentId = c.DocumentId,
                Embedding = new EmbeddingVector(c.Embedding!, c.Id)
            });

        return await _leidenCommunityService.DetectHierarchicalCommunitiesAsync(
            leidenChunks,
            options.CommunityOptions,
            cancellationToken);
    }

    private async Task<HierarchicalSummaryResult> GenerateSummariesAsync(
        CommunityHierarchy hierarchy,
        List<DocumentChunk> chunks,
        GraphRAGBuildOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating hierarchical summaries for {LevelCount} levels", hierarchy.LevelCount);

        return await _summarizationService.GenerateHierarchicalSummariesAsync(
            hierarchy,
            chunks,
            options.SummarizationOptions,
            cancellationToken);
    }

    private async Task<List<DocumentChunk>> GenerateChunkEmbeddingsAsync(
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (_embeddingService == null) return chunks;

        var result = new List<DocumentChunk>();
        foreach (var chunk in chunks)
        {
            if (chunk.Embedding != null)
            {
                result.Add(chunk);
                continue;
            }

            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken);
            if (embedding != null && embedding.Length > 0)
            {
                // Update the existing chunk with the embedding
                chunk.Embedding = embedding;
                result.Add(chunk);
            }
            else
            {
                result.Add(chunk);
            }
        }

        return result;
    }

    private List<GraphRAGDocument> FuseResults(
        IReadOnlyList<GraphRAGDocument> localDocs,
        List<GraphRAGDocument> globalDocs,
        HybridGraphSearchOptions options)
    {
        return options.FusionStrategy switch
        {
            GraphFusionStrategy.WeightedSum => FuseWeightedSum(localDocs, globalDocs, options),
            GraphFusionStrategy.ReciprocalRankFusion => FuseRRF(localDocs, globalDocs, options),
            GraphFusionStrategy.Interleaved => FuseInterleaved(localDocs, globalDocs, options),
            _ => FuseWeightedSum(localDocs, globalDocs, options)
        };
    }

    private List<GraphRAGDocument> FuseWeightedSum(
        IReadOnlyList<GraphRAGDocument> localDocs,
        List<GraphRAGDocument> globalDocs,
        HybridGraphSearchOptions options)
    {
        var allDocs = new Dictionary<string, GraphRAGDocument>();

        foreach (var doc in localDocs)
        {
            var score = doc.Score * options.LocalWeight;
            allDocs[doc.ChunkId] = new GraphRAGDocument
            {
                ChunkId = doc.ChunkId,
                DocumentId = doc.DocumentId,
                Content = doc.Content,
                Score = score,
                Source = "local",
                RelatedEntityIds = doc.RelatedEntityIds,
                CommunityId = doc.CommunityId
            };
        }

        foreach (var doc in globalDocs)
        {
            var score = doc.Score * options.GlobalWeight;
            if (allDocs.TryGetValue(doc.ChunkId, out var existing))
            {
                // Combine scores
                allDocs[doc.ChunkId] = new GraphRAGDocument
                {
                    ChunkId = doc.ChunkId,
                    DocumentId = doc.DocumentId,
                    Content = doc.Content,
                    Score = existing.Score + score,
                    Source = "hybrid",
                    RelatedEntityIds = existing.RelatedEntityIds,
                    CommunityId = doc.CommunityId
                };
            }
            else
            {
                allDocs[doc.ChunkId] = new GraphRAGDocument
                {
                    ChunkId = doc.ChunkId,
                    DocumentId = doc.DocumentId,
                    Content = doc.Content,
                    Score = score,
                    Source = "global",
                    CommunityId = doc.CommunityId
                };
            }
        }

        return allDocs.Values
            .OrderByDescending(d => d.Score)
            .Take(options.MaxResults)
            .ToList();
    }

    private List<GraphRAGDocument> FuseRRF(
        IReadOnlyList<GraphRAGDocument> localDocs,
        List<GraphRAGDocument> globalDocs,
        HybridGraphSearchOptions options)
    {
        const double k = 60.0; // RRF constant

        var scores = new Dictionary<string, (double score, GraphRAGDocument doc)>();

        // Add local documents with RRF scores
        for (int i = 0; i < localDocs.Count; i++)
        {
            var doc = localDocs[i];
            var rrfScore = options.LocalWeight / (k + i + 1);
            scores[doc.ChunkId] = (rrfScore, doc);
        }

        // Add global documents with RRF scores
        for (int i = 0; i < globalDocs.Count; i++)
        {
            var doc = globalDocs[i];
            var rrfScore = options.GlobalWeight / (k + i + 1);

            if (scores.TryGetValue(doc.ChunkId, out var existing))
            {
                scores[doc.ChunkId] = (existing.score + rrfScore, new GraphRAGDocument
                {
                    ChunkId = doc.ChunkId,
                    DocumentId = doc.DocumentId,
                    Content = doc.Content,
                    Score = existing.score + rrfScore,
                    Source = "hybrid",
                    RelatedEntityIds = existing.doc.RelatedEntityIds,
                    CommunityId = doc.CommunityId
                });
            }
            else
            {
                scores[doc.ChunkId] = (rrfScore, new GraphRAGDocument
                {
                    ChunkId = doc.ChunkId,
                    DocumentId = doc.DocumentId,
                    Content = doc.Content,
                    Score = rrfScore,
                    Source = "global",
                    CommunityId = doc.CommunityId
                });
            }
        }

        return scores.Values
            .OrderByDescending(x => x.score)
            .Take(options.MaxResults)
            .Select(x => new GraphRAGDocument
            {
                ChunkId = x.doc.ChunkId,
                DocumentId = x.doc.DocumentId,
                Content = x.doc.Content,
                Score = x.score,
                Source = x.doc.Source,
                RelatedEntityIds = x.doc.RelatedEntityIds,
                CommunityId = x.doc.CommunityId
            })
            .ToList();
    }

    private List<GraphRAGDocument> FuseInterleaved(
        IReadOnlyList<GraphRAGDocument> localDocs,
        List<GraphRAGDocument> globalDocs,
        HybridGraphSearchOptions options)
    {
        var result = new List<GraphRAGDocument>();
        var seenIds = new HashSet<string>();

        int localIdx = 0, globalIdx = 0;
        bool pickLocal = true;

        while (result.Count < options.MaxResults &&
               (localIdx < localDocs.Count || globalIdx < globalDocs.Count))
        {
            if (pickLocal && localIdx < localDocs.Count)
            {
                var doc = localDocs[localIdx++];
                if (seenIds.Add(doc.ChunkId))
                {
                    result.Add(new GraphRAGDocument
                    {
                        ChunkId = doc.ChunkId,
                        DocumentId = doc.DocumentId,
                        Content = doc.Content,
                        Score = doc.Score,
                        Source = "local",
                        RelatedEntityIds = doc.RelatedEntityIds
                    });
                }
            }
            else if (!pickLocal && globalIdx < globalDocs.Count)
            {
                var doc = globalDocs[globalIdx++];
                if (seenIds.Add(doc.ChunkId))
                {
                    result.Add(new GraphRAGDocument
                    {
                        ChunkId = doc.ChunkId,
                        DocumentId = doc.DocumentId,
                        Content = doc.Content,
                        Score = doc.Score,
                        Source = "global",
                        CommunityId = doc.CommunityId
                    });
                }
            }
            else if (localIdx < localDocs.Count)
            {
                var doc = localDocs[localIdx++];
                if (seenIds.Add(doc.ChunkId))
                {
                    result.Add(new GraphRAGDocument
                    {
                        ChunkId = doc.ChunkId,
                        DocumentId = doc.DocumentId,
                        Content = doc.Content,
                        Score = doc.Score,
                        Source = "local",
                        RelatedEntityIds = doc.RelatedEntityIds
                    });
                }
            }
            else if (globalIdx < globalDocs.Count)
            {
                var doc = globalDocs[globalIdx++];
                if (seenIds.Add(doc.ChunkId))
                {
                    result.Add(new GraphRAGDocument
                    {
                        ChunkId = doc.ChunkId,
                        DocumentId = doc.DocumentId,
                        Content = doc.Content,
                        Score = doc.Score,
                        Source = "global",
                        CommunityId = doc.CommunityId
                    });
                }
            }

            pickLocal = !pickLocal;
        }

        return result;
    }

    private List<GraphRAGDocument> ConvertGlobalToDocuments(GlobalSearchResult globalResult)
    {
        var documents = new List<GraphRAGDocument>();

        foreach (var match in globalResult.MatchedCommunities)
        {
            // Create a document from the community summary
            documents.Add(new GraphRAGDocument
            {
                ChunkId = $"community:{match.CommunityId}",
                DocumentId = match.CommunityId,
                Content = match.Summary?.Summary ?? "",
                Score = match.RelevanceScore,
                Source = "community",
                CommunityId = match.CommunityId
            });

            // Also add source chunks from the community summary
            if (match.Summary?.SourceChunkIds != null)
            {
                foreach (var chunkId in match.Summary.SourceChunkIds.Take(3))
                {
                    documents.Add(new GraphRAGDocument
                    {
                        ChunkId = chunkId,
                        Score = match.RelevanceScore * 0.8,
                        Source = "community-source",
                        CommunityId = match.CommunityId
                    });
                }
            }
        }

        return documents.OrderByDescending(d => d.Score).ToList();
    }

    private async Task<(string answer, List<AnswerCitation> citations)> GenerateAnswerAsync(
        string query,
        List<GraphRAGDocument> documents,
        List<GraphRAGCommunity> communities,
        GraphRAGQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return (ConstructFallbackAnswer(query, documents), new List<AnswerCitation>());

        var context = BuildAnswerContext(documents, communities);

        var prompt = $@"Based on the following context, answer the question comprehensively and accurately.
Include relevant citations by referencing [1], [2], etc.

Context:
{context}

Question: {query}

Answer:";

        var response = await _textCompletionService.GenerateCompletionAsync(
            prompt,
            options.MaxAnswerTokens,
            options.Temperature,
            cancellationToken);

        // Extract citations from the response
        var citations = ExtractCitations(response, documents, communities);

        return (response, citations);
    }

    private async Task<string> GenerateLocalAnswerAsync(
        string query,
        string context,
        CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
            return $"Based on entity analysis: Found relevant information for query '{query}'";

        var prompt = $@"Answer the following question based on the context. Be specific and factual.

Context:
{context}

Question: {query}

Answer:";

        return await _textCompletionService.GenerateCompletionAsync(
            prompt,
            500,
            0.3f,
            cancellationToken);
    }

    private async Task<string> GenerateHybridAnswerAsync(
        string query,
        LocalSearchResult localResult,
        GlobalSearchResult globalResult,
        CancellationToken cancellationToken)
    {
        if (_textCompletionService == null)
        {
            return localResult.Answer ?? globalResult.Answer.Text;
        }

        var localContext = string.Join("\n", localResult.Documents.Take(3).Select(d => d.Content));
        var globalContext = globalResult.Answer.Text;

        var prompt = $@"Combine insights from both specific entity information and broader thematic understanding to answer the question.

Specific Information:
{localContext}

Broader Context:
{globalContext}

Question: {query}

Provide a comprehensive answer that integrates both perspectives:";

        return await _textCompletionService.GenerateCompletionAsync(
            prompt,
            1000,
            0.3f,
            cancellationToken);
    }

    private string BuildAnswerContext(List<GraphRAGDocument> documents, List<GraphRAGCommunity> communities)
    {
        var context = new System.Text.StringBuilder();

        for (int i = 0; i < Math.Min(documents.Count, 5); i++)
        {
            var doc = documents[i];
            context.AppendLine($"[{i + 1}] {doc.Content}");
            context.AppendLine();
        }

        if (communities.Any())
        {
            context.AppendLine("Community Context:");
            foreach (var community in communities.Take(3))
            {
                context.AppendLine($"- {community.Title ?? community.Id}: {community.Summary}");
            }
        }

        return context.ToString();
    }

    private List<AnswerCitation> ExtractCitations(
        string response,
        List<GraphRAGDocument> documents,
        List<GraphRAGCommunity> communities)
    {
        var citations = new List<AnswerCitation>();

        // Simple citation extraction by looking for [1], [2], etc.
        for (int i = 0; i < Math.Min(documents.Count, 10); i++)
        {
            if (response.Contains($"[{i + 1}]"))
            {
                var doc = documents[i];
                citations.Add(new AnswerCitation
                {
                    Index = i + 1,
                    CommunityId = doc.CommunityId ?? "",
                    CommunityTitle = communities.FirstOrDefault(c => c.Id == doc.CommunityId)?.Title,
                    Excerpt = doc.Content.Length > 200 ? doc.Content.Substring(0, 200) + "..." : doc.Content,
                    Relevance = doc.Score
                });
            }
        }

        return citations;
    }

    private string ConstructFallbackAnswer(string query, List<GraphRAGDocument> documents)
    {
        if (!documents.Any())
            return $"No relevant information found for: {query}";

        var topDoc = documents.First();
        return $"Based on available information: {topDoc.Content.Substring(0, Math.Min(500, topDoc.Content.Length))}...";
    }

    private double CalculateAnswerConfidence(
        List<GraphRAGDocument> documents,
        List<GraphRAGEntity> entities,
        List<GraphRAGCommunity> communities)
    {
        if (!documents.Any()) return 0;

        var docScore = documents.Average(d => d.Score);
        var entityScore = entities.Any() ? entities.Average(e => e.Relevance) : 0.5;
        var communityScore = communities.Any() ? communities.Average(c => c.Relevance) : 0.5;

        return (docScore * 0.5 + entityScore * 0.25 + communityScore * 0.25);
    }

    private double CalculateHybridConfidence(
        LocalSearchResult localResult,
        GlobalSearchResult globalResult,
        List<GraphRAGDocument> fusedDocs)
    {
        var localConf = localResult.Confidence;
        var globalConf = globalResult.Answer.Confidence;
        var fusedConf = fusedDocs.Any() ? fusedDocs.Average(d => d.Score) : 0;

        return (localConf * 0.3 + globalConf * 0.3 + fusedConf * 0.4);
    }

    private double CalculateSpecificityScore(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Shorter queries with specific terms are more specific
        double lengthFactor = Math.Max(0, 1 - (words.Length - 5) * 0.1);

        // Check for specific patterns
        bool hasQuotes = query.Contains("\"");
        bool hasProperNoun = words.Any(w => char.IsUpper(w[0]) && w.Length > 1);

        return (lengthFactor * 0.5 + (hasQuotes ? 0.3 : 0) + (hasProperNoun ? 0.2 : 0));
    }

    private double CalculateThematicScore(string query)
    {
        string[] thematicWords = { "theme", "topic", "concept", "idea", "trend", "pattern",
                                   "overall", "general", "main", "key", "summary", "overview" };

        var matchCount = thematicWords.Count(w => query.Contains(w));
        return Math.Min(matchCount * 0.2, 1.0);
    }

    private List<string> DetectEntityMentions(string query)
    {
        var entities = new List<string>();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Simple heuristic: capitalized words that are not at the start of sentences
        for (int i = 1; i < words.Length; i++)
        {
            var word = words[i].TrimEnd('.', ',', '?', '!');
            if (word.Length > 1 && char.IsUpper(word[0]) && !char.IsUpper(word[1]))
            {
                entities.Add(word);
            }
        }

        // Also check first word if it's not a question word
        if (words.Length > 0)
        {
            var first = words[0].TrimEnd('.', ',', '?', '!');
            if (!LocalQueryIndicators.Contains(first.ToLower()) &&
                !GlobalQueryIndicators.Contains(first.ToLower()) &&
                char.IsUpper(first[0]))
            {
                entities.Add(first);
            }
        }

        return entities.Distinct().ToList();
    }

    private IReadOnlyList<string> DetectThemes(string query)
    {
        var themes = new List<string>();

        // Simple theme detection based on common patterns
        string[] themePatterns = { "about", "regarding", "concerning", "related to", "in terms of" };

        foreach (var pattern in themePatterns)
        {
            var idx = query.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var afterPattern = query.Substring(idx + pattern.Length).Trim();
                var words = afterPattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                {
                    themes.Add(string.Join(" ", words.Take(3)));
                }
            }
        }

        return themes;
    }

    private string GetDocumentId(string chunkId, GraphRAGIndex index)
    {
        if (index.Chunks.TryGetValue(chunkId, out var chunk))
        {
            return chunk.DocumentId;
        }
        return chunkId.Split(':').FirstOrDefault() ?? chunkId;
    }

    #endregion
}
