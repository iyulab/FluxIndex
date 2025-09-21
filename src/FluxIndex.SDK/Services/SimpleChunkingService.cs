using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using CoreChunkingStrategy = FluxIndex.Core.Application.Interfaces.ChunkingStrategy;
using System;
using System.Collections.Generic;
using System.IO;
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

    public Task<IEnumerable<DocumentChunk>> ChunkTextAsync(
        string text,
        string documentId,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chunkingOptions = options ?? new ChunkingOptions
        {
            ChunkSize = _defaultChunkSize,
            ChunkOverlap = _defaultOverlap
        };

        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
        }

        var chunkTexts = SplitIntoChunks(text, chunkingOptions.ChunkSize, chunkingOptions.ChunkOverlap);

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            var chunk = new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                Content = chunkTexts[i],
                ChunkIndex = i,
                TokenCount = EstimateTokenCount(chunkTexts[i]),
                Metadata = new Dictionary<string, object>
                {
                    ["chunk_method"] = "simple_text_splitting"
                },
                CreatedAt = DateTime.UtcNow
            };
            chunks.Add(chunk);
        }

        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    public async Task<IEnumerable<DocumentChunk>> ChunkDocumentAsync(
        string documentPath,
        string documentId,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(documentPath))
        {
            throw new FileNotFoundException($"Document not found: {documentPath}");
        }

        var content = await File.ReadAllTextAsync(documentPath, cancellationToken);
        return await ChunkTextAsync(content, documentId, options, cancellationToken);
    }

    public Task<IEnumerable<DocumentChunk>> ChunkWithStrategyAsync(
        string text,
        string documentId,
        CoreChunkingStrategy strategy,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chunkingOptions = options ?? new ChunkingOptions
        {
            ChunkSize = _defaultChunkSize,
            ChunkOverlap = _defaultOverlap
        };

        return strategy switch
        {
            CoreChunkingStrategy.Auto => ChunkTextAsync(text, documentId, chunkingOptions, cancellationToken),
            CoreChunkingStrategy.Fixed => ChunkWithFixedSize(text, documentId, chunkingOptions),
            CoreChunkingStrategy.Sentence => ChunkBySentence(text, documentId, chunkingOptions),
            CoreChunkingStrategy.Paragraph => ChunkByParagraph(text, documentId, chunkingOptions),
            CoreChunkingStrategy.Semantic => ChunkTextAsync(text, documentId, chunkingOptions, cancellationToken), // 기본 구현
            CoreChunkingStrategy.Sliding => ChunkWithSlidingWindow(text, documentId, chunkingOptions),
            _ => ChunkTextAsync(text, documentId, chunkingOptions, cancellationToken)
        };
    }

    private Task<IEnumerable<DocumentChunk>> ChunkWithFixedSize(
        string text,
        string documentId,
        ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();
        var chunkTexts = SplitIntoChunks(text, options.ChunkSize, 0); // No overlap for fixed size

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            var chunk = new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                Content = chunkTexts[i],
                ChunkIndex = i,
                TokenCount = EstimateTokenCount(chunkTexts[i]),
                Metadata = new Dictionary<string, object>
                {
                    ["chunk_method"] = "fixed_size"
                },
                CreatedAt = DateTime.UtcNow
            };
            chunks.Add(chunk);
        }

        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    private Task<IEnumerable<DocumentChunk>> ChunkBySentence(
        string text,
        string documentId,
        ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = "";
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence)) continue;

            if (currentChunk.Length + trimmedSentence.Length > options.ChunkSize && !string.IsNullOrEmpty(currentChunk))
            {
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    DocumentId = documentId,
                    Content = currentChunk.Trim(),
                    ChunkIndex = chunkIndex++,
                    TokenCount = EstimateTokenCount(currentChunk),
                    Metadata = new Dictionary<string, object>
                    {
                        ["chunk_method"] = "sentence_boundary"
                    },
                    CreatedAt = DateTime.UtcNow
                });
                currentChunk = "";
            }

            currentChunk += trimmedSentence + ". ";
        }

        if (!string.IsNullOrEmpty(currentChunk))
        {
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                Content = currentChunk.Trim(),
                ChunkIndex = chunkIndex,
                TokenCount = EstimateTokenCount(currentChunk),
                Metadata = new Dictionary<string, object>
                {
                    ["chunk_method"] = "sentence_boundary"
                },
                CreatedAt = DateTime.UtcNow
            });
        }

        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    private Task<IEnumerable<DocumentChunk>> ChunkByParagraph(
        string text,
        string documentId,
        ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var paragraph = paragraphs[i].Trim();
            if (string.IsNullOrEmpty(paragraph)) continue;

            if (paragraph.Length > options.ChunkSize)
            {
                // If paragraph is too long, split it further
                var subChunks = SplitIntoChunks(paragraph, options.ChunkSize, options.ChunkOverlap);
                foreach (var subChunk in subChunks)
                {
                    chunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid().ToString(),
                        DocumentId = documentId,
                        Content = subChunk,
                        ChunkIndex = chunks.Count,
                        TokenCount = EstimateTokenCount(subChunk),
                        Metadata = new Dictionary<string, object>
                        {
                            ["chunk_method"] = "paragraph_split"
                        },
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else
            {
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    DocumentId = documentId,
                    Content = paragraph,
                    ChunkIndex = chunks.Count,
                    TokenCount = EstimateTokenCount(paragraph),
                    Metadata = new Dictionary<string, object>
                    {
                        ["chunk_method"] = "paragraph_boundary"
                    },
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    private Task<IEnumerable<DocumentChunk>> ChunkWithSlidingWindow(
        string text,
        string documentId,
        ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordsPerChunk = options.ChunkSize / 5; // Estimate words per chunk
        var overlapWords = options.ChunkOverlap / 5; // Estimate overlap words

        for (int i = 0; i < words.Length; i += wordsPerChunk - overlapWords)
        {
            var chunkWords = words.Skip(i).Take(wordsPerChunk).ToArray();
            if (chunkWords.Length == 0) break;

            var chunkContent = string.Join(" ", chunkWords);
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                Content = chunkContent,
                ChunkIndex = chunks.Count,
                TokenCount = EstimateTokenCount(chunkContent),
                Metadata = new Dictionary<string, object>
                {
                    ["chunk_method"] = "sliding_window"
                },
                CreatedAt = DateTime.UtcNow
            });
        }

        return Task.FromResult<IEnumerable<DocumentChunk>>(chunks);
    }

    private List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();

        if (text.Length <= chunkSize)
        {
            chunks.Add(text);
            return chunks;
        }

        int start = 0;
        while (start < text.Length)
        {
            int end = Math.Min(start + chunkSize, text.Length);

            // 단어 경계에서 자르기 시도
            if (end < text.Length)
            {
                int lastSpaceIndex = text.LastIndexOf(' ', end - 1, Math.Min(end - start, 100));
                if (lastSpaceIndex > start)
                {
                    end = lastSpaceIndex;
                }
            }

            chunks.Add(text.Substring(start, end - start).Trim());

            if (end >= text.Length)
                break;

            start = Math.Max(start + 1, end - overlap);
        }

        return chunks;
    }

    private int EstimateTokenCount(string text)
    {
        // 간단한 토큰 추정: 평균 4자당 1토큰
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}