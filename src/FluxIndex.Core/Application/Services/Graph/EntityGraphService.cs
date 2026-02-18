using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services.Graph;

/// <summary>
/// Entity Graph Service implementation for entity-centric indexing and retrieval.
/// Provides GraphRAG capabilities through entity-based search and traversal.
/// </summary>
public partial class EntityGraphService : IEntityGraphService
{
    private readonly IAdvancedEntityExtractionService? _entityExtractionService;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IGraphStore? _graphStore;
    private readonly ILogger<EntityGraphService> _logger;

    public EntityGraphService(
        IAdvancedEntityExtractionService? entityExtractionService = null,
        IEmbeddingService? embeddingService = null,
        IGraphStore? graphStore = null,
        ILogger<EntityGraphService>? logger = null)
    {
        _entityExtractionService = entityExtractionService;
        _embeddingService = embeddingService;
        _graphStore = graphStore;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EntityGraphService>.Instance;
    }

    /// <inheritdoc/>
    public async Task<EntityGraphResult> BuildEntityGraphAsync(
        IEnumerable<DocumentChunk> chunks,
        EntityGraphBuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EntityGraphBuildOptions();
        var stopwatch = Stopwatch.StartNew();

        var chunkList = chunks.ToList();
        LogEntityGraph8(_logger, chunkList.Count);

        var allEntities = new List<ExtractedEntity>();
        var allRelations = new List<EntityRelation>();
        var chunkMappings = new List<EntityChunkMapping>();
        var sourceChunkIds = new List<string>();

        // Process chunks in batches
        var batches = chunkList
            .Select((chunk, index) => new { chunk, index })
            .GroupBy(x => x.index / options.BatchSize)
            .Select(g => g.Select(x => x.chunk).ToList());

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchResults = await ProcessChunkBatchAsync(batch, options, cancellationToken);

            foreach (var result in batchResults)
            {
                sourceChunkIds.Add(result.ChunkId);

                // Filter by confidence
                var filteredEntities = result.Entities
                    .Where(e => e.Confidence >= options.MinEntityConfidence)
                    .Take(options.MaxEntitiesPerChunk)
                    .ToList();

                allEntities.AddRange(filteredEntities);

                // Create chunk mappings
                foreach (var entity in filteredEntities)
                {
                    chunkMappings.Add(new EntityChunkMapping
                    {
                        EntityId = entity.Id,
                        ChunkId = result.ChunkId,
                        MentionCount = entity.OccurrenceCount,
                        Positions = entity.Occurrences
                            .Select(o => (o.StartPosition, o.EndPosition))
                            .ToList(),
                        RelevanceScore = entity.Confidence
                    });
                }

                if (options.ExtractRelations)
                {
                    var filteredRelations = result.Relations
                        .Where(r => r.Confidence >= options.MinRelationConfidence)
                        .ToList();
                    allRelations.AddRange(filteredRelations);
                }
            }
        }

        // Link entities across chunks if enabled
        var entityNodes = new List<EntityNode>();
        var entityEdges = new List<EntityEdge>();

        if (options.LinkEntitiesAcrossChunks)
        {
            var (linkedNodes, updatedMappings) = LinkEntitiesAcrossChunks(allEntities, chunkMappings);
            entityNodes = linkedNodes;
            chunkMappings = updatedMappings;
        }
        else
        {
            entityNodes = allEntities.Select(e => ConvertToEntityNode(e)).ToList();
        }

        // Convert relations to edges
        entityEdges = allRelations.Select(r => ConvertToEntityEdge(r)).ToList();

        // Compute entity embeddings if enabled
        if (options.ComputeEntityEmbeddings && _embeddingService != null)
        {
            await ComputeEntityEmbeddingsAsync(entityNodes, cancellationToken);
        }

        // Compute statistics
        var stats = ComputeGraphStats(entityNodes, entityEdges, stopwatch.Elapsed.TotalMilliseconds);

        var entityGraphResult = new EntityGraphResult
        {
            Entities = entityNodes,
            Relations = entityEdges,
            ChunkMappings = chunkMappings,
            SourceChunkIds = sourceChunkIds,
            Stats = stats
        };

        // Persist to graph store if available
        if (_graphStore != null && options.PersistToGraphStore)
        {
            await PersistGraphAsync(entityGraphResult, cancellationToken);
        }

        stopwatch.Stop();
        LogEntityGraph7(_logger, entityNodes.Count, entityEdges.Count, stopwatch.Elapsed.TotalMilliseconds);

