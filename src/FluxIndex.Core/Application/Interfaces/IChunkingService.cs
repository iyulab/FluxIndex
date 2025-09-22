using FluxIndex.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 문서 청킹 서비스 인터페이스
/// </summary>
public interface IChunkingService
{
    /// <summary>
    /// 문서를 청크로 분할
    /// </summary>
    Task<IEnumerable<DocumentChunk>> ChunkDocumentAsync(
        string content,
        int chunkSize = 512,
        int chunkOverlap = 64,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 텍스트를 청크로 분할
    /// </summary>
    IEnumerable<string> ChunkText(
        string text,
        int chunkSize = 512,
        int chunkOverlap = 64);
}