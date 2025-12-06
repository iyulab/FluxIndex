using System.Security.Cryptography;
using System.Text;
using FluxIndex.Service.Application.Interfaces.Repositories;
using FluxIndex.Service.Application.Interfaces.Services;
using FluxIndex.Service.Application.Mappings;
using FluxIndex.Service.Domain.Entities;
using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Documents;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Service.Application.Services;

/// <summary>
/// Service implementation for document operations.
/// </summary>
public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IIndexingService? _indexingService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        ILogger<DocumentService> logger,
        IIndexingService? indexingService = null)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _indexingService = indexingService;
        _logger = logger;
    }

    public async Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        return document?.ToDto();
    }

    public async Task<DocumentDetailDto?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdWithChunksAsync(id, cancellationToken);
        if (document == null) return null;

        var chunks = await _chunkRepository.GetByDocumentIdAsync(id, cancellationToken);
        return document.ToDetailDto(chunks);
    }

    public async Task<PagedResult<DocumentDto>> GetPagedAsync(
        int page,
        int pageSize,
        Guid? collectionId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        DocumentStatus? statusEnum = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DocumentStatus>(status, true, out var parsed))
        {
            statusEnum = parsed;
        }

        var (items, totalCount) = await _documentRepository.GetPagedAsync(
            page, pageSize, collectionId, statusEnum, cancellationToken);

        var dtos = items.Select(d => d.ToDto()).ToList();
        return PagedResult<DocumentDto>.Create(dtos, page, pageSize, totalCount);
    }

    public async Task<UploadDocumentResponse> UploadAsync(
        UploadDocumentRequest request,
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // Read content from stream
        using var reader = new StreamReader(fileStream);
        var content = await reader.ReadToEndAsync(cancellationToken);

        // Compute content hash
        var contentHash = ComputeHash(content);
        var fileSize = fileStream.Length;

        // Check for duplicate
        if (await _documentRepository.ContentHashExistsAsync(contentHash, cancellationToken))
        {
            _logger.LogWarning("Document with same content already exists: {Hash}", contentHash);
        }

        // Create document
        var document = Document.Create(
            request.Title,
            request.CollectionId,
            request.SourceType ?? "file",
            fileName);

        document.SetContentHash(contentHash, fileSize);

        if (request.Metadata != null)
        {
            document.SetMetadata(request.Metadata);
        }

        await _documentRepository.AddAsync(document, cancellationToken);

        _logger.LogInformation("Document uploaded: {DocumentId} - {Title}", document.Id, document.Title);

        // Queue for indexing if service available
        Guid? jobId = null;
        if (_indexingService != null)
        {
            try
            {
                jobId = await _indexingService.QueueIndexingJobAsync(document.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue document for indexing: {DocumentId}", document.Id);
            }
        }

        return new UploadDocumentResponse
        {
            DocumentId = document.Id,
            JobId = jobId,
            Status = document.Status.ToString(),
            Message = jobId.HasValue
                ? "Document uploaded and queued for indexing."
                : "Document uploaded successfully."
        };
    }

    public async Task<UploadDocumentResponse> UploadContentAsync(
        UploadDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        // Compute content hash
        var contentHash = ComputeHash(request.Content);
        var fileSize = Encoding.UTF8.GetByteCount(request.Content);

        // Create document
        var document = Document.Create(
            request.Title,
            request.CollectionId,
            request.SourceType ?? "text");

        document.SetContentHash(contentHash, fileSize);

        if (request.Metadata != null)
        {
            document.SetMetadata(request.Metadata);
        }

        await _documentRepository.AddAsync(document, cancellationToken);

        _logger.LogInformation("Document content uploaded: {DocumentId} - {Title}", document.Id, document.Title);

        // Queue for indexing if service available
        Guid? jobId = null;
        if (_indexingService != null)
        {
            try
            {
                jobId = await _indexingService.QueueIndexingJobAsync(document.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue document for indexing: {DocumentId}", document.Id);
            }
        }

        return new UploadDocumentResponse
        {
            DocumentId = document.Id,
            JobId = jobId,
            Status = document.Status.ToString(),
            Message = jobId.HasValue
                ? "Document content uploaded and queued for indexing."
                : "Document content uploaded successfully."
        };
    }

    public async Task<DocumentDto> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Document with id '{id}' not found.");

        document.Update(request.Title, request.Metadata);
        await _documentRepository.UpdateAsync(document, cancellationToken);

        _logger.LogInformation("Document updated: {DocumentId}", id);

        return document.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _documentRepository.ExistsAsync(id, cancellationToken))
        {
            throw new KeyNotFoundException($"Document with id '{id}' not found.");
        }

        // Chunks are deleted via cascade
        await _documentRepository.DeleteAsync(id, cancellationToken);

        _logger.LogInformation("Document deleted: {DocumentId}", id);
    }

    public async Task ReindexAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Document with id '{id}' not found.");

        if (_indexingService == null)
        {
            throw new InvalidOperationException("Indexing service is not available.");
        }

        // Delete existing chunks
        await _chunkRepository.DeleteByDocumentIdAsync(id, cancellationToken);

        // Mark as pending
        document.MarkAsProcessing();
        await _documentRepository.UpdateAsync(document, cancellationToken);

        // Re-queue for indexing (content would need to be retrieved from storage)
        _logger.LogInformation("Document queued for reindexing: {DocumentId}", id);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
