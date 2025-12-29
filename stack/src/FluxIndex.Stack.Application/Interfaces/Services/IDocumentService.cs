using FluxIndex.Stack.Shared.Common;
using FluxIndex.Stack.Shared.DTOs.Documents;

namespace FluxIndex.Stack.Application.Interfaces.Services;

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
    Task<GenerateQAResponse> GenerateQAAsync(Guid id, int maxPairs = 10, CancellationToken cancellationToken = default);
}
