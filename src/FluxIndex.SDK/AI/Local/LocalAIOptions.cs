using LocalAI;
using LocalAI.Embedder;

namespace FluxIndex.SDK.AI.Local;

/// <summary>
/// Execution provider for ONNX runtime inference
/// </summary>
public enum LocalAIExecutionProvider
{
    /// <summary>
    /// Automatic GPU detection (recommended).
    /// Tries GPU providers first, falls back to CPU if unavailable.
    /// </summary>
    Auto,

    /// <summary>
    /// CPU execution (always available)
    /// </summary>
    Cpu,

    /// <summary>
    /// NVIDIA CUDA GPU execution
    /// </summary>
    Cuda,

    /// <summary>
    /// DirectML execution (Windows GPU - AMD, Intel, NVIDIA)
    /// </summary>
    DirectML,

    /// <summary>
    /// CoreML execution (macOS/iOS)
    /// </summary>
    CoreML
}

/// <summary>
/// Pooling mode for generating embeddings from token outputs
/// </summary>
public enum LocalAIPoolingMode
{
    /// <summary>
    /// Average all token outputs (default, best for most models)
    /// </summary>
    Mean,

    /// <summary>
    /// Use CLS token output (first token, required for BGE models)
    /// </summary>
    Cls,

    /// <summary>
    /// Max pooling across all tokens
    /// </summary>
    Max
}

/// <summary>
/// Configuration options for LocalAI embedding service
/// </summary>
public sealed class LocalAIEmbeddingOptions
{
    /// <summary>
    /// Model identifier or alias.
    /// Available aliases: "default" (bge-small), "fast" (MiniLM), "quality" (bge-base),
    /// "large" (nomic-embed), "multilingual" (e5-base)
    /// </summary>
    public string ModelId { get; set; } = "default";

    /// <summary>
    /// Execution provider for ONNX runtime.
    /// Default: Auto (automatic GPU detection)
    /// </summary>
    public LocalAIExecutionProvider ExecutionProvider { get; set; } = LocalAIExecutionProvider.Auto;

    /// <summary>
    /// Pooling mode for generating embeddings.
    /// Default: Mean (best for most models)
    /// </summary>
    public LocalAIPoolingMode PoolingMode { get; set; } = LocalAIPoolingMode.Mean;

    /// <summary>
    /// Maximum sequence length for tokenization.
    /// Default: 512
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>
    /// Whether to normalize output embeddings to unit vectors.
    /// Default: true
    /// </summary>
    public bool NormalizeEmbeddings { get; set; } = true;

    /// <summary>
    /// Custom cache directory for model files.
    /// Default: null (uses HuggingFace standard cache: ~/.cache/huggingface/hub)
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Maximum tokens per text (for estimation).
    /// Default: 8192
    /// </summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// Pre-load model during service registration.
    /// Default: false
    /// </summary>
    public bool WarmupOnStartup { get; set; } = false;

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
            "default" or "bge-small-en-v1.5" => 384,
            "fast" or "all-minilm-l6-v2" => 384,
            "quality" or "bge-base-en-v1.5" => 768,
            "large" or "nomic-embed-text-v1.5" => 768,
            "multilingual" or "multilingual-e5-base" => 768,
            _ => 384
        };
    }

    internal ExecutionProvider ToExecutionProvider() => ExecutionProvider switch
    {
        LocalAIExecutionProvider.Auto => LocalAI.ExecutionProvider.Auto,
        LocalAIExecutionProvider.Cpu => LocalAI.ExecutionProvider.Cpu,
        LocalAIExecutionProvider.Cuda => LocalAI.ExecutionProvider.Cuda,
        LocalAIExecutionProvider.DirectML => LocalAI.ExecutionProvider.DirectML,
        LocalAIExecutionProvider.CoreML => LocalAI.ExecutionProvider.CoreML,
        _ => LocalAI.ExecutionProvider.Auto
    };

    internal PoolingMode ToPoolingMode() => PoolingMode switch
    {
        LocalAIPoolingMode.Mean => LocalAI.Embedder.PoolingMode.Mean,
        LocalAIPoolingMode.Cls => LocalAI.Embedder.PoolingMode.Cls,
        LocalAIPoolingMode.Max => LocalAI.Embedder.PoolingMode.Max,
        _ => LocalAI.Embedder.PoolingMode.Mean
    };
}

/// <summary>
/// Configuration options for LocalAI reranker service
/// </summary>
public sealed class LocalAIRerankerOptions
{
    /// <summary>
    /// Model identifier or alias.
    /// Available aliases: "default" (ms-marco-MiniLM), "fast" (TinyBERT),
    /// "quality" (bge-reranker-base), "large" (bge-reranker-large), "multilingual" (bge-reranker-v2-m3)
    /// </summary>
    public string ModelId { get; set; } = "default";

    /// <summary>
    /// Maximum input sequence length.
    /// Default: null (uses model's default, typically 512)
    /// </summary>
    public int? MaxSequenceLength { get; set; }

    /// <summary>
    /// Execution provider for ONNX runtime.
    /// Default: Auto (automatic GPU detection)
    /// </summary>
    public LocalAIExecutionProvider ExecutionProvider { get; set; } = LocalAIExecutionProvider.Auto;

    /// <summary>
    /// Batch size for processing multiple documents.
    /// Default: 32
    /// </summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Custom cache directory for model files.
    /// Default: null (uses HuggingFace standard cache)
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Pre-load model during service registration.
    /// Default: true
    /// </summary>
    public bool WarmupOnStartup { get; set; } = true;

    /// <summary>
    /// Number of inference threads.
    /// Default: null (uses Environment.ProcessorCount)
    /// </summary>
    public int? ThreadCount { get; set; }

    internal ExecutionProvider ToExecutionProvider() => ExecutionProvider switch
    {
        LocalAIExecutionProvider.Auto => LocalAI.ExecutionProvider.Auto,
        LocalAIExecutionProvider.Cpu => LocalAI.ExecutionProvider.Cpu,
        LocalAIExecutionProvider.Cuda => LocalAI.ExecutionProvider.Cuda,
        LocalAIExecutionProvider.DirectML => LocalAI.ExecutionProvider.DirectML,
        LocalAIExecutionProvider.CoreML => LocalAI.ExecutionProvider.CoreML,
        _ => LocalAI.ExecutionProvider.Auto
    };
}
