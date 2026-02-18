using System.Security.Cryptography;

namespace FluxIndex.SDK.Processing;

/// <summary>
/// Result of extraction-only stage.
/// Contains raw content that can be persisted and used to resume processing later.
/// </summary>
public class ExtractionResult
{
    /// <summary>
    /// Document ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Original file path
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of source file for change detection
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>
    /// Extracted raw text content
    /// </summary>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>
    /// Extracted images (filename -> binary data)
    /// </summary>
    public Dictionary<string, byte[]> Images { get; set; } = new();

    /// <summary>
    /// Document metadata from extraction
    /// </summary>
    public DocumentMetadataResult Metadata { get; set; } = new();

    /// <summary>
    /// Extraction timestamp
    /// </summary>
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether extraction was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if extraction failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Markdown-converted text (if ConvertToMarkdown was enabled)
    /// </summary>
    public string? MarkdownText { get; set; }

    /// <summary>
    /// Markdown conversion statistics (if ConvertToMarkdown was enabled)
    /// </summary>
    public MarkdownConversionStatistics? MarkdownStatistics { get; set; }

    /// <summary>
    /// Compute SHA-256 hash of a file for change detection.
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Check if source file has changed since this extraction.
    /// </summary>
    public bool HasSourceChanged(string filePath)
    {
        if (!File.Exists(filePath)) return true;
        var currentHash = ComputeFileHash(filePath);
        return !string.Equals(SourceHash, currentHash, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Options for processing from extracted content (skip extraction stage).
/// </summary>
public class ContentProcessingOptions
{
    /// <summary>
    /// Document ID to use for the result
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Language hint for processing (null = auto-detect)
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Chunking strategy (Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize, Hierarchical)
    /// </summary>
    public string ChunkingStrategy { get; set; } = "Auto";

    /// <summary>
    /// Maximum chunk size in tokens
    /// </summary>
    public int MaxChunkSize { get; set; } = 1024;

    /// <summary>
    /// Overlap size between chunks in tokens
    /// </summary>
    public int OverlapSize { get; set; } = 128;

    /// <summary>
    /// Generate embeddings for chunks
    /// </summary>
    public bool GenerateEmbeddings { get; set; } = true;

    /// <summary>
    /// Enable contextual enrichment for chunks
    /// </summary>
    public bool EnableContextualEnrichment { get; set; }

    /// <summary>
    /// Enable QA pair generation from chunks
    /// </summary>
    public bool EnableQAGeneration { get; set; }

    /// <summary>
    /// Maximum QA pairs to generate per chunk
    /// </summary>
    public int MaxQAPairsPerChunk { get; set; } = 3;

    /// <summary>
    /// Progress callback for reporting processing status
    /// </summary>
    public Action<ProcessingProgress>? OnProgress { get; set; }
}

/// <summary>
/// Result of document processing pipeline
/// </summary>
public class DocumentProcessingResult
{
    /// <summary>
    /// Document ID
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Original file path
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Extracted raw text content
    /// </summary>
    public string ExtractedText { get; set; } = string.Empty;

    /// <summary>
    /// Cleaned/preprocessed text (if EnableTextCleaning is true)
    /// </summary>
    public string? CleanedText { get; set; }

    /// <summary>
    /// Extracted images (path -> binary data)
    /// </summary>
    public Dictionary<string, byte[]> Images { get; set; } = new();

    /// <summary>
    /// Document metadata
    /// </summary>
    public DocumentMetadataResult Metadata { get; set; } = new();

    /// <summary>
    /// Processed chunks with embeddings and context
    /// </summary>
    public List<ChunkResult> Chunks { get; set; } = new();

    /// <summary>
    /// Generated QA pairs (if EnableQAGeneration is true)
    /// </summary>
    public List<QAPairResult> QAPairs { get; set; } = new();

    /// <summary>
    /// Processing statistics
    /// </summary>
    public ProcessingStats Stats { get; set; } = new();

    /// <summary>
    /// Whether processing was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Document metadata result
/// </summary>
public class DocumentMetadataResult
{
    /// <summary>
    /// Document title (extracted or inferred)
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Document author
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Creation date
    /// </summary>
    public DateTime? CreatedDate { get; set; }

    /// <summary>
    /// Last modified date
    /// </summary>
    public DateTime? ModifiedDate { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// File extension
    /// </summary>
    public string? FileExtension { get; set; }

    /// <summary>
    /// Detected language
    /// </summary>
    public string? DetectedLanguage { get; set; }

    /// <summary>
    /// Content type / MIME type
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Total character count
    /// </summary>
    public int CharacterCount { get; set; }

    /// <summary>
    /// Total word count
    /// </summary>
    public int WordCount { get; set; }

    /// <summary>
    /// Number of pages (if applicable)
    /// </summary>
    public int? PageCount { get; set; }

    /// <summary>
    /// Number of images extracted
    /// </summary>
    public int ImageCount { get; set; }

    /// <summary>
    /// Additional custom metadata
    /// </summary>
    public Dictionary<string, object> CustomMetadata { get; set; } = new();
}

/// <summary>
/// Individual chunk result with optional contextual enrichment
/// </summary>
public class ChunkResult
{
    /// <summary>
    /// Chunk ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Chunk index (0-based)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Chunk text content (original)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Approximate token count
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Character count
    /// </summary>
    public int CharacterCount { get; set; }

    /// <summary>
    /// Start position in original text
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// End position in original text
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// Embedding vector (if generated).
    /// Note: Embedding is generated from GetContextualizedText() if ContextSummary exists.
    /// </summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// Context summary from contextual enrichment (Anthropic Contextual Retrieval).
    /// This is prepended to Content when generating embeddings for better retrieval.
    /// </summary>
    public string? ContextSummary { get; set; }

    /// <summary>
    /// Gets the contextualized text for embedding.
    /// Returns ContextSummary + Content if context exists, otherwise just Content.
    /// </summary>
    public string GetContextualizedText()
    {
        if (string.IsNullOrEmpty(ContextSummary))
            return Content;
        return $"{ContextSummary}\n\n{Content}";
    }

    /// <summary>
    /// Chunk-level metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Processing statistics
/// </summary>
public class ProcessingStats
{
    /// <summary>
    /// Processing start time
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Processing end time
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Total processing duration
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Time spent on text extraction
    /// </summary>
    public TimeSpan ExtractionTime { get; set; }

    /// <summary>
    /// Time spent on text cleaning/preprocessing
    /// </summary>
    public TimeSpan? CleaningTime { get; set; }

    /// <summary>
    /// Time spent on chunking
    /// </summary>
    public TimeSpan ChunkingTime { get; set; }

    /// <summary>
    /// Time spent on contextual enrichment
    /// </summary>
    public TimeSpan? ContextualEnrichmentTime { get; set; }

    /// <summary>
    /// Time spent on embedding generation
    /// </summary>
    public TimeSpan EmbeddingTime { get; set; }

    /// <summary>
    /// Time spent on QA generation
    /// </summary>
    public TimeSpan? QAGenerationTime { get; set; }

    /// <summary>
    /// Total chunks generated
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// Total chunks with context enrichment
    /// </summary>
    public int EnrichedChunks { get; set; }

    /// <summary>
    /// Total QA pairs generated
    /// </summary>
    public int TotalQAPairs { get; set; }

    /// <summary>
    /// Total images extracted
    /// </summary>
    public int TotalImages { get; set; }
}

/// <summary>
/// QA pair result from QA generation
/// </summary>
public class QAPairResult
{
    /// <summary>
    /// Source chunk ID
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// Generated question
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Generated answer
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Context used for generation (typically the chunk content)
    /// </summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// Quality score (0-1) if evaluation was performed
    /// </summary>
    public double? QualityScore { get; set; }
}

/// <summary>
/// Options for extraction-only stage
/// </summary>
public class ExtractionOptions
{
    /// <summary>
    /// Extract images from document
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Convert extracted text to structured Markdown (FileFlux v0.8.6+)
    /// </summary>
    public bool ConvertToMarkdown { get; set; }

    /// <summary>
    /// Markdown conversion options (used when ConvertToMarkdown is true)
    /// </summary>
    public MarkdownOptions? MarkdownOptions { get; set; }
}

/// <summary>
/// Markdown conversion options (wrapper for FileFlux.MarkdownConversionOptions)
/// </summary>
public class MarkdownOptions
{
    /// <summary>
    /// Preserve detected heading hierarchy
    /// </summary>
    public bool PreserveHeadings { get; set; } = true;

    /// <summary>
    /// Convert detected tables to Markdown tables
    /// </summary>
    public bool ConvertTables { get; set; } = true;

    /// <summary>
    /// Preserve bullet/numbered lists
    /// </summary>
    public bool PreserveLists { get; set; } = true;

    /// <summary>
    /// Include image placeholders (![alt](embedded:img_000))
    /// </summary>
    public bool IncludeImagePlaceholders { get; set; } = true;

    /// <summary>
    /// Use LLM for structure inference when heuristics fail
    /// (requires ITextCompletionService)
    /// </summary>
    public bool UseLLMInference { get; set; }

    /// <summary>
    /// Detect and preserve code blocks
    /// </summary>
    public bool DetectCodeBlocks { get; set; } = true;

    /// <summary>
    /// Normalize whitespace for readability
    /// </summary>
    public bool NormalizeWhitespace { get; set; } = true;
}

/// <summary>
/// Markdown conversion statistics from FileFlux
/// </summary>
public class MarkdownConversionStatistics
{
    /// <summary>
    /// Number of headings detected
    /// </summary>
    public int HeadingCount { get; set; }

    /// <summary>
    /// Number of tables detected
    /// </summary>
    public int TableCount { get; set; }

    /// <summary>
    /// Number of lists detected
    /// </summary>
    public int ListCount { get; set; }

    /// <summary>
    /// Number of code blocks detected
    /// </summary>
    public int CodeBlockCount { get; set; }

    /// <summary>
    /// Number of image placeholders converted
    /// </summary>
    public int ImagePlaceholderCount { get; set; }

    /// <summary>
    /// Conversion method used (Heuristic, LLM, Mixed)
    /// </summary>
    public string Method { get; set; } = "Heuristic";

    /// <summary>
    /// Original text length
    /// </summary>
    public int OriginalLength { get; set; }

    /// <summary>
    /// Converted markdown length
    /// </summary>
    public int MarkdownLength { get; set; }

    /// <summary>
    /// Warnings from conversion process
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}
