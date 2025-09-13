using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 간단한 텍스트 청킹 서비스
/// </summary>
public class SimpleChunkingService : IChunkingService
{
    private readonly int _defaultChunkSize;
    private readonly int _defaultChunkOverlap;

    public SimpleChunkingService(int defaultChunkSize = 512, int defaultChunkOverlap = 64)
    {
        _defaultChunkSize = defaultChunkSize;
        _defaultChunkOverlap = defaultChunkOverlap;
    }

    public Task<IEnumerable<DocumentChunk>> ChunkDocumentAsync(
        string content,
        int chunkSize = 512,
        int chunkOverlap = 64,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        var textChunks = ChunkText(content, chunkSize, chunkOverlap);
        
        int index = 0;
        foreach (var chunk in textChunks)
        {
            chunks.Add(new DocumentChunk(chunk, index++)
            {
                TokenCount = EstimateTokenCount(chunk)
            });
        }

        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    public IEnumerable<string> ChunkText(string text, int chunkSize = 512, int chunkOverlap = 64)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        chunkSize = chunkSize > 0 ? chunkSize : _defaultChunkSize;
        chunkOverlap = chunkOverlap >= 0 ? chunkOverlap : _defaultChunkOverlap;

        if (text.Length <= chunkSize)
        {
            yield return text;
            yield break;
        }

        int start = 0;
        while (start < text.Length)
        {
            int end = Math.Min(start + chunkSize, text.Length);
            yield return text.Substring(start, end - start);
            
            start += chunkSize - chunkOverlap;
            if (start + chunkOverlap >= text.Length)
                break;
        }
    }

    private int EstimateTokenCount(string text)
    {
        // Simple estimation: ~4 characters per token
        return text.Length / 4;
    }
}