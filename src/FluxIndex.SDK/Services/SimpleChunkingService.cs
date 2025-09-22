using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.SDK.Services;

/// <summary>
/// 간단한 청킹 서비스 구현
/// </summary>
public class SimpleChunkingService : IChunkingService
{
    private readonly int _defaultChunkSize;
    private readonly int _defaultOverlap;

    public SimpleChunkingService(int defaultChunkSize = 512, int defaultOverlap = 64)
    {
        _defaultChunkSize = defaultChunkSize;
        _defaultOverlap = defaultOverlap;
    }

    public Task<IEnumerable<DocumentChunk>> ChunkDocumentAsync(
        string content,
        int chunkSize = 512,
        int chunkOverlap = 64,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
        }

        var chunkTexts = SplitIntoChunks(content, chunkSize, chunkOverlap);

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            var chunk = DocumentChunk.Create(
                "temp", // DocumentId will be set by caller
                chunkTexts[i],
                i,
                chunkTexts.Count // totalChunks
            );
            chunks.Add(chunk);
        }

        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    public IEnumerable<string> ChunkText(
        string text,
        int chunkSize = 512,
        int chunkOverlap = 64)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Enumerable.Empty<string>();
        }

        return SplitIntoChunks(text, chunkSize, chunkOverlap);
    }

    private List<string> SplitIntoChunks(string text, int chunkSize, int chunkOverlap)
    {
        var chunks = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return chunks;
        }

        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var word in words)
        {
            var wordLength = word.Length + 1; // +1 for space

            if (currentLength + wordLength > chunkSize && currentChunk.Count > 0)
            {
                // Create chunk
                chunks.Add(string.Join(" ", currentChunk));

                // Start new chunk with overlap
                var overlapWords = Math.Min(chunkOverlap / 10, currentChunk.Count); // Rough estimate
                if (overlapWords > 0)
                {
                    currentChunk = currentChunk.Skip(currentChunk.Count - overlapWords).ToList();
                    currentLength = currentChunk.Sum(w => w.Length + 1);
                }
                else
                {
                    currentChunk.Clear();
                    currentLength = 0;
                }
            }

            currentChunk.Add(word);
            currentLength += wordLength;
        }

        // Add final chunk if there are remaining words
        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join(" ", currentChunk));
        }

        return chunks;
    }

    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Simple estimation: roughly 0.75 tokens per word
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return (int)(words.Length * 0.75);
    }
}