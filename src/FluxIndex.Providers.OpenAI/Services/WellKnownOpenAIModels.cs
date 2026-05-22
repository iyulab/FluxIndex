namespace FluxIndex.Providers.OpenAI.Services;

/// <summary>
/// Embedding dimension lookup for well-known OpenAI and compatible models.
/// </summary>
public static class WellKnownOpenAIModels
{
    // OpenAI models return the default (maximum) dimension.
    // Pass an explicit dimension to OpenAICompatibleEmbeddingService when using reduced dimensions.
    private static readonly Dictionary<string, int> _embeddingDimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI
        { "text-embedding-3-small", 1536 },
        { "text-embedding-3-large", 3072 },
        { "text-embedding-ada-002", 1536 },
        // Qwen3 embedding family
        { "qwen3-embedding-0.6b", 1024 },
        { "qwen3-embedding-4b", 2560 },
        { "qwen3-embedding-8b", 4096 },
        // Alibaba/Qwen
        { "text-embedding-v1", 1536 },
        { "text-embedding-v2", 1536 },
        { "text-embedding-v3", 1024 },
        // Nomic
        { "nomic-embed-text", 768 },
        { "nomic-embed-text-v1", 768 },
        { "nomic-embed-text-v1.5", 768 },
        // BGE (BAAI)
        { "bge-small-en-v1.5", 384 },
        { "bge-base-en-v1.5", 768 },
        { "bge-large-en-v1.5", 1024 },
        { "bge-m3", 1024 },
        // E5
        { "multilingual-e5-small", 384 },
        { "multilingual-e5-base", 768 },
        { "multilingual-e5-large", 1024 },
        // Ollama / common aliases
        { "mxbai-embed-large", 1024 },
        { "all-minilm", 384 },
        { "all-minilm-l6-v2", 384 },
        { "all-minilm-l12-v2", 384 },
        { "all-mpnet-base-v2", 768 },
    };

    /// <summary>
    /// Attempts to retrieve the default embedding dimension for a well-known model name.
    /// </summary>
    /// <param name="modelName">Model name (case-insensitive).</param>
    /// <param name="dimension">Embedding dimension if found.</param>
    /// <returns><see langword="true"/> if the model is in the well-known list.</returns>
    public static bool TryGetEmbeddingDimension(string modelName, out int dimension)
        => _embeddingDimensions.TryGetValue(modelName, out dimension);
}
