using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using System.Globalization;

namespace FluxIndex.Storage.Neo4j;

/// <summary>
/// Neo4j implementation of IGraphStore for GraphRAG entity and relationship storage.
/// </summary>
public partial class Neo4jGraphStore : IGraphStore, IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly Neo4jOptions _options;
    private readonly ILogger<Neo4jGraphStore> _logger;
    private bool _indexesCreated;

    private const string EntityLabel = "Entity";
    private const string CommunityLabel = "Community";

    public Neo4jGraphStore(
        IOptions<Neo4jOptions> options,
        ILogger<Neo4jGraphStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _driver = CreateDriver();
    }

    private IDriver CreateDriver()
    {
        var builder = GraphDatabase.Driver(
            _options.Uri,
            AuthTokens.Basic(_options.Username, _options.Password),
            config => config
                .WithConnectionTimeout(TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds))
                .WithMaxConnectionPoolSize(_options.MaxConnectionPoolSize));

        return builder;
    }

    private async Task<IAsyncSession> GetSessionAsync()
    {
        var session = _options.Database is not null
            ? _driver.AsyncSession(o => o.WithDatabase(_options.Database))
            : _driver.AsyncSession();

        if (_options.CreateIndexesOnStartup && !_indexesCreated)
        {
            await EnsureIndexesAsync(session);
            _indexesCreated = true;
        }

        return session;
    }

    private async Task EnsureIndexesAsync(IAsyncSession session)
    {
        try
        {
            var indexQueries = new[]
            {
                $"CREATE INDEX IF NOT EXISTS FOR (e:{EntityLabel}) ON (e.id)",
                $"CREATE INDEX IF NOT EXISTS FOR (e:{EntityLabel}) ON (e.normalizedName)",
                $"CREATE INDEX IF NOT EXISTS FOR (e:{EntityLabel}) ON (e.type)",
                $"CREATE INDEX IF NOT EXISTS FOR (c:{CommunityLabel}) ON (c.id)",
                $"CREATE INDEX IF NOT EXISTS FOR (c:{CommunityLabel}) ON (c.level)",
            };

            foreach (var query in indexQueries)
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync(query);
                });
            }

            LogIndexesCreated(_logger);
        }
        catch (Exception ex)
        {
            LogIndexCreationFailed(_logger, ex);
        }
    }

    #region Entity Operations

    public async Task<string> StoreEntityAsync(GraphEntity entity, CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        var id = entity.Id ?? Guid.NewGuid().ToString();

        await session.ExecuteWriteAsync(async tx =>
        {
            var query = $@"
                MERGE (e:{EntityLabel} {{id: $id}})
                SET e.name = $name,
                    e.normalizedName = $normalizedName,
                    e.type = $type,
                    e.surfaceForms = $surfaceForms,
                    e.description = $description,
                    e.embedding = $embedding,
                    e.confidence = $confidence,
                    e.importanceScore = $importanceScore,
                    e.mentionCount = $mentionCount,
                    e.chunkIds = $chunkIds,
                    e.documentIds = $documentIds,
                    e.externalLinks = $externalLinks,
                    e.properties = $properties,
                    e.createdAt = $createdAt,
                    e.updatedAt = $updatedAt";

            var parameters = new
            {
                id,
                name = entity.Name,
                normalizedName = entity.NormalizedName,
                type = (int)entity.Type,
                surfaceForms = entity.SurfaceForms.ToList(),
                description = entity.Description,
                embedding = entity.Embedding?.ToList(),
                confidence = entity.Confidence,
                importanceScore = entity.ImportanceScore,
                mentionCount = entity.MentionCount,
                chunkIds = entity.ChunkIds.ToList(),
                documentIds = entity.DocumentIds.ToList(),
                externalLinks = SerializeDict(entity.ExternalLinks),
                properties = SerializeDict(entity.Properties),
                createdAt = entity.CreatedAt.ToString("O"),
                updatedAt = DateTimeOffset.UtcNow.ToString("O")
            };

            await tx.RunAsync(query, parameters);
        });

        LogEntityStored(_logger, id, entity.Name);
        return id;
    }

    public async Task<IReadOnlyList<string>> StoreEntitiesBatchAsync(
        IEnumerable<GraphEntity> entities,
        CancellationToken ct = default)
    {
        var entityList = entities.ToList();
        if (entityList.Count == 0) return [];

        await using var session = await GetSessionAsync();
        var ids = new List<string>();

        await session.ExecuteWriteAsync(async tx =>
        {
            foreach (var entity in entityList)
            {
                var id = entity.Id ?? Guid.NewGuid().ToString();
                ids.Add(id);

                var query = $@"
                    MERGE (e:{EntityLabel} {{id: $id}})
                    SET e.name = $name,
                        e.normalizedName = $normalizedName,
                        e.type = $type,
                        e.surfaceForms = $surfaceForms,
                        e.description = $description,
                        e.confidence = $confidence,
                        e.importanceScore = $importanceScore,
                        e.mentionCount = $mentionCount,
                        e.chunkIds = $chunkIds,
                        e.documentIds = $documentIds,
                        e.createdAt = $createdAt,
                        e.updatedAt = $updatedAt";

                var parameters = new
                {
                    id,
                    name = entity.Name,
                    normalizedName = entity.NormalizedName,
                    type = (int)entity.Type,
                    surfaceForms = entity.SurfaceForms.ToList(),
                    description = entity.Description,
                    confidence = entity.Confidence,
                    importanceScore = entity.ImportanceScore,
                    mentionCount = entity.MentionCount,
                    chunkIds = entity.ChunkIds.ToList(),
                    documentIds = entity.DocumentIds.ToList(),
                    createdAt = entity.CreatedAt.ToString("O"),
                    updatedAt = DateTimeOffset.UtcNow.ToString("O")
                };

                await tx.RunAsync(query, parameters);
            }
        });

        LogEntitiesBatchStored(_logger, ids.Count);
        return ids;
    }

    public async Task<GraphEntity?> GetEntityByIdAsync(string id, CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $"MATCH (e:{EntityLabel} {{id: $id}}) RETURN e";
            var cursor = await tx.RunAsync(query, new { id });

            if (await cursor.FetchAsync())
            {
                var node = cursor.Current["e"].As<INode>();
                return MapNodeToEntity(node);
            }

            return null;
        });
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByNameAsync(
        string name,
        bool fuzzyMatch = false,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            string query;
            object parameters;

            if (fuzzyMatch)
            {
                var normalizedName = name.ToLowerInvariant().Trim();
                query = $@"
                    MATCH (e:{EntityLabel})
                    WHERE toLower(e.name) CONTAINS $searchTerm
                       OR e.normalizedName CONTAINS $normalizedName
                    RETURN e";
                parameters = new { searchTerm = name.ToLowerInvariant(), normalizedName };
            }
            else
            {
                query = $@"
                    MATCH (e:{EntityLabel})
                    WHERE e.name = $name OR e.normalizedName = $normalizedName
                    RETURN e";
                parameters = new { name, normalizedName = name.ToLowerInvariant().Trim() };
            }

            var cursor = await tx.RunAsync(query, parameters);
            var entities = new List<GraphEntity>();

            while (await cursor.FetchAsync())
            {
                var node = cursor.Current["e"].As<INode>();
                entities.Add(MapNodeToEntity(node));
            }

            return entities;
        });
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByTypeAsync(
        NamedEntityType type,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $@"
                MATCH (e:{EntityLabel} {{type: $type}})
                RETURN e
                LIMIT $limit";

            var cursor = await tx.RunAsync(query, new { type = (int)type, limit });
            var entities = new List<GraphEntity>();

            while (await cursor.FetchAsync())
            {
                var node = cursor.Current["e"].As<INode>();
                entities.Add(MapNodeToEntity(node));
            }

            return entities;
        });
    }

    public async Task<bool> UpdateEntityAsync(GraphEntity entity, CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        var updated = await session.ExecuteWriteAsync(async tx =>
        {
            var query = $@"
                MATCH (e:{EntityLabel} {{id: $id}})
                SET e.name = $name,
                    e.normalizedName = $normalizedName,
                    e.type = $type,
                    e.surfaceForms = $surfaceForms,
                    e.description = $description,
                    e.embedding = $embedding,
                    e.confidence = $confidence,
                    e.importanceScore = $importanceScore,
                    e.mentionCount = $mentionCount,
                    e.chunkIds = $chunkIds,
                    e.documentIds = $documentIds,
                    e.externalLinks = $externalLinks,
                    e.properties = $properties,
                    e.updatedAt = $updatedAt
                RETURN e";

            var parameters = new
            {
                id = entity.Id,
                name = entity.Name,
                normalizedName = entity.NormalizedName,
                type = (int)entity.Type,
                surfaceForms = entity.SurfaceForms.ToList(),
                description = entity.Description,
                embedding = entity.Embedding?.ToList(),
                confidence = entity.Confidence,
                importanceScore = entity.ImportanceScore,
                mentionCount = entity.MentionCount,
                chunkIds = entity.ChunkIds.ToList(),
                documentIds = entity.DocumentIds.ToList(),
                externalLinks = SerializeDict(entity.ExternalLinks),
                properties = SerializeDict(entity.Properties),
                updatedAt = DateTimeOffset.UtcNow.ToString("O")
            };

            var cursor = await tx.RunAsync(query, parameters);
            return await cursor.FetchAsync();
        });

        return updated;
    }

    public async Task<bool> DeleteEntityAsync(string id, CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteWriteAsync(async tx =>
        {
            // Delete entity and all its relationships
            var query = $@"
                MATCH (e:{EntityLabel} {{id: $id}})
                DETACH DELETE e
                RETURN count(e) as deleted";

            var cursor = await tx.RunAsync(query, new { id });
            if (await cursor.FetchAsync())
            {
                var deleted = cursor.Current["deleted"].As<long>();
                return deleted > 0;
            }
            return false;
        });
    }

    #endregion

    #region Relationship Operations

    public async Task<string> StoreRelationshipAsync(
        GraphRelationship relationship,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        var id = relationship.Id ?? Guid.NewGuid().ToString();
        var relType = GetRelationshipTypeName(relationship.Type);

        await session.ExecuteWriteAsync(async tx =>
        {
            var query = $@"
                MATCH (source:{EntityLabel} {{id: $sourceId}})
                MATCH (target:{EntityLabel} {{id: $targetId}})
                MERGE (source)-[r:{relType} {{id: $id}}]->(target)
                SET r.label = $label,
                    r.type = $type,
                    r.confidence = $confidence,
                    r.weight = $weight,
                    r.isDirectional = $isDirectional,
                    r.evidenceChunkIds = $evidenceChunkIds,
                    r.evidenceTexts = $evidenceTexts,
                    r.properties = $properties,
                    r.createdAt = $createdAt";

            var parameters = new
            {
                id,
                sourceId = relationship.SourceEntityId,
                targetId = relationship.TargetEntityId,
                label = relationship.Label,
                type = (int)relationship.Type,
                confidence = relationship.Confidence,
                weight = relationship.Weight,
                isDirectional = relationship.IsDirectional,
                evidenceChunkIds = relationship.EvidenceChunkIds.ToList(),
                evidenceTexts = relationship.EvidenceTexts.ToList(),
                properties = SerializeDict(relationship.Properties),
                createdAt = relationship.CreatedAt.ToString("O")
            };

            await tx.RunAsync(query, parameters);
        });

        LogRelationshipStored(_logger, id, relationship.SourceEntityId, relationship.TargetEntityId);
        return id;
    }

    public async Task<IReadOnlyList<string>> StoreRelationshipsBatchAsync(
        IEnumerable<GraphRelationship> relationships,
        CancellationToken ct = default)
    {
        var relList = relationships.ToList();
        if (relList.Count == 0) return [];

        await using var session = await GetSessionAsync();
        var ids = new List<string>();

        await session.ExecuteWriteAsync(async tx =>
        {
            foreach (var rel in relList)
            {
                var id = rel.Id ?? Guid.NewGuid().ToString();
                ids.Add(id);
                var relType = GetRelationshipTypeName(rel.Type);

                var query = $@"
                    MATCH (source:{EntityLabel} {{id: $sourceId}})
                    MATCH (target:{EntityLabel} {{id: $targetId}})
                    MERGE (source)-[r:{relType} {{id: $id}}]->(target)
                    SET r.label = $label,
                        r.type = $type,
                        r.confidence = $confidence,
                        r.weight = $weight,
                        r.isDirectional = $isDirectional,
                        r.evidenceChunkIds = $evidenceChunkIds,
                        r.createdAt = $createdAt";

                var parameters = new
                {
                    id,
                    sourceId = rel.SourceEntityId,
                    targetId = rel.TargetEntityId,
                    label = rel.Label,
                    type = (int)rel.Type,
                    confidence = rel.Confidence,
                    weight = rel.Weight,
                    isDirectional = rel.IsDirectional,
                    evidenceChunkIds = rel.EvidenceChunkIds.ToList(),
                    createdAt = rel.CreatedAt.ToString("O")
                };

                await tx.RunAsync(query, parameters);
            }
        });

        LogRelationshipsBatchStored(_logger, ids.Count);
        return ids;
    }

    public async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsAsync(
        string entityId,
        TraversalDirection direction = TraversalDirection.Both,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = direction switch
            {
                TraversalDirection.Outgoing => $@"
                    MATCH (e:{EntityLabel} {{id: $entityId}})-[r]->(target)
                    RETURN r, e.id as sourceId, target.id as targetId",
                TraversalDirection.Incoming => $@"
                    MATCH (source)-[r]->(e:{EntityLabel} {{id: $entityId}})
                    RETURN r, source.id as sourceId, e.id as targetId",
                _ => $@"
                    MATCH (e:{EntityLabel} {{id: $entityId}})-[r]-(other)
                    RETURN r,
                           CASE WHEN startNode(r).id = $entityId THEN startNode(r).id ELSE endNode(r).id END as sourceId,
                           CASE WHEN startNode(r).id = $entityId THEN endNode(r).id ELSE startNode(r).id END as targetId"
            };

            var cursor = await tx.RunAsync(query, new { entityId });
            var relationships = new List<GraphRelationship>();

            while (await cursor.FetchAsync())
            {
                var rel = cursor.Current["r"].As<IRelationship>();
                var sourceId = cursor.Current["sourceId"].As<string>();
                var targetId = cursor.Current["targetId"].As<string>();
                relationships.Add(MapRelationshipToGraphRelationship(rel, sourceId, targetId));
            }

            return relationships;
        });
    }

    public async Task<IReadOnlyList<GraphRelationship>> GetRelationshipsByTypeAsync(
        RelationType type,
        int limit = 100,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var relType = GetRelationshipTypeName(type);
            var query = $@"
                MATCH (source)-[r:{relType}]->(target)
                RETURN r, source.id as sourceId, target.id as targetId
                LIMIT $limit";

            var cursor = await tx.RunAsync(query, new { limit });
            var relationships = new List<GraphRelationship>();

            while (await cursor.FetchAsync())
            {
                var rel = cursor.Current["r"].As<IRelationship>();
                var sourceId = cursor.Current["sourceId"].As<string>();
                var targetId = cursor.Current["targetId"].As<string>();
                relationships.Add(MapRelationshipToGraphRelationship(rel, sourceId, targetId));
            }

            return relationships;
        });
    }

    public async Task<bool> DeleteRelationshipAsync(string relationshipId, CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteWriteAsync(async tx =>
        {
            var query = @"
                MATCH ()-[r {id: $id}]-()
                DELETE r
                RETURN count(r) as deleted";

            var cursor = await tx.RunAsync(query, new { id = relationshipId });
            if (await cursor.FetchAsync())
            {
                var deleted = cursor.Current["deleted"].As<long>();
                return deleted > 0;
            }
            return false;
        });
    }

    #endregion

    #region Traversal Operations

    public async Task<GraphStoreTraversalResult> TraverseAsync(
        string startEntityId,
        GraphStoreTraversalOptions options,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var startEntity = await GetEntityByIdInternalAsync(tx, startEntityId);
            if (startEntity == null)
            {
                return new GraphStoreTraversalResult
                {
                    StartEntity = new GraphEntity { Id = startEntityId, Name = "Unknown" },
                    Entities = [],
                    Relationships = [],
                    Paths = new Dictionary<string, GraphPath>()
                };
            }

            // Cypher relationship pattern: -[r]-> (outgoing), <-[r]- (incoming), -[r]- (both)
            var (leftArrow, rightArrow) = options.Direction switch
            {
                TraversalDirection.Outgoing => ("", ">"),
                TraversalDirection.Incoming => ("<", ""),
                _ => ("", "")
            };

            var relTypeFilter = options.RelationTypes.Count > 0
                ? ":" + string.Join("|", options.RelationTypes.Select(GetRelationshipTypeName))
                : "";

            var query = $@"
                MATCH path = (start:{EntityLabel} {{id: $startId}}){leftArrow}-[r*1..{options.MaxDepth}]-{rightArrow}(end:{EntityLabel})
                WHERE ALL(rel IN r WHERE rel.weight >= $minWeight)
                RETURN DISTINCT end, path
                LIMIT $maxNodes";

            var parameters = new
            {
                startId = startEntityId,
                minWeight = options.MinWeight,
                maxNodes = options.MaxNodes
            };

            var cursor = await tx.RunAsync(query, parameters);
            var entities = new Dictionary<string, GraphEntity> { { startEntityId, startEntity } };
            var relationships = new Dictionary<string, GraphRelationship>();
            var paths = new Dictionary<string, GraphPath>();
            var maxDepthReached = 0;

            while (await cursor.FetchAsync())
            {
                var endNode = cursor.Current["end"].As<INode>();
                var entity = MapNodeToEntity(endNode);
                entities.TryAdd(entity.Id, entity);

                var pathObj = cursor.Current["path"].As<IPath>();
                var entityIds = new List<string> { startEntityId };
                var relationshipIds = new List<string>();
                double totalWeight = 0;

                foreach (var rel in pathObj.Relationships)
                {
                    var relId = rel.Properties.TryGetValue("id", out var idVal)
                        ? idVal.As<string>()
                        : Guid.NewGuid().ToString();
                    relationshipIds.Add(relId);

                    if (rel.Properties.TryGetValue("weight", out var weightVal))
                    {
                        totalWeight += weightVal.As<double>();
                    }
                }

                foreach (var node in pathObj.Nodes.Skip(1))
                {
                    var nodeId = node.Properties["id"].As<string>();
                    entityIds.Add(nodeId);
                }

                var graphPath = new GraphPath
                {
                    EntityIds = entityIds,
                    RelationshipIds = relationshipIds,
                    TotalWeight = totalWeight
                };

                paths.TryAdd(entity.Id, graphPath);
                maxDepthReached = Math.Max(maxDepthReached, graphPath.Length);
            }

            return new GraphStoreTraversalResult
            {
                StartEntity = startEntity,
                Entities = entities.Values.ToList(),
                Relationships = relationships.Values.ToList(),
                Paths = paths,
                MaxDepthReached = maxDepthReached,
                WasTruncated = entities.Count >= options.MaxNodes
            };
        });
    }

    public async Task<GraphPath?> FindShortestPathAsync(
        string sourceEntityId,
        string targetEntityId,
        int maxDepth = 5,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $@"
                MATCH path = shortestPath(
                    (source:{EntityLabel} {{id: $sourceId}})-[*1..{maxDepth}]-(target:{EntityLabel} {{id: $targetId}})
                )
                RETURN path";

            var cursor = await tx.RunAsync(query, new { sourceId = sourceEntityId, targetId = targetEntityId });

            if (await cursor.FetchAsync())
            {
                var pathObj = cursor.Current["path"].As<IPath>();
                var entityIds = new List<string>();
                var relationshipIds = new List<string>();
                double totalWeight = 0;

                foreach (var node in pathObj.Nodes)
                {
                    entityIds.Add(node.Properties["id"].As<string>());
                }

                foreach (var rel in pathObj.Relationships)
                {
                    var relId = rel.Properties.TryGetValue("id", out var idVal)
                        ? idVal.As<string>()
                        : Guid.NewGuid().ToString();
                    relationshipIds.Add(relId);

                    if (rel.Properties.TryGetValue("weight", out var weightVal))
                    {
                        totalWeight += weightVal.As<double>();
                    }
                }

                return new GraphPath
                {
                    EntityIds = entityIds,
                    RelationshipIds = relationshipIds,
                    TotalWeight = totalWeight
                };
            }

            return null;
        });
    }

    public async Task<IReadOnlyList<GraphEntity>> GetNeighborsAsync(
        string entityId,
        int depth = 1,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $@"
                MATCH (e:{EntityLabel} {{id: $entityId}})-[*1..{depth}]-(neighbor:{EntityLabel})
                WHERE neighbor.id <> $entityId
                RETURN DISTINCT neighbor";

            var cursor = await tx.RunAsync(query, new { entityId });
            var neighbors = new List<GraphEntity>();

            while (await cursor.FetchAsync())
            {
                var node = cursor.Current["neighbor"].As<INode>();
                neighbors.Add(MapNodeToEntity(node));
            }

            return neighbors;
        });
    }

    public async Task<IReadOnlyList<GraphEntity>> GetEntitiesByChunkIdsAsync(
        IEnumerable<string> chunkIds,
        CancellationToken ct = default)
    {
        var chunkIdList = chunkIds.ToList();
        if (chunkIdList.Count == 0) return [];

        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $@"
                MATCH (e:{EntityLabel})
                WHERE ANY(chunkId IN $chunkIds WHERE chunkId IN e.chunkIds)
                RETURN DISTINCT e";

            var cursor = await tx.RunAsync(query, new { chunkIds = chunkIdList });
            var entities = new List<GraphEntity>();

            while (await cursor.FetchAsync())
            {
                var node = cursor.Current["e"].As<INode>();
                entities.Add(MapNodeToEntity(node));
            }

            return entities;
        });
    }

    #endregion

    #region Community Operations

    public async Task<string> StoreCommunityAsync(
        GraphCommunity community,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        var id = community.Id ?? Guid.NewGuid().ToString();

        await session.ExecuteWriteAsync(async tx =>
        {
            var query = $@"
                MERGE (c:{CommunityLabel} {{id: $id}})
                SET c.name = $name,
                    c.summary = $summary,
                    c.entityIds = $entityIds,
                    c.topics = $topics,
                    c.importanceScore = $importanceScore,
                    c.level = $level,
                    c.parentCommunityId = $parentCommunityId,
                    c.embedding = $embedding,
                    c.createdAt = $createdAt";

            var parameters = new
            {
                id,
                name = community.Name,
                summary = community.Summary,
                entityIds = community.EntityIds.ToList(),
                topics = community.Topics.ToList(),
                importanceScore = community.ImportanceScore,
                level = community.Level,
                parentCommunityId = community.ParentCommunityId,
                embedding = community.Embedding?.ToList(),
                createdAt = community.CreatedAt.ToString("O")
            };

            await tx.RunAsync(query, parameters);

            // Create relationships from community to its entities
            foreach (var entityId in community.EntityIds)
            {
                var relQuery = $@"
                    MATCH (c:{CommunityLabel} {{id: $communityId}})
                    MATCH (e:{EntityLabel} {{id: $entityId}})
                    MERGE (c)-[:CONTAINS]->(e)";

                await tx.RunAsync(relQuery, new { communityId = id, entityId });
            }
        });

        LogCommunityStored(_logger, id, community.Name);
        return id;
    }

    public async Task<GraphCommunity?> GetCommunityByIdAsync(
        string communityId,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $"MATCH (c:{CommunityLabel} {{id: $id}}) RETURN c";
            var cursor = await tx.RunAsync(query, new { id = communityId });

            if (await cursor.FetchAsync())
            {
                var node = cursor.Current["c"].As<INode>();
                return MapNodeToCommunity(node);
            }

            return null;
        });
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetCommunitiesForEntityAsync(
        string entityId,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $@"
                MATCH (c:{CommunityLabel})-[:CONTAINS]->(e:{EntityLabel} {{id: $entityId}})
                RETURN c";

            var cursor = await tx.RunAsync(query, new { entityId });
            var communities = new List<GraphCommunity>();

            while (await cursor.FetchAsync())
            {
                var node = cursor.Current["c"].As<INode>();
                communities.Add(MapNodeToCommunity(node));
            }

            return communities;
        });
    }

    public async Task<IReadOnlyList<GraphCommunity>> GetTopCommunitiesAsync(
        int limit = 10,
        CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            var query = $@"
                MATCH (c:{CommunityLabel})
                RETURN c
                ORDER BY c.importanceScore DESC
                LIMIT $limit";

            var cursor = await tx.RunAsync(query, new { limit });
            var communities = new List<GraphCommunity>();

            while (await cursor.FetchAsync())
            {
                var node = cursor.Current["c"].As<INode>();
                communities.Add(MapNodeToCommunity(node));
            }

            return communities;
        });
    }

    #endregion

    #region Statistics and Maintenance

    public async Task<GraphStoreStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        return await session.ExecuteReadAsync(async tx =>
        {
            // Count entities
            var entityCountQuery = $"MATCH (e:{EntityLabel}) RETURN count(e) as count";
            var entityCursor = await tx.RunAsync(entityCountQuery);
            await entityCursor.FetchAsync();
            var entityCount = entityCursor.Current["count"].As<long>();

            // Count relationships
            var relCountQuery = $"MATCH (:{EntityLabel})-[r]-(:{EntityLabel}) RETURN count(r)/2 as count";
            var relCursor = await tx.RunAsync(relCountQuery);
            await relCursor.FetchAsync();
            var relCount = relCursor.Current["count"].As<long>();

            // Count communities
            var communityCountQuery = $"MATCH (c:{CommunityLabel}) RETURN count(c) as count";
            var communityCursor = await tx.RunAsync(communityCountQuery);
            await communityCursor.FetchAsync();
            var communityCount = communityCursor.Current["count"].As<long>();

            // Entity counts by type
            var entityByTypeQuery = $@"
                MATCH (e:{EntityLabel})
                RETURN e.type as type, count(e) as count";
            var entityByTypeCursor = await tx.RunAsync(entityByTypeQuery);
            var entityCountsByType = new Dictionary<NamedEntityType, long>();
            while (await entityByTypeCursor.FetchAsync())
            {
                var type = (NamedEntityType)entityByTypeCursor.Current["type"].As<int>();
                var count = entityByTypeCursor.Current["count"].As<long>();
                entityCountsByType[type] = count;
            }

            // Relationship counts by type
            var relByTypeQuery = $@"
                MATCH (:{EntityLabel})-[r]-(:{EntityLabel})
                RETURN r.type as type, count(r)/2 as count";
            var relByTypeCursor = await tx.RunAsync(relByTypeQuery);
            var relCountsByType = new Dictionary<RelationType, long>();
            while (await relByTypeCursor.FetchAsync())
            {
                if (relByTypeCursor.Current["type"] != null)
                {
                    var type = (RelationType)relByTypeCursor.Current["type"].As<int>();
                    var count = relByTypeCursor.Current["count"].As<long>();
                    relCountsByType[type] = count;
                }
            }

            var avgRelPerEntity = entityCount > 0 ? (double)relCount / entityCount : 0;

            return new GraphStoreStatistics
            {
                EntityCount = entityCount,
                RelationshipCount = relCount,
                CommunityCount = communityCount,
                EntityCountsByType = entityCountsByType,
                RelationshipCountsByType = relCountsByType,
                AverageRelationshipsPerEntity = avgRelPerEntity,
                LastUpdated = DateTimeOffset.UtcNow
            };
        });
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var session = await GetSessionAsync();

        await session.ExecuteWriteAsync(async tx =>
        {
            // Delete all nodes and relationships
            await tx.RunAsync($"MATCH (n:{EntityLabel}) DETACH DELETE n");
            await tx.RunAsync($"MATCH (n:{CommunityLabel}) DETACH DELETE n");
        });

        LogDataCleared(_logger);
    }

    #endregion

    #region Helper Methods

    private static async Task<GraphEntity?> GetEntityByIdInternalAsync(IAsyncQueryRunner tx, string id)
    {
        var query = $"MATCH (e:{EntityLabel} {{id: $id}}) RETURN e";
        var cursor = await tx.RunAsync(query, new { id });

        if (await cursor.FetchAsync())
        {
            var node = cursor.Current["e"].As<INode>();
            return MapNodeToEntity(node);
        }

        return null;
    }

    private static GraphEntity MapNodeToEntity(INode node)
    {
        var props = node.Properties;

        return new GraphEntity
        {
            Id = props["id"].As<string>(),
            Name = props["name"].As<string>(),
            NormalizedName = props.TryGetValue("normalizedName", out var nn) ? nn.As<string>() : string.Empty,
            Type = props.TryGetValue("type", out var t) ? (NamedEntityType)t.As<int>() : NamedEntityType.Unknown,
            SurfaceForms = props.TryGetValue("surfaceForms", out var sf) ? sf.As<List<object>>().Select(x => x.ToString()!).ToList() : [],
            Description = props.TryGetValue("description", out var d) ? d.As<string?>() : null,
            Embedding = props.TryGetValue("embedding", out var emb) && emb != null
                ? emb.As<List<object>>().Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture)).ToArray()
                : null,
            Confidence = props.TryGetValue("confidence", out var c) ? c.As<double>() : 0,
            ImportanceScore = props.TryGetValue("importanceScore", out var ims) ? ims.As<double>() : 0,
            MentionCount = props.TryGetValue("mentionCount", out var mc) ? mc.As<int>() : 0,
            ChunkIds = props.TryGetValue("chunkIds", out var cids) ? cids.As<List<object>>().Select(x => x.ToString()!).ToList() : [],
            DocumentIds = props.TryGetValue("documentIds", out var dids) ? dids.As<List<object>>().Select(x => x.ToString()!).ToList() : [],
            ExternalLinks = props.TryGetValue("externalLinks", out var el) ? DeserializeDict<string>(el.As<string>()) : new Dictionary<string, string>(),
            Properties = props.TryGetValue("properties", out var p) ? DeserializeDict<object>(p.As<string>()) : new Dictionary<string, object>(),
            CreatedAt = props.TryGetValue("createdAt", out var ca) ? DateTimeOffset.Parse(ca.As<string>(), CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow,
            UpdatedAt = props.TryGetValue("updatedAt", out var ua) ? DateTimeOffset.Parse(ua.As<string>(), CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow
        };
    }

    private static GraphRelationship MapRelationshipToGraphRelationship(IRelationship rel, string sourceId, string targetId)
    {
        var props = rel.Properties;

        return new GraphRelationship
        {
            Id = props.TryGetValue("id", out var id) ? id.As<string>() : Guid.NewGuid().ToString(),
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Type = props.TryGetValue("type", out var t) ? (RelationType)t.As<int>() : RelationType.RelatedTo,
            Label = props.TryGetValue("label", out var l) ? l.As<string>() : string.Empty,
            Confidence = props.TryGetValue("confidence", out var c) ? c.As<double>() : 0,
            Weight = props.TryGetValue("weight", out var w) ? w.As<double>() : 1.0,
            IsDirectional = props.TryGetValue("isDirectional", out var dir) && dir.As<bool>(),
            EvidenceChunkIds = props.TryGetValue("evidenceChunkIds", out var ecids)
                ? ecids.As<List<object>>().Select(x => x.ToString()!).ToList()
                : [],
            EvidenceTexts = props.TryGetValue("evidenceTexts", out var etexts)
                ? etexts.As<List<object>>().Select(x => x.ToString()!).ToList()
                : [],
            Properties = props.TryGetValue("properties", out var p) ? DeserializeDict<object>(p.As<string>()) : new Dictionary<string, object>(),
            CreatedAt = props.TryGetValue("createdAt", out var ca) ? DateTimeOffset.Parse(ca.As<string>(), CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow
        };
    }

    private static GraphCommunity MapNodeToCommunity(INode node)
    {
        var props = node.Properties;

        return new GraphCommunity
        {
            Id = props["id"].As<string>(),
            Name = props["name"].As<string>(),
            Summary = props.TryGetValue("summary", out var s) ? s.As<string?>() : null,
            EntityIds = props.TryGetValue("entityIds", out var eids) ? eids.As<List<object>>().Select(x => x.ToString()!).ToList() : [],
            Topics = props.TryGetValue("topics", out var topics) ? topics.As<List<object>>().Select(x => x.ToString()!).ToList() : [],
            ImportanceScore = props.TryGetValue("importanceScore", out var ims) ? ims.As<double>() : 0,
            Level = props.TryGetValue("level", out var lvl) ? lvl.As<int>() : 0,
            ParentCommunityId = props.TryGetValue("parentCommunityId", out var pcid) ? pcid.As<string?>() : null,
            Embedding = props.TryGetValue("embedding", out var emb) && emb != null
                ? emb.As<List<object>>().Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture)).ToArray()
                : null,
            CreatedAt = props.TryGetValue("createdAt", out var ca) ? DateTimeOffset.Parse(ca.As<string>(), CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow
        };
    }

    private static string GetRelationshipTypeName(RelationType type)
    {
        return type switch
        {
            RelationType.PartOf => "PART_OF",
            RelationType.LocatedIn => "LOCATED_IN",
            RelationType.WorksFor => "WORKS_FOR",
            RelationType.FoundedBy => "FOUNDED_BY",
            RelationType.Owns => "OWNS",
            RelationType.Uses => "USES",
            RelationType.RelatedTo => "RELATED_TO",
            RelationType.Causes => "CAUSES",
            RelationType.Enables => "ENABLES",
            RelationType.DependsOn => "DEPENDS_ON",
            RelationType.InheritsFrom => "INHERITS_FROM",
            RelationType.Implements => "IMPLEMENTS",
            RelationType.Contains => "CONTAINS",
            RelationType.Precedes => "PRECEDES",
            RelationType.Follows => "FOLLOWS",
            RelationType.IsTypeOf => "IS_TYPE_OF",
            RelationType.SynonymOf => "SYNONYM_OF",
            RelationType.OppositeOf => "OPPOSITE_OF",
            RelationType.ComparesTo => "COMPARES_TO",
            RelationType.Custom => "CUSTOM",
            _ => "RELATED_TO"
        };
    }

    private static string SerializeDict<T>(IReadOnlyDictionary<string, T> dict)
    {
        return System.Text.Json.JsonSerializer.Serialize(dict);
    }

    private static Dictionary<string, T> DeserializeDict<T>(string json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, T>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, T>>(json) ?? new Dictionary<string, T>();
        }
        catch
        {
            return new Dictionary<string, T>();
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Neo4j indexes created successfully")]
    private static partial void LogIndexesCreated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create Neo4j indexes (may already exist)")]
    private static partial void LogIndexCreationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored entity {EntityId} ({EntityName})")]
    private static partial void LogEntityStored(ILogger logger, string entityId, string entityName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored {Count} entities in batch")]
    private static partial void LogEntitiesBatchStored(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored relationship {RelId} from {Source} to {Target}")]
    private static partial void LogRelationshipStored(ILogger logger, string relId, string source, string target);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored {Count} relationships in batch")]
    private static partial void LogRelationshipsBatchStored(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored community {CommunityId} ({CommunityName})")]
    private static partial void LogCommunityStored(ILogger logger, string communityId, string communityName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared all data from Neo4j graph store")]
    private static partial void LogDataCleared(ILogger logger);

    #endregion
}
