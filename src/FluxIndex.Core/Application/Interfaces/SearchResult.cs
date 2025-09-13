using FluxIndex.Core.Domain.Entities;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// 검색 결과
/// </summary>
public class SearchResult
{
    public DocumentChunk Chunk { get; set; } = null!;
    public float Score { get; set; }
    
    // Backward compatibility properties
    public string DocumentId => Chunk.DocumentId;
    public string ChunkId => Chunk.Id;
    public string Content => Chunk.Content;
    public string FileName { get; set; } = string.Empty;
    public DocumentMetadata Metadata { get; set; } = new();
    public int ChunkIndex => Chunk.ChunkIndex;
    public int TotalChunks => Chunk.TotalChunks;
}