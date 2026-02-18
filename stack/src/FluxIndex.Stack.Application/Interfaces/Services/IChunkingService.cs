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
    /// Chunks content and extracts embedded images.
    /// </summary>
    /// <param name="content">The document content to chunk.</param>
    /// <param name="documentId">The document ID to associate with chunks.</param>
    /// <param name="options">Optional chunking configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chunking result with chunks and extracted images.</returns>
    Task<ChunkingResult> ChunkContentWithImagesAsync(
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
/// Result of a chunking operation including extracted images.
/// </summary>
public class ChunkingResult
{
    /// <summary>
    /// The document chunks.
    /// </summary>
    public List<DocumentChunk> Chunks { get; set; } = new();

    /// <summary>
    /// Extracted images from the document.
    /// Key is the image ID (e.g., "img_001"), value contains the image data and content type.
    /// </summary>
    public Dictionary<string, ExtractedImage> Images { get; set; } = new();
}

/// <summary>
/// Represents an extracted image from a document.
/// </summary>
public class ExtractedImage
{
    /// <summary>
    /// The image binary data.
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The MIME content type (e.g., "image/png", "image/jpeg").
    /// </summary>
    public string ContentType { get; set; } = "image/png";

    /// <summary>
    /// Optional description or alt text for the image.
    /// </summary>
    public string? Description { get; set; }
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
    public bool EnableMetadataEnrichment { get; set; }

    /// <summary>
    /// Enable Late Chunking for contextual embeddings.
    /// When enabled, generates embeddings with document-level context preserved.
    /// </summary>
    public bool EnableLateChunking { get; set; }

    /// <summary>
    /// Context window size for Late Chunking (number of surrounding chunks to include).
    /// Default: 2 (includes 2 chunks before and after the current chunk).
    /// </summary>
    public int LateChunkingContextWindow { get; set; } = 2;
}
