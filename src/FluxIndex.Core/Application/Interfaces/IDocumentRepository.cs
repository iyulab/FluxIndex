using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Domain.Entities;

namespace FluxIndex.Core.Interfaces;

/// <summary>
/// 문서 리포지토리 인터페이스
/// </summary>
public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Document?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IEnumerable<Document>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Document>> GetByStatusAsync(DocumentStatus status, CancellationToken cancellationToken = default);
    Task<string> AddAsync(Document document, CancellationToken cancellationToken = default);
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Document>> SearchByKeywordAsync(string keyword, int maxResults, CancellationToken cancellationToken = default);
}