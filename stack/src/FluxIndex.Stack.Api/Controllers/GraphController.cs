using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Graph;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for graph operations (GraphRAG).
/// Provides access to knowledge graph for relationship-aware retrieval.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public partial class GraphController : ControllerBase
{
    private readonly INeo4jGraphService _graphService;
    private readonly ILogger<GraphController> _logger;

    public GraphController(
        INeo4jGraphService graphService,
        ILogger<GraphController> logger)
    {
        _graphService = graphService;
        _logger = logger;
    }

    /// <summary>
    /// Gets graph statistics including node and relationship counts.
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(ApiResponse<GraphStatisticsResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GraphStatisticsResponse>>> GetStatistics(
        CancellationToken cancellationToken = default)
    {
        var stats = await _graphService.GetStatisticsAsync(cancellationToken);

        var response = new GraphStatisticsResponse
        {
            IsAvailable = _graphService.IsAvailable,
            TotalNodes = stats.TotalNodes,
            TotalRelationships = stats.TotalRelationships,
            TotalCommunities = stats.TotalCommunities,
            NodesByType = stats.NodesByType,
            RelationshipsByType = stats.RelationshipsByType
        };

        return Ok(ApiResponse<GraphStatisticsResponse>.Ok(response));
    }

    /// <summary>
    /// Gets graph service health status.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<ApiResponse<object>> GetHealth()
    {
        var health = new
        {
            IsAvailable = _graphService.IsAvailable,
            Service = "Neo4j",
            Status = _graphService.IsAvailable ? "Healthy" : "Unavailable"
        };

        if (!_graphService.IsAvailable)
        {
            return StatusCode(503, ApiResponse<object>.Fail("Neo4j graph service is not available"));
        }

        return Ok(ApiResponse<object>.Ok(health));
    }

    /// <summary>
    /// Gets entities related to the given entity IDs.
    /// </summary>
    [HttpPost("entities/related")]
    [ProducesResponseType(typeof(ApiResponse<GetRelatedEntitiesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<GetRelatedEntitiesResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<GetRelatedEntitiesResponse>>> GetRelatedEntities(
        [FromBody] GetRelatedEntitiesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EntityIds == null || request.EntityIds.Count == 0)
        {
            return BadRequest(ApiResponse<GetRelatedEntitiesResponse>.Fail("At least one entity ID is required."));
        }

        if (!_graphService.IsAvailable)
        {
            return Ok(ApiResponse<GetRelatedEntitiesResponse>.Ok(new GetRelatedEntitiesResponse
            {
                Relationships = new List<EntityRelationshipDto>(),
                TotalCount = 0
            }, "Graph service not available"));
        }

        var entityCount = request.EntityIds.Count;
        LogGettingRelatedEntities(_logger, entityCount, request.MaxHops);

        var relationships = await _graphService.GetRelatedEntitiesAsync(
            request.EntityIds,
            request.MaxHops,
            cancellationToken);

        var response = new GetRelatedEntitiesResponse
        {
            Relationships = relationships.Select(r => new EntityRelationshipDto
            {
                SourceEntityId = r.SourceEntityId,
                TargetEntityId = r.TargetEntityId,
                RelationshipType = r.RelationshipType,
                Properties = r.Properties
            }).ToList(),
            TotalCount = relationships.Count
        };

        return Ok(ApiResponse<GetRelatedEntitiesResponse>.Ok(response));
    }

    /// <summary>
    /// Expands a query with related entities from the knowledge graph.
    /// Useful for improving search recall by including semantically related terms.
    /// </summary>
    [HttpPost("query/expand")]
    [ProducesResponseType(typeof(ApiResponse<QueryExpansionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QueryExpansionResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<QueryExpansionResponse>>> ExpandQuery(
        [FromBody] QueryExpansionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(ApiResponse<QueryExpansionResponse>.Fail("Query cannot be empty."));
        }

        LogExpandingQuery(_logger, request.Query, request.MaxEntities);

        var relatedTerms = await _graphService.ExpandQueryWithRelatedEntitiesAsync(
            request.Query,
            request.MaxEntities,
            cancellationToken);

        var expandedQuery = relatedTerms.Count > 0
            ? $"{request.Query} {string.Join(" ", relatedTerms)}"
            : request.Query;

        var response = new QueryExpansionResponse
        {
            OriginalQuery = request.Query,
            RelatedTerms = relatedTerms,
            ExpandedQuery = expandedQuery
        };

        return Ok(ApiResponse<QueryExpansionResponse>.Ok(response));
    }

    /// <summary>
    /// Finds paths between two entities in the knowledge graph.
    /// </summary>
    [HttpPost("paths/find")]
    [ProducesResponseType(typeof(ApiResponse<FindPathsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FindPathsResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FindPathsResponse>>> FindPaths(
        [FromBody] FindPathsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceEntityId))
        {
            return BadRequest(ApiResponse<FindPathsResponse>.Fail("Source entity ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.TargetEntityId))
        {
            return BadRequest(ApiResponse<FindPathsResponse>.Fail("Target entity ID is required."));
        }

        if (!_graphService.IsAvailable)
        {
            return Ok(ApiResponse<FindPathsResponse>.Ok(new FindPathsResponse
            {
                Paths = new List<GraphPathDto>(),
                SourceEntityId = request.SourceEntityId,
                TargetEntityId = request.TargetEntityId
            }, "Graph service not available"));
        }

        LogFindingPaths(_logger, request.SourceEntityId, request.TargetEntityId, request.MaxPathLength);

        var paths = await _graphService.FindPathsAsync(
            request.SourceEntityId,
            request.TargetEntityId,
            request.MaxPathLength,
            cancellationToken);

        var response = new FindPathsResponse
        {
            Paths = paths.Select(p => new GraphPathDto
            {
                EntityIds = p.EntityIds,
                RelationshipTypes = p.RelationshipTypes,
                PathWeight = p.PathWeight
            }).ToList(),
            SourceEntityId = request.SourceEntityId,
            TargetEntityId = request.TargetEntityId
        };

        return Ok(ApiResponse<FindPathsResponse>.Ok(response));
    }

    /// <summary>
    /// Gets the community (cluster) for a specific entity.
    /// </summary>
    [HttpGet("entities/{entityId}/community")]
    [ProducesResponseType(typeof(ApiResponse<GraphCommunityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<GraphCommunityDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<GraphCommunityDto>>> GetEntityCommunity(
        [FromRoute] string entityId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return BadRequest(ApiResponse<GraphCommunityDto>.Fail("Entity ID is required."));
        }

        if (!_graphService.IsAvailable)
        {
            return NotFound(ApiResponse<GraphCommunityDto>.Fail("Graph service not available"));
        }

        var community = await _graphService.GetEntityCommunityAsync(entityId, cancellationToken);

        if (community == null)
        {
            return NotFound(ApiResponse<GraphCommunityDto>.Fail($"No community found for entity '{entityId}'"));
        }

        var response = new GraphCommunityDto
        {
            CommunityId = community.CommunityId,
            Name = community.Name,
            MemberEntityIds = community.MemberEntityIds,
            Summary = community.Summary,
            Level = community.Level
        };

        return Ok(ApiResponse<GraphCommunityDto>.Ok(response));
    }

    /// <summary>
    /// Gets chunks associated with the given entities.
    /// </summary>
    [HttpPost("entities/chunks")]
    [ProducesResponseType(typeof(ApiResponse<GetChunksForEntitiesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<GetChunksForEntitiesResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<GetChunksForEntitiesResponse>>> GetChunksForEntities(
        [FromBody] GetChunksForEntitiesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EntityIds == null || request.EntityIds.Count == 0)
        {
            return BadRequest(ApiResponse<GetChunksForEntitiesResponse>.Fail("At least one entity ID is required."));
        }

        var chunkIds = await _graphService.GetChunksForEntitiesAsync(
            request.EntityIds,
            cancellationToken);

        var response = new GetChunksForEntitiesResponse
        {
            ChunkIds = chunkIds,
            TotalCount = chunkIds.Count
        };

        return Ok(ApiResponse<GetChunksForEntitiesResponse>.Ok(response));
    }

    /// <summary>
    /// Stores an entity in the knowledge graph.
    /// </summary>
    [HttpPost("entities")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<object>>> StoreEntity(
        [FromBody] StoreEntityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return BadRequest(ApiResponse<object>.Fail("Entity ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<object>.Fail("Entity name is required."));
        }

        if (!_graphService.IsAvailable)
        {
            return StatusCode(503, ApiResponse<object>.Fail("Neo4j graph service is not available"));
        }

        LogStoringEntity(_logger, request.Id, request.Name, request.Type);

        var entity = new GraphEntity(
            request.Id,
            request.Name,
            request.Type ?? "Unknown",
            request.Properties,
            request.ChunkId,
            request.DocumentId);

        await _graphService.StoreEntityAsync(entity, cancellationToken);

        return StatusCode(201, ApiResponse<object>.Ok(new { EntityId = request.Id }, "Entity stored successfully"));
    }

    /// <summary>
    /// Stores a relationship between two entities.
    /// </summary>
    [HttpPost("relationships")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<object>>> StoreRelationship(
        [FromBody] StoreRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceEntityId))
        {
            return BadRequest(ApiResponse<object>.Fail("Source entity ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.TargetEntityId))
        {
            return BadRequest(ApiResponse<object>.Fail("Target entity ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.RelationshipType))
        {
            return BadRequest(ApiResponse<object>.Fail("Relationship type is required."));
        }

        if (!_graphService.IsAvailable)
        {
            return StatusCode(503, ApiResponse<object>.Fail("Neo4j graph service is not available"));
        }

        LogStoringRelationship(_logger, request.SourceEntityId, request.RelationshipType, request.TargetEntityId);

        await _graphService.StoreRelationshipAsync(
            request.SourceEntityId,
            request.TargetEntityId,
            request.RelationshipType,
            request.Properties,
            cancellationToken);

        return StatusCode(201, ApiResponse<object>.Ok(new
        {
            SourceEntityId = request.SourceEntityId,
            TargetEntityId = request.TargetEntityId,
            RelationshipType = request.RelationshipType
        }, "Relationship stored successfully"));
    }

    /// <summary>
    /// Links a chunk to entities found within it.
    /// </summary>
    [HttpPost("chunks/link")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<object>>> LinkChunkToEntities(
        [FromBody] LinkChunkToEntitiesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ChunkId == Guid.Empty)
        {
            return BadRequest(ApiResponse<object>.Fail("Chunk ID is required."));
        }

        if (request.EntityIds == null || request.EntityIds.Count == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("At least one entity ID is required."));
        }

        var linkedEntityCount = request.EntityIds.Count;
        LogLinkingChunkToEntities(_logger, request.ChunkId, linkedEntityCount);

        await _graphService.LinkChunkToEntitiesAsync(
            request.ChunkId,
            request.EntityIds,
            cancellationToken);

        return Ok(ApiResponse<object>.Ok(new
        {
            ChunkId = request.ChunkId,
            LinkedEntityCount = request.EntityIds.Count
        }));
    }

    /// <summary>
    /// Runs community detection algorithm on the graph.
    /// </summary>
    [HttpPost("communities/detect")]
    [ProducesResponseType(typeof(ApiResponse<RunCommunityDetectionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RunCommunityDetectionResponse>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<RunCommunityDetectionResponse>>> RunCommunityDetection(
        [FromBody] RunCommunityDetectionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!_graphService.IsAvailable)
        {
            return StatusCode(503, ApiResponse<RunCommunityDetectionResponse>.Fail("Neo4j graph service is not available"));
        }

        var stopwatch = Stopwatch.StartNew();

        var collectionIdStr = request?.CollectionId?.ToString() ?? "all";
        LogRunningCommunityDetection(_logger, collectionIdStr);

        var communitiesDetected = await _graphService.RunCommunityDetectionAsync(
            request?.CollectionId,
            cancellationToken);

        stopwatch.Stop();

        var response = new RunCommunityDetectionResponse
        {
            CommunitiesDetected = communitiesDetected,
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };

        LogCommunityDetectionCompleted(_logger, communitiesDetected, stopwatch.Elapsed.TotalMilliseconds);

        return Ok(ApiResponse<RunCommunityDetectionResponse>.Ok(response));
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting related entities for {Count} entities with {MaxHops} max hops")]
    private static partial void LogGettingRelatedEntities(ILogger logger, int count, int maxHops);

    [LoggerMessage(Level = LogLevel.Information, Message = "Expanding query '{Query}' with max {MaxEntities} entities")]
    private static partial void LogExpandingQuery(ILogger logger, string query, int maxEntities);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finding paths from {Source} to {Target} with max length {MaxLength}")]
    private static partial void LogFindingPaths(ILogger logger, string source, string target, int maxLength);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storing entity {EntityId} ({EntityName}) of type {EntityType}")]
    private static partial void LogStoringEntity(ILogger logger, string entityId, string entityName, string? entityType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storing relationship {Source} -[{Type}]-> {Target}")]
    private static partial void LogStoringRelationship(ILogger logger, string source, string type, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Linking chunk {ChunkId} to {Count} entities")]
    private static partial void LogLinkingChunkToEntities(ILogger logger, Guid chunkId, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Running community detection (collection: {CollectionId})")]
    private static partial void LogRunningCommunityDetection(ILogger logger, string collectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Community detection completed: {Count} communities in {Time}ms")]
    private static partial void LogCommunityDetectionCompleted(ILogger logger, int count, double time);

    #endregion
}
