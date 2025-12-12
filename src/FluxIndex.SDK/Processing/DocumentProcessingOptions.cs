namespace FluxIndex.SDK.Processing;

/// <summary>
/// Options for document processing pipeline.
/// Pipeline order: Extract → Clean → Chunk → ContextualEnrich → Embed → QAGenerate → Save
/// </summary>
public class DocumentProcessingOptions
{
    /// <summary>
    /// Output directory for processed files (null = source file directory + _output)
    /// </summary>
    public string? OutputDirectory { get; set; }

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
    /// Enable metadata enrichment via LLM
    /// </summary>
    public bool EnableMetadataEnrichment { get; set; } = false;

    /// <summary>
    /// Enable text cleaning/preprocessing (noise removal, OCR fixes).
    /// Applied to full text BEFORE chunking.
    /// </summary>
    public bool EnableTextCleaning { get; set; } = false;

    /// <summary>
    /// Enable contextual enrichment for chunks (Anthropic Contextual Retrieval).
    /// Adds document-level context to each chunk BEFORE embedding for 49-67% better retrieval.
    /// Requires LLM service.
    /// </summary>
    public bool EnableContextualEnrichment { get; set; } = false;

    /// <summary>
    /// Enable QA pair generation from chunks.
    /// Generates question-answer pairs for RAG evaluation datasets.
    /// Runs AFTER embedding as final optional stage.
    /// Requires LLM service.
    /// </summary>
    public bool EnableQAGeneration { get; set; } = false;

    /// <summary>
    /// Maximum QA pairs to generate per chunk (default: 3).
    /// Only used when EnableQAGeneration is true.
    /// </summary>
    public int MaxQAPairsPerChunk { get; set; } = 3;

    /// <summary>
    /// Extract images from documents
    /// </summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>
    /// Save extracted text to file
    /// </summary>
    public bool SaveExtractedText { get; set; } = true;

    /// <summary>
    /// Save cleaned text to file (only if EnableTextCleaning is true)
    /// </summary>
    public bool SaveCleanedText { get; set; } = true;

    /// <summary>
    /// Save metadata to JSON file
    /// </summary>
    public bool SaveMetadata { get; set; } = true;

    /// <summary>
    /// Save individual chunk files
    /// </summary>
    public bool SaveChunks { get; set; } = true;

    /// <summary>
    /// Save generated QA pairs to JSON file (only if EnableQAGeneration is true)
    /// </summary>
    public bool SaveQAPairs { get; set; } = true;

    /// <summary>
    /// Progress callback for reporting processing status
    /// </summary>
    public Action<ProcessingProgress>? OnProgress { get; set; }
}

/// <summary>
/// Processing progress information
/// </summary>
public class ProcessingProgress
{
    /// <summary>
    /// Current processing stage
    /// </summary>
    public ProcessingStage Stage { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int Percentage { get; set; }

    /// <summary>
    /// Current item being processed
    /// </summary>
    public string? CurrentItem { get; set; }

    /// <summary>
    /// Status message
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Processing stages in execution order.
/// Order: Extract → Images → Clean → Chunk → ContextualEnrich → Embed → Metadata → QAGenerate → Save
/// </summary>
public enum ProcessingStage
{
    /// <summary>
    /// Initializing pipeline
    /// </summary>
    Initializing = 0,

    /// <summary>
    /// Extracting text from document
    /// </summary>
    Extracting = 1,

    /// <summary>
    /// Extracting images
    /// </summary>
    ExtractingImages = 2,

    /// <summary>
    /// Cleaning/preprocessing text (noise removal, OCR fixes)
    /// </summary>
    Cleaning = 3,

    /// <summary>
    /// Chunking document
    /// </summary>
    Chunking = 4,

    /// <summary>
    /// Contextual enrichment - adding document context to each chunk (Anthropic Contextual Retrieval)
    /// </summary>
    ContextualEnrichment = 5,

    /// <summary>
    /// Generating embeddings (uses contextualized text if enrichment was performed)
    /// </summary>
    GeneratingEmbeddings = 6,

    /// <summary>
    /// Enriching metadata
    /// </summary>
    EnrichingMetadata = 7,

    /// <summary>
    /// Generating QA pairs for evaluation
    /// </summary>
    GeneratingQA = 8,

    /// <summary>
    /// Saving output files
    /// </summary>
    SavingOutput = 9,

    /// <summary>
    /// Processing complete
    /// </summary>
    Complete = 100,

    /// <summary>
    /// Processing failed
    /// </summary>
    Failed = -1
}
