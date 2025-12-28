using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// 간단한 텍스트 청킹 서비스 (자연 경계 감지 지원)
/// </summary>
public class SimpleChunkingService : IChunkingService
{
    private readonly int _defaultChunkSize;
    private readonly int _defaultChunkOverlap;
    private readonly bool _useNaturalBreaks;

    /// <summary>
    /// Sentence boundary markers in order of preference
    /// </summary>
    private static readonly string[] SentenceEndMarkers = { ". ", "! ", "? ", ".\n", "!\n", "?\n" };

    /// <summary>
    /// Paragraph boundary markers
    /// </summary>
    private static readonly string[] ParagraphMarkers = { "\n\n", "\r\n\r\n" };

    public SimpleChunkingService(
        int defaultChunkSize = 512,
        int defaultChunkOverlap = 64,
        bool useNaturalBreaks = true)
    {
        _defaultChunkSize = defaultChunkSize;
        _defaultChunkOverlap = defaultChunkOverlap;
        _useNaturalBreaks = useNaturalBreaks;
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

            // Find natural break point if enabled and not at the end
            if (_useNaturalBreaks && end < text.Length)
            {
                var breakPoint = FindNaturalBreakPoint(text, start, end);
                if (breakPoint > start)
                {
                    end = breakPoint;
                }
            }

            yield return text.Substring(start, end - start);

            start = end - chunkOverlap;
            if (start < 0) start = 0;
            if (start >= text.Length)
                break;
        }
    }

    /// <summary>
    /// Finds a natural break point (sentence or paragraph boundary) within the given range.
    /// Prefers sentence boundaries, then paragraphs, then word boundaries.
    /// </summary>
    /// <param name="content">The text content</param>
    /// <param name="start">Start position of the range</param>
    /// <param name="end">End position of the range</param>
    /// <returns>Position after the break point, or start if no break found</returns>
    public static int FindNaturalBreakPoint(string content, int start, int end)
    {
        int lastBreak = start;

        // 1. Prefer sentence boundaries (highest priority)
        foreach (var marker in SentenceEndMarkers)
        {
            int pos = content.LastIndexOf(marker, end - 1, end - start, StringComparison.Ordinal);
            if (pos >= start && pos + marker.Length > lastBreak)
            {
                lastBreak = pos + marker.Length;
            }
        }

        // If we found a sentence boundary, return it
        if (lastBreak > start)
        {
            return lastBreak;
        }

        // 2. Try paragraph boundaries
        foreach (var marker in ParagraphMarkers)
        {
            int pos = content.LastIndexOf(marker, end - 1, end - start, StringComparison.Ordinal);
            if (pos >= start && pos + marker.Length > lastBreak)
            {
                lastBreak = pos + marker.Length;
            }
        }

        if (lastBreak > start)
        {
            return lastBreak;
        }

        // 3. Fall back to word boundaries (space or newline)
        for (int i = end - 1; i >= start; i--)
        {
            if (content[i] == ' ' || content[i] == '\n')
            {
                return i + 1;
            }
        }

        // No break point found
        return start;
    }

    private static int EstimateTokenCount(string text)
    {
        // Simple estimation: ~4 characters per token
        return text.Length / 4;
    }
}