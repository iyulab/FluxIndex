namespace FluxIndex.SDK.Processing;

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
