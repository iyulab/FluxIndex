using FluxIndex.Domain.Models;

namespace FluxIndex.Interfaces;

/// <summary>
/// 청킹 서비스 인터페이스
/// </summary>
public interface IChunkingService
{
    /// <summary>
    /// 텍스트를 청크로 분할
    /// </summary>
    Task<IEnumerable<DocumentChunk>> ChunkTextAsync(
        string text,
        string documentId,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 문서를 청크로 분할
    /// </summary>
    Task<IEnumerable<DocumentChunk>> ChunkDocumentAsync(
        string documentPath,
        string documentId,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 청킹 전략별 분할
    /// </summary>
    Task<IEnumerable<DocumentChunk>> ChunkWithStrategyAsync(
        string text,
        string documentId,
        ChunkingStrategy strategy,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 청킹 옵션
/// </summary>
public class ChunkingOptions
{
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 64;
    public bool PreserveStructure { get; set; } = true;
    public string[]? CustomSeparators { get; set; }
}

/// <summary>
/// 청킹 전략
/// </summary>
public enum ChunkingStrategy
{
    Auto,
    Fixed,
    Sentence,
    Paragraph,
    Semantic,
    Sliding
}