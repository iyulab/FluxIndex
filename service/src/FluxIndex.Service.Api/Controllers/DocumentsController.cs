using FluxIndex.Service.Api.Middleware;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Documents;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Service.Api.Controllers;

/// <summary>
/// API controller for document management.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentService documentService,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all documents with pagination and optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DocumentDto>>>> GetDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? collectionId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _documentService.GetPagedAsync(page, pageSize, collectionId, status, cancellationToken);
        return Ok(ApiResponse<List<DocumentDto>>.Ok(result.Items, result.ToMetadata()));
    }

    /// <summary>
    /// Gets a document by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<DocumentDto>>> GetDocument(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentService.GetByIdAsync(id, cancellationToken);
        if (document == null)
        {
            return NotFound(ApiResponse<DocumentDto>.Fail($"Document with id '{id}' not found."));
        }

        return Ok(ApiResponse<DocumentDto>.Ok(document));
    }

    /// <summary>
    /// Gets a document with its chunks by ID.
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<ApiResponse<DocumentDetailDto>>> GetDocumentDetail(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentService.GetDetailByIdAsync(id, cancellationToken);
        if (document == null)
        {
            return NotFound(ApiResponse<DocumentDetailDto>.Fail($"Document with id '{id}' not found."));
        }

        return Ok(ApiResponse<DocumentDetailDto>.Ok(document));
    }

    /// <summary>
    /// Uploads a document file.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB limit
    public async Task<ActionResult<ApiResponse<UploadDocumentResponse>>> UploadDocument(
        [FromForm] string title,
        [FromForm] Guid? collectionId,
        [FromForm] string? sourceType,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<UploadDocumentResponse>.Fail("No file uploaded."));
        }

        var request = new UploadDocumentRequest
        {
            Title = title,
            CollectionId = collectionId,
            SourceType = sourceType
        };

        await using var stream = file.OpenReadStream();
        var response = await _documentService.UploadAsync(request, stream, file.FileName, cancellationToken);

        _logger.LogInformation("Document uploaded: {DocumentId} - {Title}", response.DocumentId, title);

        return CreatedAtAction(
            nameof(GetDocument),
            new { id = response.DocumentId },
            ApiResponse<UploadDocumentResponse>.Ok(response, "Document uploaded successfully."));
    }

    /// <summary>
    /// Uploads document content directly (text).
    /// </summary>
    [HttpPost("upload/content")]
    public async Task<ActionResult<ApiResponse<UploadDocumentResponse>>> UploadDocumentContent(
        [FromBody] UploadDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        var response = await _documentService.UploadContentAsync(request, cancellationToken);

        _logger.LogInformation("Document content uploaded: {DocumentId} - {Title}", response.DocumentId, request.Title);

        return CreatedAtAction(
            nameof(GetDocument),
            new { id = response.DocumentId },
            ApiResponse<UploadDocumentResponse>.Ok(response, "Document content uploaded successfully."));
    }

    /// <summary>
    /// Updates a document's metadata.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<DocumentDto>>> UpdateDocument(
        Guid id,
        [FromBody] UpdateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        try
        {
            var document = await _documentService.UpdateAsync(id, request, cancellationToken);
            _logger.LogInformation("Document updated: {DocumentId}", id);
            return Ok(ApiResponse<DocumentDto>.Ok(document, "Document updated successfully."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<DocumentDto>.Fail($"Document with id '{id}' not found."));
        }
    }

    /// <summary>
    /// Deletes a document.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteDocument(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsAdmin())
        {
            return Forbid();
        }

        try
        {
            await _documentService.DeleteAsync(id, cancellationToken);
            _logger.LogInformation("Document deleted: {DocumentId}", id);
            return Ok(ApiResponse<object>.Ok(null!, "Document deleted successfully."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail($"Document with id '{id}' not found."));
        }
    }

    /// <summary>
    /// Reindexes a document.
    /// </summary>
    [HttpPost("{id:guid}/reindex")]
    public async Task<ActionResult<ApiResponse<object>>> ReindexDocument(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        try
        {
            await _documentService.ReindexAsync(id, cancellationToken);
            _logger.LogInformation("Document queued for reindexing: {DocumentId}", id);
            return Ok(ApiResponse<object>.Ok(null!, "Document queued for reindexing."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail($"Document with id '{id}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }
}
