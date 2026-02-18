using FluxIndex.Stack.Api.Middleware;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Documents;
using Microsoft.AspNetCore.Mvc;

namespace FluxIndex.Stack.Api.Controllers;

/// <summary>
/// API controller for document management.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public partial class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IDocumentContentProvider? _contentProvider;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentService documentService,
        ILogger<DocumentsController> logger,
        IDocumentContentProvider? contentProvider = null)
    {
        _documentService = documentService;
        _contentProvider = contentProvider;
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

        LogDocumentUploaded(_logger, response.DocumentId, title);

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

        LogDocumentContentUploaded(_logger, response.DocumentId, request.Title);

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
            LogDocumentUpdated(_logger, id);
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
            LogDocumentDeleted(_logger, id);
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
            LogDocumentQueuedForReindexing(_logger, id);
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

    /// <summary>
    /// Generates Q&A pairs for a document using AI.
    /// </summary>
    [HttpPost("{id:guid}/generate-qa")]
    public async Task<ActionResult<ApiResponse<GenerateQAResponse>>> GenerateQA(
        Guid id,
        [FromQuery] int maxPairs = 10,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsWriter())
        {
            return Forbid();
        }

        try
        {
            var response = await _documentService.GenerateQAAsync(id, maxPairs, cancellationToken);
            LogGeneratedQAPairs(_logger, response.QAPairsGenerated, id);
            return Ok(ApiResponse<GenerateQAResponse>.Ok(response));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<GenerateQAResponse>.Fail($"Document with id '{id}' not found."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<GenerateQAResponse>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Gets a specific image from a document.
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <param name="imageId">Image ID (e.g., "img_001")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("{id:guid}/images/{imageId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<IActionResult> GetDocumentImage(
        Guid id,
        string imageId,
        CancellationToken cancellationToken = default)
    {
        if (_contentProvider == null)
        {
            return NotFound("Content provider not configured.");
        }

        var imageResult = await _contentProvider.GetImageAsync(id, imageId, cancellationToken);
        if (imageResult == null)
        {
            return NotFound($"Image '{imageId}' not found for document '{id}'.");
        }

        return File(imageResult.Value.Data, imageResult.Value.ContentType);
    }

    /// <summary>
    /// Lists all images for a document.
    /// </summary>
    /// <param name="id">Document ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("{id:guid}/images")]
    [ProducesResponseType(typeof(ApiResponse<DocumentImagesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DocumentImagesDto>>> GetDocumentImages(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // First check if document exists
        var document = await _documentService.GetByIdAsync(id, cancellationToken);
        if (document == null)
        {
            return NotFound(ApiResponse<DocumentImagesDto>.Fail($"Document with id '{id}' not found."));
        }

        if (_contentProvider == null)
        {
            return Ok(ApiResponse<DocumentImagesDto>.Ok(new DocumentImagesDto
            {
                DocumentId = id,
                ImageIds = Array.Empty<string>()
            }));
        }

        var imageIds = await _contentProvider.GetImageIdsAsync(id, cancellationToken);

        return Ok(ApiResponse<DocumentImagesDto>.Ok(new DocumentImagesDto
        {
            DocumentId = id,
            ImageIds = imageIds.ToArray()
        }));
    }

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Document uploaded: {DocumentId} - {Title}")]
    private static partial void LogDocumentUploaded(ILogger logger, Guid documentId, string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document content uploaded: {DocumentId} - {Title}")]
    private static partial void LogDocumentContentUploaded(ILogger logger, Guid documentId, string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document updated: {DocumentId}")]
    private static partial void LogDocumentUpdated(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document deleted: {DocumentId}")]
    private static partial void LogDocumentDeleted(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document queued for reindexing: {DocumentId}")]
    private static partial void LogDocumentQueuedForReindexing(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} Q&A pairs for document: {DocumentId}")]
    private static partial void LogGeneratedQAPairs(ILogger logger, int count, Guid documentId);

    #endregion
}

/// <summary>
/// DTO for document images list.
/// </summary>
public class DocumentImagesDto
{
    public Guid DocumentId { get; set; }
    public string[] ImageIds { get; set; } = Array.Empty<string>();
}
