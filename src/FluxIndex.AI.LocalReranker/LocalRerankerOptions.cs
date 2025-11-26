namespace FluxIndex.AI.LocalReranker;

/// <summary>
/// Configuration options for LocalReranker cross-encoder based semantic reranking
/// </summary>
public sealed class LocalRerankerOptions
{
    /// <summary>
    /// Model identifier (alias or HuggingFace ID)
    /// Available aliases: "default" (90MB), "quality" (134MB), "fast" (17MB)
    /// </summary>
    public string ModelId { get; set; } = "default";

    /// <summary>
    /// Maximum sequence length for input. Null uses model default.
    /// </summary>
    public int? MaxSequenceLength { get; set; }

    /// <summary>
    /// Enable GPU acceleration. Falls back to CPU if unavailable.
    /// </summary>
    public bool UseGpu { get; set; } = false;

    /// <summary>
    /// Batch size for processing multiple documents
    /// </summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Custom model cache directory. Null uses default platform location.
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Pre-load model during service registration to avoid cold start latency
    /// </summary>
    public bool WarmupOnStartup { get; set; } = true;

    /// <summary>
    /// Number of inference threads. Null uses ProcessorCount.
    /// </summary>
    public int? ThreadCount { get; set; }
}
