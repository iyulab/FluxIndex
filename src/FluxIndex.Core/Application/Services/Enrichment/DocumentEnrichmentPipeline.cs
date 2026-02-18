using System.Diagnostics;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Enrichment;

/// <summary>
/// Orchestrates AI-powered document enrichment for polyglot persistence.
/// Coordinates multi-representation embedding generation, entity extraction,
/// contextual enrichment, and graph building during document indexing.
/// </summary>
public partial class DocumentEnrichmentPipeline : IDocumentEnrichmentPipeline
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ITextCompletionService? _textCompletionService;
    private readonly IAdvancedEntityExtractionService? _entityExtractionService;
    private readonly ILogger<DocumentEnrichmentPipeline> _logger;
    private readonly EnrichmentPipelineConfig _config;

    public DocumentEnrichmentPipeline(
        IEmbeddingService embeddingService,
        ILogger<DocumentEnrichmentPipeline> logger,
        ITextCompletionService? textCompletionService = null,
        IAdvancedEntityExtractionService? entityExtractionService = null,
        EnrichmentPipelineConfig? config = null)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textCompletionService = textCompletionService;
        _entityExtractionService = entityExtractionService;
        _config = config ?? new EnrichmentPipelineConfig();
    }

    /// <inheritdoc />
    public async Task<EnrichedChunk> EnrichChunkAsync(
        ChunkEnrichmentInput input,
        EnrichmentOptions? options = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= _config.DefaultOptions;

        LogDocumentEnrichment7(_logger, input.ChunkId, input.DocumentId);

        var embeddings = new MultiRepresentationEmbeddings();
        ContextualSummary? contextualSummary = null;
        var entities = new List<EnrichmentEntity>();
        var relationships = new List<ExtractedRelationship>();
        var keywords = new List<string>();

        try
        {
            // 1. Generate multi-representation embeddings
            if (options.GenerateContentEmbedding || options.GenerateContextualEmbedding ||
                options.GenerateHypotheticalEmbedding || options.GenerateSummaryEmbedding)
            {
                var embeddingOptions = new EmbeddingGenerationOptions
                {
                    GenerateContentEmbedding = options.GenerateContentEmbedding,
                    GenerateSummaryEmbedding = options.GenerateSummaryEmbedding,
                    GenerateHypotheticalEmbedding = options.GenerateHypotheticalEmbedding,
                    GenerateQuestionEmbeddings = options.GenerateHypotheticalQuestions
                };

                embeddings = await GenerateEmbeddingsAsync(input.Content, embeddingOptions, ct);

                // Generate contextual embedding if requested
                if (options.GenerateContextualEmbedding)
                {
                    var contextualContent = BuildContextualContent(input);
                    var contextualEmb = await _embeddingService.GenerateEmbeddingAsync(contextualContent, ct);
                    embeddings = embeddings with { Contextual = contextualEmb };
                }
            }

            // 2. Generate contextual summary (Anthropic's contextual retrieval)
            if (options.GenerateContextualSummary && _textCompletionService != null)
            {
                contextualSummary = await GenerateContextualSummaryAsync(
                    input.Content,
                    input.DocumentSummary,
                    input.PrecedingContent,
                    input.FollowingContent,
                    ct);
            }

            // 3. Extract entities
            if (options.ExtractEntities && _entityExtractionService != null)
            {
                var entityResult = await ExtractEntitiesAsync(input.Content, options.EntityExtractionOptions, ct);
                entities.AddRange(entityResult.Entities);
                relationships.AddRange(entityResult.Relationships);
            }

            // 4. Extract keywords
            if (options.ExtractKeywords)
            {
                keywords = await ExtractKeywordsAsync(input.Content, ct);
            }

            sw.Stop();
            LogDocumentEnrichment6(_logger, input.ChunkId, sw.ElapsedMilliseconds, embeddings.Content != null, entities.Count);

            return new EnrichedChunk
            {
                ChunkId = input.ChunkId,
                DocumentId = input.DocumentId,
                Content = input.Content,
                Embeddings = embeddings,
                ContextualSummary = contextualSummary,
                Entities = entities,
                Relationships = relationships,
                Keywords = keywords,
                Metadata = input.Metadata,
                EnrichedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogDocumentEnrichment5(_logger, ex, input.ChunkId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EnrichedChunk>> EnrichChunksBatchAsync(
        IEnumerable<ChunkEnrichmentInput> inputs,
        EnrichmentOptions? options = null,
        CancellationToken ct = default)
    {
        var inputList = inputs.ToList();
        if (inputList.Count == 0) return [];

        LogDocumentEnrichment4(_logger, inputList.Count);
        options ??= _config.DefaultOptions;

        // Process in parallel with configurable concurrency
        var semaphore = new SemaphoreSlim(options.MaxConcurrency);
        var tasks = inputList.Select(async input =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await EnrichChunkAsync(input, options, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results;
    }

    /// <inheritdoc />
    public async Task<EnrichedDocument> EnrichDocumentAsync(
        DocumentEnrichmentInput input,
        EnrichmentOptions? options = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= _config.DefaultOptions;

        LogDocumentEnrichment3(_logger, input.DocumentId, input.Chunks.Count);

        // Generate document-level summary first if we have full content and LLM
        string? documentSummary = null;
        float[]? documentEmbedding = null;

        if (_textCompletionService != null && !string.IsNullOrWhiteSpace(input.FullContent))
        {
            documentSummary = await GenerateDocumentSummaryAsync(input.FullContent, ct);
            documentEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                input.FullContent[..Math.Min(4000, input.FullContent.Length)], ct);
        }

        // Enrich each chunk with document context
        var enrichedInputs = input.Chunks.Select((chunk, index) => chunk with
        {
            DocumentTitle = input.Title,
            DocumentSummary = documentSummary,
            PrecedingContent = index > 0 ? input.Chunks[index - 1].Content : null,
            FollowingContent = index < input.Chunks.Count - 1 ? input.Chunks[index + 1].Content : null
        });

        var enrichedChunks = await EnrichChunksBatchAsync(enrichedInputs, options, ct);

        // Aggregate entities and relationships from chunks
        var allEntities = enrichedChunks.SelectMany(c => c.Entities).ToList();
        var allRelationships = enrichedChunks.SelectMany(c => c.Relationships).ToList();

        // Extract topics
        var topics = ExtractTopicsFromChunks(enrichedChunks);

        sw.Stop();

        return new EnrichedDocument
        {
            DocumentId = input.DocumentId,
            Title = input.Title,
            Summary = documentSummary,
            DocumentEmbedding = documentEmbedding,
            Chunks = enrichedChunks,
            Entities = MergeEntities(allEntities),
            Relationships = allRelationships,
            Topics = topics,
            Statistics = new EnrichmentStatistics
            {
                ChunksProcessed = enrichedChunks.Count,
                EntitiesExtracted = allEntities.Count,
                RelationshipsExtracted = allRelationships.Count,
                EmbeddingsGenerated = enrichedChunks.Count(c => c.Embeddings.Content != null),
                TotalProcessingTimeMs = sw.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc />
    public async Task<MultiRepresentationEmbeddings> GenerateEmbeddingsAsync(
        string content,
        EmbeddingGenerationOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new EmbeddingGenerationOptions();

        float[]? contentEmb = null;
        float[]? summaryEmb = null;
        float[]? hypotheticalEmb = null;

        // 1. Content embedding (standard)
        if (options.GenerateContentEmbedding)
        {
            contentEmb = await _embeddingService.GenerateEmbeddingAsync(content, ct);
        }

        // 2. Summary embedding
        if (options.GenerateSummaryEmbedding && _textCompletionService != null)
        {
            var summary = await GenerateSummaryAsync(content, ct);
            summaryEmb = await _embeddingService.GenerateEmbeddingAsync(summary, ct);
        }

        // 3. Hypothetical document embedding (HyDE)
        if (options.GenerateHypotheticalEmbedding && _textCompletionService != null)
        {
            var hypothetical = await GenerateHypotheticalDocumentAsync(content, ct);
            hypotheticalEmb = await _embeddingService.GenerateEmbeddingAsync(hypothetical, ct);
        }

        return new MultiRepresentationEmbeddings
        {
            Content = contentEmb,
            Summary = summaryEmb,
            Hypothetical = hypotheticalEmb
        };
    }

    /// <inheritdoc />
    public async Task<EntityExtractionResult> ExtractEntitiesAsync(
        string content,
        EnrichmentEntityOptions? options = null,
        CancellationToken ct = default)
    {
        if (_entityExtractionService == null)
        {
            return new EntityExtractionResult();
        }

        var sw = Stopwatch.StartNew();
        options ??= new EnrichmentEntityOptions();

        try
        {
            var extractionOptions = new EntityExtractionOptions
            {
                MinConfidence = options.MinConfidence,
                ExtractRelations = options.ExtractRelationships,
                MaxEntities = options.MaxEntitiesPerChunk
            };

            // Use ExtractEntityGraphAsync which returns both entities and relations
            var graph = await _entityExtractionService.ExtractEntityGraphAsync(content, extractionOptions, ct);

            // Convert to enrichment types
            var entities = graph.Entities.Select(e => new EnrichmentEntity
            {
                Id = e.Id,
                Name = e.Text,
                NormalizedName = !string.IsNullOrEmpty(e.NormalizedText) ? e.NormalizedText : e.Text.ToLowerInvariant(),
                Type = e.Type,
                Confidence = e.Confidence,
                SurfaceForms = [e.Text],
                Spans = e.Occurrences.Select(o => new TextSpan(o.StartPosition, o.EndPosition, e.Text)).ToList()
            }).ToList();

            // Build entity ID lookup
            var entityIdMap = graph.Entities.ToDictionary(e => e.Id, e => e.Text);

            var relationships = graph.Relations.Select(r => new ExtractedRelationship
            {
                Id = r.Id,
                SourceEntity = entityIdMap.GetValueOrDefault(r.SourceEntityId, r.SourceEntityId),
                TargetEntity = entityIdMap.GetValueOrDefault(r.TargetEntityId, r.TargetEntityId),
                Type = r.Type,
                Label = r.Label,
                Confidence = r.Confidence,
                Evidence = r.Evidence
            }).ToList();

            sw.Stop();

            return new EntityExtractionResult
            {
                Entities = entities,
                Relationships = relationships,
                ProcessingTimeMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            LogDocumentEnrichment2(_logger, ex);
            return new EntityExtractionResult();
        }
    }

    /// <inheritdoc />
    public async Task<ContextualSummary> GenerateContextualSummaryAsync(
        string chunkContent,
        string? documentContext = null,
        string? precedingChunks = null,
        string? followingChunks = null,
        CancellationToken ct = default)
    {
        if (_textCompletionService == null)
        {
            // Return a basic contextual summary without LLM
            var combined = BuildCombinedText(chunkContent, documentContext);
            return new ContextualSummary
            {
                Summary = chunkContent.Length > 200 ? chunkContent[..200] + "..." : chunkContent,
                CombinedText = combined,
                Confidence = 0.5
            };
        }

        var prompt = BuildContextualSummaryPrompt(chunkContent, documentContext, precedingChunks, followingChunks);

        try
        {
            var response = await _textCompletionService.GenerateCompletionAsync(
                prompt, maxTokens: 300, temperature: 0.3f, ct);

            var summary = response.Trim();
            var combined = BuildCombinedText(chunkContent, summary);

            return new ContextualSummary
            {
                Summary = summary,
                CombinedText = combined,
                DocumentContext = documentContext,
                Confidence = 0.9
            };
        }
        catch (Exception ex)
        {
            LogDocumentEnrichment1(_logger, ex);
            var combined = BuildCombinedText(chunkContent, documentContext);
            return new ContextualSummary
            {
                Summary = chunkContent.Length > 200 ? chunkContent[..200] + "..." : chunkContent,
                CombinedText = combined,
                Confidence = 0.3
            };
        }
    }

    /// <inheritdoc />
    public async Task<GraphBuildResult> BuildGraphDataAsync(
        IEnumerable<EnrichedChunk> chunks,
        GraphBuildOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new GraphBuildOptions();
        var chunkList = chunks.ToList();

        // Collect all entities from chunks
        var allEntities = chunkList.SelectMany(c => c.Entities).ToList();
        var allRelationships = chunkList.SelectMany(c => c.Relationships).ToList();

        // Merge duplicate entities if requested
        var mergedEntities = allEntities;
        var mergeStats = new EntityMergeStatistics { OriginalCount = allEntities.Count };

        if (options.MergeEntities && allEntities.Count > 0)
        {
            mergedEntities = MergeEntitiesWithNormalization(allEntities);
            mergeStats = mergeStats with
            {
                MergedCount = mergedEntities.Count,
                MergePairs = allEntities.Count - mergedEntities.Count
            };
        }

        // Convert relationships to graph relationships
        var graphRelationships = MapToGraphRelationships(allRelationships, mergedEntities, chunkList);

        // Detect communities if requested
        var communities = new List<GraphCommunity>();
        if (options.DetectCommunities && mergedEntities.Count >= options.MinCommunitySize)
        {
            communities = DetectCommunities(mergedEntities, graphRelationships, options);
        }

        // Convert entities to graph entities
        var graphEntities = await ConvertToGraphEntitiesAsync(mergedEntities, chunkList, options, ct);

        return new GraphBuildResult
        {
            Entities = graphEntities,
            Relationships = graphRelationships,
            Communities = communities,
            MergeStats = mergeStats
        };
    }

    /// <inheritdoc />
    public EnrichmentPipelineConfig GetConfiguration() => _config;

    #region Private Methods - Embeddings

    private static string BuildContextualContent(ChunkEnrichmentInput input)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(input.DocumentTitle))
        {
            parts.Add($"Document: {input.DocumentTitle}");
        }

        if (!string.IsNullOrWhiteSpace(input.DocumentSummary))
        {
            parts.Add($"Context: {input.DocumentSummary}");
        }

        parts.Add($"Content: {input.Content}");

        return string.Join("\n\n", parts);
    }

    private async Task<string> GenerateSummaryAsync(string content, CancellationToken ct)
    {
        if (_textCompletionService == null)
            return content.Length > 200 ? content[..200] : content;

        var prompt = $"Summarize the following text in 2-3 sentences:\n\n{content}";
        return await _textCompletionService.GenerateCompletionAsync(prompt, maxTokens: 150, temperature: 0.3f, ct);
    }

    private async Task<string> GenerateHypotheticalDocumentAsync(string content, CancellationToken ct)
    {
        if (_textCompletionService == null) return content;

        var prompt = $@"Given this text content, generate a detailed document that would contain this information:

Content: {content}

Generate a comprehensive document:";

        return await _textCompletionService.GenerateCompletionAsync(prompt, maxTokens: 300, temperature: 0.7f, ct);
    }

    private async Task<string> GenerateDocumentSummaryAsync(string content, CancellationToken ct)
    {
        if (_textCompletionService == null)
            return content.Length > 500 ? content[..500] : content;

        var truncated = content.Length > 8000 ? content[..8000] : content;
        var prompt = $"Provide a comprehensive summary of this document in 3-5 sentences:\n\n{truncated}";

        return await _textCompletionService.GenerateCompletionAsync(prompt, maxTokens: 300, temperature: 0.3f, ct);
    }

    #endregion

    #region Private Methods - Contextual Summary

    private static string BuildCombinedText(string chunkContent, string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return chunkContent;

        return $"{context}\n\n{chunkContent}";
    }

    private static string BuildContextualSummaryPrompt(
        string chunkContent,
        string? documentContext,
        string? precedingChunks,
        string? followingChunks)
    {
        var contextParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(documentContext))
        {
            contextParts.Add($"Document context: {documentContext}");
        }

        if (!string.IsNullOrWhiteSpace(precedingChunks))
        {
            var truncated = precedingChunks.Length > 500 ? precedingChunks[..500] : precedingChunks;
            contextParts.Add($"Preceding content: {truncated}");
        }

        if (!string.IsNullOrWhiteSpace(followingChunks))
        {
            var truncated = followingChunks.Length > 500 ? followingChunks[..500] : followingChunks;
            contextParts.Add($"Following content: {truncated}");
        }

        var context = contextParts.Count > 0
            ? string.Join("\n", contextParts)
            : "No additional context available.";

        return $@"Given the following chunk from a larger document, provide a brief (1-2 sentence) contextual summary that explains what this chunk is about and how it relates to the broader document context.

{context}

Chunk content:
{chunkContent}

Contextual summary:";
    }

    #endregion

    #region Private Methods - Keywords

    private async Task<List<string>> ExtractKeywordsAsync(string content, CancellationToken ct)
    {
        if (_textCompletionService == null)
        {
            return ExtractKeywordsSimple(content);
        }

        var prompt = $@"Extract 5-10 key terms or concepts from this text. Return as a comma-separated list:

{content}

Keywords:";

        try
        {
            var response = await _textCompletionService.GenerateCompletionAsync(
                prompt, maxTokens: 100, temperature: 0.2f, ct);

            return response.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(10)
                .ToList();
        }
        catch
        {
            return ExtractKeywordsSimple(content);
        }
    }

    private static List<string> ExtractKeywordsSimple(string content)
    {
        // Simple TF-based keyword extraction
        var words = content.ToLowerInvariant()
            .Split([' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        return words;
    }

    #endregion

    #region Private Methods - Graph Building

    private static List<EnrichmentEntity> MergeEntitiesWithNormalization(List<EnrichmentEntity> entities)
    {
        if (entities.Count <= 1) return entities;

        // Group by normalized name first (exact match)
        var groups = entities.GroupBy(e => e.NormalizedName.ToLowerInvariant()).ToList();

        var merged = new List<EnrichmentEntity>();

        foreach (var group in groups)
        {
            var items = group.ToList();
            if (items.Count == 1)
            {
                merged.Add(items[0]);
            }
            else
            {
                // Merge entities with same normalized name
                var primary = items.OrderByDescending(e => e.Confidence).First();
                var allSurfaceForms = items.SelectMany(e => e.SurfaceForms).Distinct().ToList();
                var allChunkIds = items.SelectMany(e => e.ChunkIds).Distinct().ToList();
                var allSpans = items.SelectMany(e => e.Spans).ToList();

                merged.Add(primary with
                {
                    SurfaceForms = allSurfaceForms,
                    ChunkIds = allChunkIds,
                    Spans = allSpans,
                    Confidence = items.Max(e => e.Confidence)
                });
            }
        }

        return merged;
    }

    private static List<GraphRelationship> MapToGraphRelationships(
        List<ExtractedRelationship> relationships,
        List<EnrichmentEntity> entities,
        List<EnrichedChunk> chunks)
    {
        // Build entity name to ID mapping
        var nameToId = entities
            .GroupBy(e => e.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        return relationships
            .Select(r =>
            {
                var sourceId = nameToId.GetValueOrDefault(r.SourceEntity.ToLowerInvariant());
                var targetId = nameToId.GetValueOrDefault(r.TargetEntity.ToLowerInvariant());

                if (sourceId == null || targetId == null)
                    return null;

                return new GraphRelationship
                {
                    Id = r.Id,
                    SourceEntityId = sourceId,
                    TargetEntityId = targetId,
                    Type = r.Type,
                    Label = r.Label,
                    Confidence = r.Confidence,
                    EvidenceChunkIds = r.ChunkIds,
                    EvidenceTexts = r.Evidence != null ? [r.Evidence] : []
                };
            })
            .Where(r => r != null)
            .Cast<GraphRelationship>()
            .ToList();
    }

    private static List<GraphCommunity> DetectCommunities(
        List<EnrichmentEntity> entities,
        List<GraphRelationship> relationships,
        GraphBuildOptions options)
    {
        // Simple community detection based on connected components
        var entityIds = entities.Select(e => e.Id).ToHashSet();
        var adjacency = new Dictionary<string, HashSet<string>>();

        foreach (var entityId in entityIds)
        {
            adjacency[entityId] = [];
        }

        foreach (var rel in relationships)
        {
            if (adjacency.TryGetValue(rel.SourceEntityId, out var sourceAdj) &&
                adjacency.TryGetValue(rel.TargetEntityId, out var targetAdj))
            {
                sourceAdj.Add(rel.TargetEntityId);
                targetAdj.Add(rel.SourceEntityId);
            }
        }

        // BFS to find connected components
        var visited = new HashSet<string>();
        var communities = new List<GraphCommunity>();

        foreach (var startId in entityIds)
        {
            if (visited.Contains(startId)) continue;

            var component = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(startId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (visited.Contains(current)) continue;

                visited.Add(current);
                component.Add(current);

                foreach (var neighbor in adjacency[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (component.Count >= options.MinCommunitySize)
            {
                var communityEntities = entities.Where(e => component.Contains(e.Id)).ToList();
                var topEntity = communityEntities.OrderByDescending(e => e.Confidence).First();

                communities.Add(new GraphCommunity
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"Community: {topEntity.Name}",
                    EntityIds = component,
                    Level = 0,
                    ImportanceScore = communityEntities.Average(e => e.Confidence)
                });
            }
        }

        return communities.OrderByDescending(c => c.EntityIds.Count).Take(options.MaxCommunities).ToList();
    }

    private async Task<List<GraphEntity>> ConvertToGraphEntitiesAsync(
        List<EnrichmentEntity> entities,
        List<EnrichedChunk> chunks,
        GraphBuildOptions options,
        CancellationToken ct)
    {
        var result = new List<GraphEntity>();

        foreach (var entity in entities)
        {
            float[]? embedding = null;
            if (options.GenerateEntityEmbeddings)
            {
                embedding = await _embeddingService.GenerateEmbeddingAsync(entity.Name, ct);
            }

            var chunkIds = chunks
                .Where(c => c.Entities.Any(e => e.NormalizedName == entity.NormalizedName))
                .Select(c => c.ChunkId)
                .Distinct()
                .ToList();

            var documentIds = chunks
                .Where(c => c.Entities.Any(e => e.NormalizedName == entity.NormalizedName))
                .Select(c => c.DocumentId)
                .Distinct()
                .ToList();

            result.Add(new GraphEntity
            {
                Id = entity.Id,
                Name = entity.Name,
                NormalizedName = entity.NormalizedName,
                Type = entity.Type,
                SurfaceForms = entity.SurfaceForms,
                Embedding = embedding,
                Confidence = entity.Confidence,
                MentionCount = entity.Spans.Count,
                ChunkIds = chunkIds,
                DocumentIds = documentIds,
                ExternalLinks = entity.ExternalLinks
            });
        }

        return result;
    }

    private static List<EnrichmentEntity> MergeEntities(List<EnrichmentEntity> entities)
    {
        return entities
            .GroupBy(e => e.NormalizedName.ToLowerInvariant())
            .Select(g =>
            {
                var items = g.ToList();
                var primary = items.OrderByDescending(e => e.Confidence).First();
                return primary with
                {
                    SurfaceForms = items.SelectMany(e => e.SurfaceForms).Distinct().ToList(),
                    ChunkIds = items.SelectMany(e => e.ChunkIds).Distinct().ToList(),
                    Confidence = items.Max(e => e.Confidence)
                };
            })
            .ToList();
    }

    private static List<string> ExtractTopicsFromChunks(IReadOnlyList<EnrichedChunk> chunks)
    {
        return chunks
            .SelectMany(c => c.Keywords)
            .GroupBy(k => k.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(20)
            .Select(g => g.Key)
            .ToList();
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enriching chunk {ChunkId} from document {DocumentId}")]
    private static partial void LogDocumentEnrichment7(ILogger logger, string chunkId, string documentId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Enriched chunk {ChunkId} in {ElapsedMs}ms (embeddings: {HasEmb}, entities: {EntityCount})")]
    private static partial void LogDocumentEnrichment6(ILogger logger, string chunkId, long elapsedMs, bool hasEmb, int entityCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error enriching chunk {ChunkId}")]
    private static partial void LogDocumentEnrichment5(ILogger logger, Exception exception, string chunkId);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Enriching batch of {Count} chunks")]
    private static partial void LogDocumentEnrichment4(ILogger logger, int count);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Enriching document {DocumentId} with {ChunkCount} chunks")]
    private static partial void LogDocumentEnrichment3(ILogger logger, string documentId, int chunkCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "Error extracting entities from content")]
    private static partial void LogDocumentEnrichment2(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate contextual summary, using fallback")]
    private static partial void LogDocumentEnrichment1(ILogger logger, Exception exception);

    #endregion
}
