using FluxIndex.Domain.Entities;
using FluxIndex.Domain.Models;

namespace FluxIndex.Interfaces;

/// <summary>
/// 문서 리포지토리 인터페이스
/// </summary>
public interface IDocumentRepository
{
    /// <summary>
    /// ID로 문서 조회
    /// </summary>
    Task<Document?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 파일 경로로 문서 조회
    /// </summary>
    Task<Document?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 모든 문서 조회
    /// </summary>
    Task<IEnumerable<Document>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 상태별 문서 조회
    /// </summary>
    Task<IEnumerable<Document>> GetByStatusAsync(DocumentStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 추가
    /// </summary>
    Task<string> AddAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 업데이트
    /// </summary>
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 삭제
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 존재 여부 확인
    /// </summary>
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서 수 조회
    /// </summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 키워드로 문서 검색
    /// </summary>
    Task<IEnumerable<Document>> SearchByKeywordAsync(string keyword, int maxResults, CancellationToken cancellationToken = default);
}