        return entityGraphResult;
    }

    /// <summary>
    /// Persists the entity graph to the configured graph store (Neo4j, etc.).
    /// </summary>
    /// <param name="graph">The entity graph to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PersistGraphAsync(EntityGraphResult graph, CancellationToken cancellationToken = default)
    {
        if (_graphStore == null)
        {
            LogEntityGraph6(_logger);
            return;
        }

        LogEntityGraph5(_logger, graph.Entities.Count, graph.Relations.Count);

        // Convert EntityNodes to GraphEntities and store
        var graphEntities = graph.Entities.Select(e => new GraphEntity
        {
            Id = e.Id,
            Name = e.Name,
            NormalizedName = e.NormalizedName,
            Type = e.Type,
            Confidence = e.Confidence,
            ImportanceScore = e.ImportanceScore,
            MentionCount = e.MentionCount,
            Embedding = e.Embedding,
            ExternalLinks = e.ExternalLinks,
            Properties = e.Properties
        }).ToList();

        if (graphEntities.Count > 0)
        {
            await _graphStore.StoreEntitiesBatchAsync(graphEntities, cancellationToken);
        }

        // Convert EntityEdges to GraphRelationships and store
        var relationships = graph.Relations.Select(r => new GraphRelationship
        {
            Id = r.Id,
            SourceEntityId = r.SourceEntityId,
            TargetEntityId = r.TargetEntityId,
            Type = r.RelationType,
            Label = r.Label,
            Confidence = r.Confidence,
            Weight = r.Weight,
            IsDirectional = r.IsDirectional,
            EvidenceChunkIds = r.EvidenceChunkIds,
            EvidenceTexts = r.EvidenceTexts,
            Properties = r.Properties
        }).ToList();

        if (relationships.Count > 0)
        {
            await _graphStore.StoreRelationshipsBatchAsync(relationships, cancellationToken);
        }

        LogEntityGraph4(_logger);
    }

    /// <inheritdoc/>
    public async Task<EntitySearchResult> SearchByEntitiesAsync(
        string query,
        EntityGraphResult entityGraph,
        EntitySearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EntitySearchOptions();
        var stopwatch = Stopwatch.StartNew();

        if (_logger.IsEnabled(LogLevel.Information))
            LogEntityGraph3(_logger, query);

        // Extract entities from query
        var queryEntities = await ExtractQueryEntitiesAsync(query, entityGraph, cancellationToken);

        if (queryEntities.Count == 0)
        {
            LogEntityGraph2(_logger);
            return new EntitySearchResult
            {
                Query = query,
                QueryEntities = Array.Empty<EntityNode>(),
                Hits = Array.Empty<EntitySearchHit>(),
                Stats = new EntitySearchStats
                {
                    SearchTimeMs = stopwatch.Elapsed.TotalMilliseconds
                }
            };
        }

        // Compute importance scores using PPR
        IReadOnlyDictionary<string, double> importanceScores;
        var pprIterations = 0;

        if (options.UsePersonalizedPageRank)
        {
            var pprOptions = new PersonalizedPageRankOptions
            {
                DampingFactor = options.DampingFactor,
                MaxIterations = options.MaxIterations
            };

            var pprResult = await ComputePersonalizedPageRankInternalAsync(
                entityGraph,
                queryEntities.Select(e => e.Id),
                pprOptions,
                cancellationToken);

            importanceScores = pprResult.Scores;
            pprIterations = pprResult.Iterations;
        }
        else
        {
            // Simple entity match scoring
            importanceScores = ComputeSimpleEntityScores(queryEntities, entityGraph);
        }

        // Score chunks based on entity importance
        var chunkScores = new Dictionary<string, (double Score, List<EntityNode> Entities)>();

        foreach (var mapping in entityGraph.ChunkMappings)
        {
            if (!importanceScores.TryGetValue(mapping.EntityId, out var entityScore))
                continue;

            var entity = entityGraph.Entities.FirstOrDefault(e => e.Id == mapping.EntityId);
            if (entity == null) continue;

            if (!chunkScores.TryGetValue(mapping.ChunkId, out var chunkData))
            {
                chunkData = (0, new List<EntityNode>());
            }

            chunkData.Score += entityScore * mapping.RelevanceScore;
            chunkData.Entities.Add(entity);
            chunkScores[mapping.ChunkId] = chunkData;
        }

        // Build search hits
        var hits = chunkScores
            .Where(kvp => kvp.Value.Score >= options.MinScore)
            .OrderByDescending(kvp => kvp.Value.Score)
            .Take(options.TopK)
            .Select(kvp => new EntitySearchHit
            {
                ChunkId = kvp.Key,
                Score = kvp.Value.Score,
                PprScore = kvp.Value.Score,
                EntityMatchScore = kvp.Value.Entities.Count(e =>
                    queryEntities.Any(qe => qe.Id == e.Id)) / (double)Math.Max(1, queryEntities.Count),
                Entities = kvp.Value.Entities,
                Explanation = options.IncludeExplanation
                    ? GenerateExplanation(kvp.Value.Entities, queryEntities)
                    : null
            })
            .ToList();

        // Find related entities
        var relatedEntities = FindRelatedEntities(queryEntities, entityGraph, 5);

        stopwatch.Stop();

        return new EntitySearchResult
        {
            Query = query,
            QueryEntities = queryEntities,
            Hits = hits,
            RelatedEntities = relatedEntities,
            Stats = new EntitySearchStats
            {
                EntitiesConsidered = entityGraph.Entities.Count,
                ChunksEvaluated = chunkScores.Count,
                PprIterations = pprIterations,
                SearchTimeMs = stopwatch.Elapsed.TotalMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public async Task<EntityTraversalResult> TraverseEntityRelationsAsync(
        IEnumerable<string> startEntities,
        EntityGraphResult entityGraph,
        EntityTraversalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EntityTraversalOptions();
        var stopwatch = Stopwatch.StartNew();

        var startEntityIds = startEntities.ToHashSet();
        var startNodes = entityGraph.Entities
            .Where(e => startEntityIds.Contains(e.Id) || startEntityIds.Contains(e.Name))
            .ToList();

        if (startNodes.Count == 0)
        {
            return new EntityTraversalResult
            {
                StartEntities = Array.Empty<EntityNode>(),
                Stats = new EntityTraversalStats { TraversalTimeMs = stopwatch.Elapsed.TotalMilliseconds }
            };
        }

        // Build adjacency list
        var adjacency = BuildAdjacencyList(entityGraph, options);

        // BFS traversal
        var visited = new HashSet<string>();
        var entitiesByHop = new Dictionary<int, List<EntityNode>>();
        var paths = new List<EntityPath>();
        var queue = new Queue<(EntityNode Entity, int Hop, List<EntityNode> Path, List<EntityEdge> Edges)>();

        // Initialize with start entities
        entitiesByHop[0] = startNodes.ToList();
        foreach (var startNode in startNodes)
        {
            visited.Add(startNode.Id);
            queue.Enqueue((startNode, 0, new List<EntityNode> { startNode }, new List<EntityEdge>()));
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, hop, path, edges) = queue.Dequeue();

            if (hop >= options.MaxHops)
            {
                if (path.Count > 1)
                {
                    paths.Add(new EntityPath
                    {
                        Entities = path.ToList(),
                        Relations = edges.ToList(),
                        Strength = edges.Count != 0 ? edges.Average(e => e.Weight) : 1.0
                    });
                }
                continue;
            }

            if (!adjacency.TryGetValue(current.Id, out var neighbors))
                continue;

            var hopEntities = entitiesByHop.GetValueOrDefault(hop + 1, new List<EntityNode>());
            var addedCount = 0;

            foreach (var (neighborId, edge) in neighbors)
            {
                if (visited.Contains(neighborId))
                    continue;

                if (addedCount >= options.MaxEntitiesPerHop)
                    break;

                var neighbor = entityGraph.Entities.FirstOrDefault(e => e.Id == neighborId);
                if (neighbor == null) continue;

                visited.Add(neighborId);
                hopEntities.Add(neighbor);
                addedCount++;

                var newPath = path.ToList();
                newPath.Add(neighbor);
                var newEdges = edges.ToList();
                newEdges.Add(edge);

                queue.Enqueue((neighbor, hop + 1, newPath, newEdges));
            }

            entitiesByHop[hop + 1] = hopEntities;
        }

        // Get relevant chunks if enabled
        var relevantChunks = new List<EntitySearchHit>();
        if (options.IncludeChunks)
        {
            var allVisitedEntityIds = visited.ToList();
            relevantChunks = GetChunksForEntityIds(allVisitedEntityIds, entityGraph);
        }

        stopwatch.Stop();

        return new EntityTraversalResult
        {
            StartEntities = startNodes,
            EntitiesByHop = entitiesByHop.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<EntityNode>)kvp.Value),
            Paths = paths,
            RelevantChunks = relevantChunks,
            Stats = new EntityTraversalStats
            {
                EntitiesVisited = visited.Count,
                MaxHopReached = entitiesByHop.Keys.DefaultIfEmpty(0).Max(),
                PathsDiscovered = paths.Count,
                TraversalTimeMs = stopwatch.Elapsed.TotalMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, double>> ComputeEntityImportanceAsync(
        EntityGraphResult entityGraph,
        IEnumerable<string>? seedEntities = null,
        PersonalizedPageRankOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PersonalizedPageRankOptions();
        var result = await ComputePersonalizedPageRankInternalAsync(
            entityGraph, seedEntities, options, cancellationToken);
        return result.Scores;
    }

    /// <inheritdoc/>
    public async Task<EntityGraphResult> MergeEntityGraphsAsync(
        IEnumerable<EntityGraphResult> graphs,
        EntityGraphMergeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EntityGraphMergeOptions();
        var stopwatch = Stopwatch.StartNew();

        var graphList = graphs.ToList();
        if (graphList.Count == 0)
        {
            return new EntityGraphResult();
        }

        if (graphList.Count == 1)
        {
            return graphList[0];
        }

        var mergedEntities = new Dictionary<string, EntityNode>();
        var mergedEdges = new List<EntityEdge>();
        var mergedMappings = new List<EntityChunkMapping>();
        var allSourceChunkIds = new List<string>();

        // Entity ID mapping: original ID -> merged ID
        var entityIdMapping = new Dictionary<string, string>();

        foreach (var graph in graphList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            allSourceChunkIds.AddRange(graph.SourceChunkIds);

            foreach (var entity in graph.Entities)
            {
                var matchedEntity = FindMatchingEntity(entity, mergedEntities.Values, options);

                if (matchedEntity != null)
                {
                    // Merge into existing entity
                    entityIdMapping[entity.Id] = matchedEntity.Id;
                    var merged = MergeEntityNodes(matchedEntity, entity);
                    mergedEntities[matchedEntity.Id] = merged;
                }
                else
                {
                    // Add as new entity
                    entityIdMapping[entity.Id] = entity.Id;
                    mergedEntities[entity.Id] = entity;
                }
            }

            // Update mappings with new entity IDs
            foreach (var mapping in graph.ChunkMappings)
            {
                var newEntityId = entityIdMapping.GetValueOrDefault(mapping.EntityId, mapping.EntityId);
                mergedMappings.Add(new EntityChunkMapping
                {
                    EntityId = newEntityId,
                    ChunkId = mapping.ChunkId,
                    MentionCount = mapping.MentionCount,
                    Positions = mapping.Positions,
                    RelevanceScore = mapping.RelevanceScore
                });
            }

            // Update edges with new entity IDs
            foreach (var edge in graph.Relations)
            {
                var newSourceId = entityIdMapping.GetValueOrDefault(edge.SourceEntityId, edge.SourceEntityId);
                var newTargetId = entityIdMapping.GetValueOrDefault(edge.TargetEntityId, edge.TargetEntityId);

                // Check for duplicate edges
                var existingEdge = mergedEdges.FirstOrDefault(e =>
                    e.SourceEntityId == newSourceId &&
                    e.TargetEntityId == newTargetId &&
                    e.RelationType == edge.RelationType);

                if (existingEdge != null && options.MergeRelationEvidence)
                {
                    // Merge evidence
                    var mergedEvidence = existingEdge.EvidenceChunkIds.Concat(edge.EvidenceChunkIds).Distinct().ToList();
                    var mergedTexts = existingEdge.EvidenceTexts.Concat(edge.EvidenceTexts).Distinct().ToList();

                    var index = mergedEdges.IndexOf(existingEdge);
                    mergedEdges[index] = new EntityEdge
                    {
                        Id = existingEdge.Id,
                        SourceEntityId = newSourceId,
                        TargetEntityId = newTargetId,
                        RelationType = existingEdge.RelationType,
                        Label = existingEdge.Label,
                        Confidence = Math.Max(existingEdge.Confidence, edge.Confidence),
                        Weight = Math.Max(existingEdge.Weight, edge.Weight),
                        IsDirectional = existingEdge.IsDirectional,
                        EvidenceChunkIds = mergedEvidence,
                        EvidenceTexts = mergedTexts
                    };
                }
                else
                {
                    mergedEdges.Add(new EntityEdge
                    {
                        Id = edge.Id,
                        SourceEntityId = newSourceId,
                        TargetEntityId = newTargetId,
                        RelationType = edge.RelationType,
                        Label = edge.Label,
                        Confidence = edge.Confidence,
                        Weight = edge.Weight,
                        IsDirectional = edge.IsDirectional,
                        EvidenceChunkIds = edge.EvidenceChunkIds.ToList(),
                        EvidenceTexts = edge.EvidenceTexts.ToList()
                    });
                }
            }
        }

        stopwatch.Stop();
        var entities = mergedEntities.Values.ToList();
        var stats = ComputeGraphStats(entities, mergedEdges, stopwatch.Elapsed.TotalMilliseconds);

        return new EntityGraphResult
        {
            Entities = entities,
            Relations = mergedEdges,
            ChunkMappings = mergedMappings,
            SourceChunkIds = allSourceChunkIds.Distinct().ToList(),
            Stats = stats
        };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<EntityChunkMapping>> GetChunksForEntitiesAsync(
        IEnumerable<string> entityIds,
        EntityGraphResult entityGraph,
        CancellationToken cancellationToken = default)
    {
        var entityIdSet = entityIds.ToHashSet();
        var mappings = entityGraph.ChunkMappings
            .Where(m => entityIdSet.Contains(m.EntityId))
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityChunkMapping>>(mappings);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BridgeEntity>> FindBridgeEntitiesAsync(
        EntityGraphResult entityGraph,
        BridgeEntityOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BridgeEntityOptions();

        var bridgeEntities = new List<BridgeEntity>();
        var adjacency = BuildSimpleAdjacencyList(entityGraph);

        // Compute degree centrality
        var degreeCentrality = new Dictionary<string, int>();
        foreach (var entity in entityGraph.Entities)
        {
            var degree = adjacency.GetValueOrDefault(entity.Id, new List<string>()).Count;
            degreeCentrality[entity.Id] = degree;
        }

        // Compute betweenness centrality if enabled
        var betweennessCentrality = new Dictionary<string, double>();
        if (options.ComputeBetweennessCentrality)
        {
            betweennessCentrality = await ComputeBetweennessCentralityAsync(
                entityGraph, adjacency, cancellationToken);
        }

        // Find bridge entities
        foreach (var entity in entityGraph.Entities)
        {
            var connections = adjacency.GetValueOrDefault(entity.Id, new List<string>());

            if (connections.Count < options.MinConnections)
                continue;

            var betweenness = betweennessCentrality.GetValueOrDefault(entity.Id, 0.0);
            var bridgeScore = (connections.Count / (double)entityGraph.Entities.Count) + betweenness;

            bridgeEntities.Add(new BridgeEntity
            {
                Entity = entity,
                BridgeScore = bridgeScore,
                BetweennessCentrality = betweenness,
                ClustersConnected = EstimateClustersConnected(entity.Id, adjacency, entityGraph),
                ConnectedEntityIds = connections
            });
        }

        return bridgeEntities
            .OrderByDescending(b => b.BridgeScore)
            .Take(options.TopN)
            .ToList();
    }

    #region Private Methods

    private async Task<List<(string ChunkId, List<ExtractedEntity> Entities, List<EntityRelation> Relations)>>
        ProcessChunkBatchAsync(
            List<DocumentChunk> chunks,
            EntityGraphBuildOptions options,
            CancellationToken cancellationToken)
    {
        var results = new List<(string ChunkId, List<ExtractedEntity> Entities, List<EntityRelation> Relations)>();

        if (_entityExtractionService == null)
        {
            LogEntityGraph1(_logger);
            foreach (var chunk in chunks)
            {
                results.Add((chunk.Id, new List<ExtractedEntity>(), new List<EntityRelation>()));
            }
            return results;
        }

        var extractionOptions = new EntityExtractionOptions
        {
            MinConfidence = options.MinEntityConfidence,
            MaxEntities = options.MaxEntitiesPerChunk,
            ExtractRelations = options.ExtractRelations,
            EntityTypes = options.EntityTypes?.ToList()
        };

        var contents = chunks.Select(c => c.Content).ToList();
        var graphs = await _entityExtractionService.ExtractBatchAsync(contents, extractionOptions, cancellationToken);

        for (var i = 0; i < chunks.Count; i++)
        {
            var graph = graphs.ElementAtOrDefault(i);
            if (graph != null)
            {
                results.Add((chunks[i].Id, graph.Entities.ToList(), graph.Relations.ToList()));
            }
            else
            {
                results.Add((chunks[i].Id, new List<ExtractedEntity>(), new List<EntityRelation>()));
            }
        }

        return results;
    }

    private static (List<EntityNode> Nodes, List<EntityChunkMapping> Mappings) LinkEntitiesAcrossChunks(
        List<ExtractedEntity> entities,
        List<EntityChunkMapping> mappings)
    {
        // Group entities by normalized text for linking
        var entityGroups = entities
            .GroupBy(e => NormalizeEntityText(e.Text, e.Type))
            .ToList();

        var linkedNodes = new List<EntityNode>();
        var updatedMappings = new List<EntityChunkMapping>();
        var oldToNewIdMap = new Dictionary<string, string>();

        foreach (var group in entityGroups)
        {
            var groupList = group.ToList();
            var canonicalEntity = groupList.OrderByDescending(e => e.Confidence).First();
            var newId = Guid.NewGuid().ToString();

            // Map all old IDs to new ID
            foreach (var entity in groupList)
            {
                oldToNewIdMap[entity.Id] = newId;
            }

            // Create linked node
            linkedNodes.Add(new EntityNode
            {
                Id = newId,
                Name = canonicalEntity.Text,
                NormalizedName = group.Key,
                Type = canonicalEntity.Type,
                SurfaceForms = groupList.Select(e => e.Text).Distinct().ToList(),
                Confidence = groupList.Average(e => e.Confidence),
                MentionCount = groupList.Sum(e => e.OccurrenceCount),
                ExternalLinks = canonicalEntity.ExternalLink != null
                    ? new Dictionary<string, string> { ["default"] = canonicalEntity.ExternalLink }
                    : new Dictionary<string, string>()
            });
        }

        // Update mappings with new entity IDs
        foreach (var mapping in mappings)
        {
            if (oldToNewIdMap.TryGetValue(mapping.EntityId, out var newId))
            {
                updatedMappings.Add(new EntityChunkMapping
                {
                    EntityId = newId,
                    ChunkId = mapping.ChunkId,
                    MentionCount = mapping.MentionCount,
                    Positions = mapping.Positions,
                    RelevanceScore = mapping.RelevanceScore
                });
            }
        }

        return (linkedNodes, updatedMappings);
    }

    private static string NormalizeEntityText(string text, NamedEntityType type)
    {
        var normalized = text.ToLowerInvariant().Trim();

        // Remove common prefixes/suffixes based on type
        if (type == NamedEntityType.Person)
        {
            normalized = normalized
                .Replace("mr. ", "")
                .Replace("mrs. ", "")
                .Replace("ms. ", "")
                .Replace("dr. ", "");
        }

        return normalized;
    }

    private static EntityNode ConvertToEntityNode(ExtractedEntity entity)
    {
        return new EntityNode
        {
            Id = entity.Id,
            Name = entity.Text,
            NormalizedName = entity.NormalizedText ?? NormalizeEntityText(entity.Text, entity.Type),
            Type = entity.Type,
            SurfaceForms = new List<string> { entity.Text },
            Confidence = entity.Confidence,
            MentionCount = entity.OccurrenceCount,
            ExternalLinks = entity.ExternalLink != null
                ? new Dictionary<string, string> { ["default"] = entity.ExternalLink }
                : new Dictionary<string, string>()
        };
    }

    private static EntityEdge ConvertToEntityEdge(EntityRelation relation)
    {
        return new EntityEdge
        {
            Id = relation.Id,
            SourceEntityId = relation.SourceEntityId,
            TargetEntityId = relation.TargetEntityId,
            RelationType = relation.Type,
            Label = relation.Label,
            Confidence = relation.Confidence,
            Weight = relation.Confidence,
            IsDirectional = relation.IsDirectional,
            EvidenceChunkIds = relation.SourceId != null
                ? new List<string> { relation.SourceId }
                : Array.Empty<string>(),
            EvidenceTexts = relation.Evidence != null
                ? new List<string> { relation.Evidence }
                : Array.Empty<string>()
        };
    }

    private async Task ComputeEntityEmbeddingsAsync(
        List<EntityNode> entities,
        CancellationToken cancellationToken)
    {
        if (_embeddingService == null) return;

        var texts = entities.Select(e => e.Name).ToList();
        var embeddings = (await _embeddingService.GenerateEmbeddingsBatchAsync(texts, cancellationToken)).ToList();

        for (var i = 0; i < entities.Count && i < embeddings.Count; i++)
        {
            var entity = entities[i];
            entities[i] = new EntityNode
            {
                Id = entity.Id,
                Name = entity.Name,
                NormalizedName = entity.NormalizedName,
                Type = entity.Type,
                SurfaceForms = entity.SurfaceForms,
                Confidence = entity.Confidence,
                MentionCount = entity.MentionCount,
                Embedding = embeddings[i],
                ExternalLinks = entity.ExternalLinks,
                Properties = entity.Properties
            };
        }
    }

    private static EntityGraphStats ComputeGraphStats(
        List<EntityNode> entities,
        List<EntityEdge> edges,
        double processingTimeMs)
    {
        var n = entities.Count;
        var e = edges.Count;
        var maxEdges = n > 1 ? n * (n - 1) : 1;

        var entitiesByType = entities
            .GroupBy(entity => entity.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var relationsByType = edges
            .GroupBy(edge => edge.RelationType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Count connected components using union-find
        var components = CountConnectedComponents(entities, edges);

        return new EntityGraphStats
        {
            TotalEntities = n,
            TotalRelations = e,
            EntitiesByType = entitiesByType,
            RelationsByType = relationsByType,
            ConnectedComponents = components,
            Density = e / (double)maxEdges,
            AverageDegree = n > 0 ? 2.0 * e / n : 0,
            ProcessingTimeMs = processingTimeMs
        };
    }

    private static int CountConnectedComponents(List<EntityNode> entities, List<EntityEdge> edges)
    {
        if (entities.Count == 0) return 0;

        var parent = entities.ToDictionary(e => e.Id, e => e.Id);

        string Find(string x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(string x, string y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py)
                parent[px] = py;
        }

        foreach (var edge in edges)
        {
            if (parent.ContainsKey(edge.SourceEntityId) && parent.ContainsKey(edge.TargetEntityId))
            {
                Union(edge.SourceEntityId, edge.TargetEntityId);
            }
        }

        return entities.Select(e => Find(e.Id)).Distinct().Count();
    }

    private async Task<List<EntityNode>> ExtractQueryEntitiesAsync(
        string query,
        EntityGraphResult entityGraph,
        CancellationToken cancellationToken)
    {
        var matchedEntities = new List<EntityNode>();

        // First, try to extract entities from query if extraction service is available
        if (_entityExtractionService != null)
        {
            var extracted = await _entityExtractionService.ExtractEntitiesAsync(
                query,
                new EntityExtractionOptions { MaxEntities = 10, UseLlm = false },
                cancellationToken);

            foreach (var entity in extracted)
            {
                var normalizedText = NormalizeEntityText(entity.Text, entity.Type);
                var match = entityGraph.Entities.FirstOrDefault(e =>
                    e.NormalizedName == normalizedText ||
                    e.SurfaceForms.Any(sf => NormalizeEntityText(sf, e.Type) == normalizedText));

                if (match != null && !matchedEntities.Contains(match))
                {
                    matchedEntities.Add(match);
                }
            }
        }

        // Fallback: simple text matching
        if (matchedEntities.Count == 0)
        {
            var queryLower = query.ToLowerInvariant();
            foreach (var entity in entityGraph.Entities)
            {
                if (queryLower.Contains(entity.NormalizedName) ||
                    entity.SurfaceForms.Any(sf => queryLower.Contains(sf, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedEntities.Add(entity);
                }
            }
        }

        return matchedEntities;
    }

    private static async Task<(IReadOnlyDictionary<string, double> Scores, int Iterations)>
        ComputePersonalizedPageRankInternalAsync(
            EntityGraphResult entityGraph,
            IEnumerable<string>? seedEntities,
            PersonalizedPageRankOptions options,
            CancellationToken cancellationToken)
    {
        var entities = entityGraph.Entities;
        var edges = entityGraph.Relations;
        var n = entities.Count;

        if (n == 0)
            return (new Dictionary<string, double>(), 0);

        // Initialize scores
        var scores = entities.ToDictionary(e => e.Id, _ => 1.0 / n);

        // Build adjacency with weights
        var inLinks = new Dictionary<string, List<(string SourceId, double Weight)>>();
        var outDegree = new Dictionary<string, double>();

        foreach (var entity in entities)
        {
            inLinks[entity.Id] = new List<(string, double)>();
            outDegree[entity.Id] = 0;
        }

        foreach (var edge in edges)
        {
            var weight = options.UseEdgeWeights ? edge.Weight : 1.0;

            if (inLinks.TryGetValue(edge.TargetEntityId, out var targetInLinks))
            {
                targetInLinks.Add((edge.SourceEntityId, weight));
            }

            if (outDegree.TryGetValue(edge.SourceEntityId, out var sourceOutDegree))
            {
                outDegree[edge.SourceEntityId] = sourceOutDegree + weight;
            }

            // Handle bidirectional edges
            if (!edge.IsDirectional)
            {
                if (inLinks.TryGetValue(edge.SourceEntityId, out var sourceInLinks))
                {
                    sourceInLinks.Add((edge.TargetEntityId, weight));
                }
                if (outDegree.TryGetValue(edge.TargetEntityId, out var targetOutDegree))
                {
                    outDegree[edge.TargetEntityId] = targetOutDegree + weight;
                }
            }
        }

        // Personalization vector
        var seedSet = seedEntities?.ToHashSet() ?? new HashSet<string>();
        var personalization = entities.ToDictionary(
            e => e.Id,
            e => seedSet.Contains(e.Id) ? options.PersonalizationWeight : (1.0 - options.PersonalizationWeight) / n);

        // Normalize personalization
        var pSum = personalization.Values.Sum();
        if (pSum > 0)
        {
            personalization = personalization.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / pSum);
        }

        // Power iteration
        var dampingFactor = options.DampingFactor;
        var iterations = 0;

        for (iterations = 0; iterations < options.MaxIterations; iterations++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var newScores = new Dictionary<string, double>();
            var diff = 0.0;

            foreach (var entity in entities)
            {
                var sum = 0.0;
                foreach (var (sourceId, weight) in inLinks[entity.Id])
                {
                    var sourceOut = outDegree.GetValueOrDefault(sourceId, 1.0);
                    if (sourceOut > 0)
                    {
                        sum += scores[sourceId] * weight / sourceOut;
                    }
                }

                var teleport = personalization.GetValueOrDefault(entity.Id, 1.0 / n);
                newScores[entity.Id] = (1 - dampingFactor) * teleport + dampingFactor * sum;
                diff += Math.Abs(newScores[entity.Id] - scores[entity.Id]);
            }

            scores = newScores;

            if (diff < options.ConvergenceThreshold)
            {
                iterations++;
                break;
            }
        }

        return (scores, iterations);
    }

    private static Dictionary<string, double> ComputeSimpleEntityScores(
        List<EntityNode> queryEntities,
        EntityGraphResult entityGraph)
    {
        var scores = new Dictionary<string, double>();
        var queryEntityIds = queryEntities.Select(e => e.Id).ToHashSet();

        foreach (var entity in entityGraph.Entities)
        {
            scores[entity.Id] = queryEntityIds.Contains(entity.Id) ? 1.0 : 0.1;
        }

        return scores;
    }

    private static Dictionary<string, List<(string NeighborId, EntityEdge Edge)>> BuildAdjacencyList(
        EntityGraphResult entityGraph,
        EntityTraversalOptions options)
    {
        var adjacency = new Dictionary<string, List<(string, EntityEdge)>>();

        foreach (var entity in entityGraph.Entities)
        {
            adjacency[entity.Id] = new List<(string, EntityEdge)>();
        }

        foreach (var edge in entityGraph.Relations)
        {
            // Filter by relation type
            if (options.RelationTypes != null && !options.RelationTypes.Contains(edge.RelationType))
                continue;

            // Filter by strength
            if (edge.Weight < options.MinRelationStrength)
                continue;

            if (adjacency.TryGetValue(edge.SourceEntityId, out var sourceAdj))
            {
                sourceAdj.Add((edge.TargetEntityId, edge));
            }

            if (options.BidirectionalTraversal && adjacency.TryGetValue(edge.TargetEntityId, out var targetAdj))
            {
                targetAdj.Add((edge.SourceEntityId, edge));
            }
        }

        return adjacency;
    }

    private static Dictionary<string, List<string>> BuildSimpleAdjacencyList(EntityGraphResult entityGraph)
    {
        var adjacency = new Dictionary<string, List<string>>();

        foreach (var entity in entityGraph.Entities)
        {
            adjacency[entity.Id] = new List<string>();
        }

        foreach (var edge in entityGraph.Relations)
        {
            if (adjacency.TryGetValue(edge.SourceEntityId, out var sourceAdj))
            {
                sourceAdj.Add(edge.TargetEntityId);
            }
            if (!edge.IsDirectional && adjacency.TryGetValue(edge.TargetEntityId, out var targetAdj))
            {
                targetAdj.Add(edge.SourceEntityId);
            }
        }

        return adjacency;
    }

    private static List<EntitySearchHit> GetChunksForEntityIds(
        List<string> entityIds,
        EntityGraphResult entityGraph)
    {
        var entityIdSet = entityIds.ToHashSet();
        var chunkScores = new Dictionary<string, (double Score, List<EntityNode> Entities)>();

        foreach (var mapping in entityGraph.ChunkMappings)
        {
            if (!entityIdSet.Contains(mapping.EntityId))
                continue;

            var entity = entityGraph.Entities.FirstOrDefault(e => e.Id == mapping.EntityId);
            if (entity == null) continue;

            if (!chunkScores.TryGetValue(mapping.ChunkId, out var data))
            {
                data = (0, new List<EntityNode>());
            }

            data.Score += mapping.RelevanceScore;
            data.Entities.Add(entity);
            chunkScores[mapping.ChunkId] = data;
        }

        return chunkScores
            .OrderByDescending(kvp => kvp.Value.Score)
            .Take(10)
            .Select(kvp => new EntitySearchHit
            {
                ChunkId = kvp.Key,
                Score = kvp.Value.Score,
                Entities = kvp.Value.Entities
            })
            .ToList();
    }

    private static List<EntityNode> FindRelatedEntities(
        List<EntityNode> queryEntities,
        EntityGraphResult entityGraph,
        int maxRelated)
    {
        var queryIds = queryEntities.Select(e => e.Id).ToHashSet();
        var relatedIds = new HashSet<string>();

        foreach (var edge in entityGraph.Relations)
        {
            if (queryIds.Contains(edge.SourceEntityId) && !queryIds.Contains(edge.TargetEntityId))
            {
                relatedIds.Add(edge.TargetEntityId);
            }
            if (queryIds.Contains(edge.TargetEntityId) && !queryIds.Contains(edge.SourceEntityId))
            {
                relatedIds.Add(edge.SourceEntityId);
            }
        }

        return entityGraph.Entities
            .Where(e => relatedIds.Contains(e.Id))
            .OrderByDescending(e => e.MentionCount)
            .Take(maxRelated)
            .ToList();
    }

    private static string GenerateExplanation(List<EntityNode> chunkEntities, List<EntityNode> queryEntities)
    {
        var matchedNames = chunkEntities
            .Where(ce => queryEntities.Any(qe => qe.Id == ce.Id))
            .Select(e => e.Name)
            .ToList();

        if (matchedNames.Count != 0)
        {
            return $"Contains query entities: {string.Join(", ", matchedNames)}";
        }

        var relatedNames = chunkEntities.Take(3).Select(e => e.Name);
        return $"Contains related entities: {string.Join(", ", relatedNames)}";
    }

    private static EntityNode? FindMatchingEntity(
        EntityNode entity,
        IEnumerable<EntityNode> existingEntities,
        EntityGraphMergeOptions options)
    {
        foreach (var existing in existingEntities)
        {
            if (existing.Type != entity.Type)
                continue;

            // Exact match
            if (existing.NormalizedName == entity.NormalizedName)
                return existing;

            // Surface form match
            if (existing.SurfaceForms.Any(sf =>
                NormalizeEntityText(sf, existing.Type) == entity.NormalizedName))
                return existing;

            if (entity.SurfaceForms.Any(sf =>
                NormalizeEntityText(sf, entity.Type) == existing.NormalizedName))
                return existing;

            // Fuzzy match
            if (options.UseFuzzyMatching)
            {
                var similarity = ComputeStringSimilarity(existing.NormalizedName, entity.NormalizedName);
                if (similarity >= options.EntitySimilarityThreshold)
                    return existing;
            }
        }

        return null;
    }

    private static EntityNode MergeEntityNodes(EntityNode existing, EntityNode newEntity)
    {
        var mergedSurfaceForms = existing.SurfaceForms
            .Concat(newEntity.SurfaceForms)
            .Distinct()
            .ToList();

        var mergedProperties = new Dictionary<string, object>(existing.Properties);
        foreach (var prop in newEntity.Properties)
        {
            mergedProperties[prop.Key] = prop.Value;
        }

        var mergedLinks = new Dictionary<string, string>(existing.ExternalLinks);
        foreach (var link in newEntity.ExternalLinks)
        {
            mergedLinks[link.Key] = link.Value;
        }

        return new EntityNode
        {
            Id = existing.Id,
            Name = existing.Name,
            NormalizedName = existing.NormalizedName,
            Type = existing.Type,
            SurfaceForms = mergedSurfaceForms,
            Confidence = Math.Max(existing.Confidence, newEntity.Confidence),
            MentionCount = existing.MentionCount + newEntity.MentionCount,
            Embedding = existing.Embedding ?? newEntity.Embedding,
            ExternalLinks = mergedLinks,
            Properties = mergedProperties
        };
    }

    private static double ComputeStringSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;

        var longer = a.Length >= b.Length ? a : b;
        var shorter = a.Length < b.Length ? a : b;

        if (longer.Length == 0)
            return 1.0;

        var editDistance = ComputeLevenshteinDistance(longer, shorter);
        return (longer.Length - editDistance) / (double)longer.Length;
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++)
            d[i, 0] = i;
        for (var j = 0; j <= m; j++)
            d[0, j] = j;

        for (var j = 1; j <= m; j++)
        {
            for (var i = 1; i <= n; i++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(
                    d[i - 1, j] + 1,
                    d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private static async Task<Dictionary<string, double>> ComputeBetweennessCentralityAsync(
        EntityGraphResult entityGraph,
        Dictionary<string, List<string>> adjacency,
        CancellationToken cancellationToken)
    {
        var betweenness = entityGraph.Entities.ToDictionary(e => e.Id, _ => 0.0);
        var n = entityGraph.Entities.Count;

        foreach (var source in entityGraph.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // BFS from source
            var dist = new Dictionary<string, int> { [source.Id] = 0 };
            var pred = new Dictionary<string, List<string>>();
            var sigma = new Dictionary<string, int> { [source.Id] = 1 };
            var queue = new Queue<string>();
            var stack = new Stack<string>();

            queue.Enqueue(source.Id);

            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                stack.Push(v);

                foreach (var w in adjacency.GetValueOrDefault(v, new List<string>()))
                {
                    if (!dist.TryGetValue(w, out _))
                    {
                        dist[w] = dist[v] + 1;
                        queue.Enqueue(w);
                    }

                    if (dist[w] == dist[v] + 1)
                    {
                        if (!sigma.ContainsKey(w)) sigma[w] = 0;
                        sigma[w] += sigma[v];

                        if (!pred.ContainsKey(w)) pred[w] = new List<string>();
                        pred[w].Add(v);
                    }
                }
            }

            // Accumulation
            var delta = entityGraph.Entities.ToDictionary(e => e.Id, _ => 0.0);

            while (stack.Count > 0)
            {
                var w = stack.Pop();
                foreach (var v in pred.GetValueOrDefault(w, new List<string>()))
                {
                    delta[v] += (sigma[v] / (double)sigma.GetValueOrDefault(w, 1)) * (1 + delta[w]);
                }

                if (w != source.Id)
                {
                    betweenness[w] += delta[w];
                }
            }
        }

        // Normalize
        if (n > 2)
        {
            var norm = 2.0 / ((n - 1) * (n - 2));
            betweenness = betweenness.ToDictionary(kvp => kvp.Key, kvp => kvp.Value * norm);
        }

        return betweenness;
    }

    private static int EstimateClustersConnected(
        string entityId,
        Dictionary<string, List<string>> adjacency,
        EntityGraphResult entityGraph)
    {
        var neighbors = adjacency.GetValueOrDefault(entityId, new List<string>());
        if (neighbors.Count <= 1) return 1;

        // Check how many neighbor groups are not connected to each other
        var visited = new HashSet<string>();
        var clusters = 0;

        foreach (var neighbor in neighbors)
        {
            if (visited.Contains(neighbor)) continue;

            // BFS to find cluster
            var queue = new Queue<string>();
            queue.Enqueue(neighbor);
            visited.Add(neighbor);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in adjacency.GetValueOrDefault(current, new List<string>()))
                {
                    if (next == entityId) continue; // Skip the bridge entity
                    if (neighbors.Contains(next) && !visited.Contains(next))
                    {
                        visited.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }

            clusters++;
        }

        return clusters;
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Building entity graph from {ChunkCount} chunks")]
    private static partial void LogEntityGraph8(ILogger logger, int chunkCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Built entity graph with {EntityCount} entities and {RelationCount} relations in {ElapsedMs:F2}ms")]
    private static partial void LogEntityGraph7(ILogger logger, int entityCount, int relationCount, double elapsedMs);
    [LoggerMessage(Level = LogLevel.Warning, Message = "No graph store configured, skipping persistence")]
    private static partial void LogEntityGraph6(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Persisting entity graph to graph store: {EntityCount} entities, {RelationCount} relations")]
    private static partial void LogEntityGraph5(ILogger logger, int entityCount, int relationCount);
    [LoggerMessage(Level = LogLevel.Information, Message = "Entity graph persisted to graph store successfully")]
    private static partial void LogEntityGraph4(ILogger logger);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Searching by entities for query: {Query}")]
    private static partial void LogEntityGraph3(ILogger logger, string query);
    [LoggerMessage(Level = LogLevel.Debug, Message = "No entities found in query, returning empty results")]
    private static partial void LogEntityGraph2(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Entity extraction service not available, returning empty results")]
    private static partial void LogEntityGraph1(ILogger logger);

    #endregion
}
