using FluxIndex.Stack.Api.Middleware;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Chunks;
using FluxIndex.Stack.Shared.DTOs.Documents;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for chunk-level operations.
/// Provides CRUD operations for document chunks including content editing,
/// metadata management, and AI enrichment.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ChunksController : ControllerBase
{
    private readonly IChunkService _chunkService;
    private readonly ILogger<ChunksController> _logger;

    public ChunksController(
        IChunkService chunkService,
        ILogger<ChunksController> logger)
    {
        _chunkService = chunkService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all chunks with pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ChunkDetailDto>>>> GetChunks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _chunkService.GetPagedAsync(page, pageSize, documentId, cancellationToken);
        return Ok(ApiResponse<List<ChunkDetailDto>>.Ok(result.Items, result.ToMetadata()));
    }

    /// <summary>
    /// Gets chunks for a specific document.
    /// </summary>
    [HttpGet("document/{documentId:guid}")]
    public async Task<ActionResult<ApiResponse<List<DocumentChunkDto>>>> GetChunksByDocument(
        Guid documentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _chunkService.GetByDocumentIdAsync(documentId, page, pageSize, cancellationToken);
        return Ok(ApiResponse<List<DocumentChunkDto>>.Ok(result.Items, result.ToMetadata()));
    }

    /// <summary>
    /// Gets a chunk by ID with full details.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ChunkDetailDto>>> GetChunk(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkService.GetByIdAsync(id, cancellationToken);
        if (chunk == null)
        {
            return NotFound(ApiResponse<ChunkDetailDto>.Fail($"Chunk with id '{id}' not found."));
        }

        return Ok(ApiResponse<ChunkDetailDto>.Ok(chunk));
    }

    /// <summary>
    /// Updates a chunk's content and/or metadata.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ChunkDetailDto>>> UpdateChunk(
        Guid id,
        [FromBody] UpdateChunkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        try
        {
            var chunk = await _chunkService.UpdateAsync(id, request, cancellationToken);
            _logger.LogInformation("Chunk updated: {ChunkId}", id);
            return Ok(ApiResponse<ChunkDetailDto>.Ok(chunk, "Chunk updated successfully."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ChunkDetailDto>.Fail($"Chunk with id '{id}' not found."));
        }
    }

    /// <summary>
    /// Deletes a chunk.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteChunk(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        try
        {
            await _chunkService.DeleteAsync(id, cancellationToken);
            _logger.LogInformation("Chunk deleted: {ChunkId}", id);
            return Ok(ApiResponse<object>.Ok(null!, "Chunk deleted successfully."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail($"Chunk with id '{id}' not found."));
        }
    }

    /// <summary>
    /// Enriches a chunk with AI-generated metadata.
    /// </summary>
    [HttpPost("{id:guid}/enrich")]
    public async Task<ActionResult<ApiResponse<EnrichChunkResponse>>> EnrichChunk(
        Guid id,
        [FromBody] EnrichChunkRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        try
        {
            var response = await _chunkService.EnrichAsync(id, request ?? new EnrichChunkRequest(), cancellationToken);
            if (response.Success)
            {
                _logger.LogInformation("Chunk enriched: {ChunkId}", id);
                return Ok(ApiResponse<EnrichChunkResponse>.Ok(response, "Chunk enriched successfully."));
            }
            else
            {
                return BadRequest(ApiResponse<EnrichChunkResponse>.Fail(response.Message ?? "Enrichment failed."));
            }
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<EnrichChunkResponse>.Fail($"Chunk with id '{id}' not found."));
        }
    }

    /// <summary>
    /// Regenerates the embedding for a chunk.
    /// </summary>
    [HttpPost("{id:guid}/regenerate-embedding")]
    public async Task<ActionResult<ApiResponse<object>>> RegenerateEmbedding(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        try
        {
            await _chunkService.RegenerateEmbeddingAsync(id, cancellationToken);
            _logger.LogInformation("Embedding regenerated for chunk: {ChunkId}", id);
            return Ok(ApiResponse<object>.Ok(null!, "Embedding regenerated successfully."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail($"Chunk with id '{id}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }
}
