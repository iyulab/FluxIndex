using FluxIndex.Service.Shared.Common;
using FluxIndex.Service.Shared.DTOs.Documents;

namespace FluxIndex.Service.Application.Interfaces.Services;

/// <summary>
/// Service interface for document operations.
/// </summary>
public interface IDocumentService
{
    Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DocumentDetailDto?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<DocumentDto>> GetPagedAsync(
        int page,
        int pageSize,
        Guid? collectionId = null,
        string? status = null,
        CancellationToken cancellationToken = default);
    Task<UploadDocumentResponse> UploadAsync(UploadDocumentRequest request, Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    Task<UploadDocumentResponse> UploadContentAsync(UploadDocumentContentRequest request, CancellationToken cancellationToken = default);
    Task<DocumentDto> UpdateAsync(Guid id, UpdateDocumentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ReindexAsync(Guid id, CancellationToken cancellationToken = default);
}
