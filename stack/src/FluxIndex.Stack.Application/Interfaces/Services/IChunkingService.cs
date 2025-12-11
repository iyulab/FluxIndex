using FluxIndex.Stack.Domain.Entities;

namespace FluxIndex.Stack.Application.Interfaces.Services;

/// <summary>
/// Service interface for intelligent document chunking.
/// </summary>
public interface IChunkingService
{
    /// <summary>
    /// Chunks content into semantically meaningful segments.
    /// </summary>
    /// <param name="content">The document content to chunk.</param>
    /// <param name="documentId">The document ID to associate with chunks.</param>
    /// <param name="options">Optional chunking configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of document chunks with metadata.</returns>
    Task<List<DocumentChunk>> ChunkContentAsync(
        string content,
        Guid documentId,
        ChunkingOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the language of the content.
    /// </summary>
    /// <param name="content">Content to analyze.</param>
    /// <returns>Language code (e.g., "en", "ko", "zh").</returns>
    string? DetectLanguage(string content);
}

/// <summary>
/// Configuration options for chunking.
/// </summary>
public class ChunkingOptions
{
    /// <summary>
    /// Chunking strategy (Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize).
    /// Default: Auto for intelligent language-aware chunking.
    /// </summary>
    public string Strategy { get; set; } = "Auto";

    /// <summary>
    /// Maximum chunk size in characters.
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks in characters.
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Language code for language-aware chunking.
    /// If null, language will be auto-detected.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Enable metadata enrichment with AI (requires ITextCompletionService).
    /// </summary>
    public bool EnableMetadataEnrichment { get; set; } = false;

    /// <summary>
    /// Enable Late Chunking for contextual embeddings.
    /// When enabled, generates embeddings with document-level context preserved.
    /// </summary>
    public bool EnableLateChunking { get; set; } = false;

    /// <summary>
    /// Context window size for Late Chunking (number of surrounding chunks to include).
    /// Default: 2 (includes 2 chunks before and after the current chunk).
    /// </summary>
    public int LateChunkingContextWindow { get; set; } = 2;
}
