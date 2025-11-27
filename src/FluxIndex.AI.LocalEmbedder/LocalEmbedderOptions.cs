namespace FluxIndex.AI.LocalEmbedder;

/// <summary>
/// Execution provider for ONNX runtime
/// </summary>
public enum LocalEmbedderExecutionProvider
{
    /// <summary>
    /// CPU execution (default, always available)
    /// </summary>
    CPU,

    /// <summary>
    /// CUDA GPU execution (requires CUDA-enabled GPU)
    /// </summary>
    CUDA,

    /// <summary>
    /// DirectML execution (Windows GPU acceleration)
    /// </summary>
    DirectML
}

/// <summary>
/// Pooling mode for generating embeddings from token outputs
/// </summary>
public enum LocalEmbedderPoolingMode
{
    /// <summary>
    /// Use CLS token output (first token)
    /// </summary>
    Cls,

    /// <summary>
    /// Average all token outputs
    /// </summary>
    Mean,

    /// <summary>
    /// Use the last token output
    /// </summary>
    LastToken
}

/// <summary>
/// Configuration options for LocalEmbedder service
/// </summary>
public class LocalEmbedderOptions
{
    /// <summary>
    /// Model identifier from HuggingFace or local path
    /// Available models:
    /// - "all-MiniLM-L6-v2" (384 dimensions, default)
    /// - "all-mpnet-base-v2" (768 dimensions)
    /// - "bge-small-en-v1.5" (384 dimensions)
    /// - "bge-base-en-v1.5" (768 dimensions)
    /// - "multilingual-e5-small" (384 dimensions)
    /// - "multilingual-e5-base" (768 dimensions)
    /// </summary>
    public string ModelId { get; set; } = "all-MiniLM-L6-v2";

    /// <summary>
    /// Execution provider for ONNX runtime
    /// Default: CPU (always available)
    /// </summary>
    public LocalEmbedderExecutionProvider ExecutionProvider { get; set; } = LocalEmbedderExecutionProvider.CPU;

    /// <summary>
    /// Pooling mode for generating embeddings
    /// Default: Mean (best for most models)
    /// </summary>
    public LocalEmbedderPoolingMode PoolingMode { get; set; } = LocalEmbedderPoolingMode.Mean;

    /// <summary>
    /// Maximum sequence length for tokenization
    /// Default: 512
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>
    /// Whether to normalize output embeddings
    /// Default: true
    /// </summary>
    public bool NormalizeEmbeddings { get; set; } = true;

    /// <summary>
    /// Maximum tokens per text (estimated)
    /// Default: 8192
    /// </summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// Embedding dimensions (auto-detected from model if null)
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// Gets the effective dimensions based on model selection
    /// </summary>
    public int GetEffectiveDimensions()
    {
        if (Dimensions.HasValue)
            return Dimensions.Value;

        return ModelId.ToLowerInvariant() switch
        {
            "all-minilm-l6-v2" => 384,
            "bge-small-en-v1.5" => 384,
            "multilingual-e5-small" => 384,
            "all-mpnet-base-v2" => 768,
            "bge-base-en-v1.5" => 768,
            "multilingual-e5-base" => 768,
            _ => 384 // Default fallback
        };
    }

    /// <summary>
    /// Validates the configuration options
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModelId))
            throw new InvalidOperationException("ModelId is required");

        if (MaxSequenceLength <= 0)
            throw new InvalidOperationException("MaxSequenceLength must be positive");
    }
}
