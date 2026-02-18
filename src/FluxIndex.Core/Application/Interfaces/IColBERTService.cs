using FluxIndex.Core.Domain.ValueObjects;

namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// ColBERT-style late interaction scoring service.
/// Implements token-level matching between query and document embeddings
/// using MaxSim (maximum similarity) operations for fine-grained relevance.
/// </summary>
public interface IColBERTService
{
    /// <summary>
    /// Compute late interaction score between query and document token embeddings.
    /// Uses MaxSim: for each query token, find max similarity with any document token,
    /// then sum across all query tokens.
    /// </summary>
    /// <param name="queryEmbeddings">Token-level embeddings for query (shape: [num_query_tokens, dim])</param>
    /// <param name="documentEmbeddings">Token-level embeddings for document (shape: [num_doc_tokens, dim])</param>
    /// <returns>Late interaction score</returns>
    float ComputeMaxSimScore(
        ReadOnlySpan<float[]> queryEmbeddings,
        ReadOnlySpan<float[]> documentEmbeddings);

    /// <summary>
    /// Compute late interaction scores for multiple documents in batch.
    /// </summary>
    Task<IReadOnlyList<ColBERTScore>> ComputeBatchScoresAsync(
        float[][] queryEmbeddings,
        IEnumerable<ColBERTDocument> documents,
        ColBERTOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rank candidates using late interaction scoring.
    /// </summary>
    Task<IReadOnlyList<ColBERTRankedResult>> RankAsync(
        float[][] queryEmbeddings,
        IEnumerable<ColBERTCandidate> candidates,
        ColBERTOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate token-level embeddings for text using the underlying embedding model.
    /// </summary>
    Task<float[][]> GenerateTokenEmbeddingsAsync(
        string text,
        bool isQuery,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compress document embeddings using quantization for storage efficiency.
    /// </summary>
    Task<ColBERTCompressedEmbeddings> CompressEmbeddingsAsync(
        float[][] embeddings,
        ColBERTCompressionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decompress embeddings for scoring.
    /// </summary>
    Task<float[][]> DecompressEmbeddingsAsync(
        ColBERTCompressedEmbeddings compressed,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Document with token-level embeddings for ColBERT scoring.
/// </summary>
public record ColBERTDocument
{
    public required string Id { get; init; }
    public required float[][] TokenEmbeddings { get; init; }
    public string? Content { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Candidate for ColBERT ranking (may have pre-computed embeddings or content to embed).
/// </summary>
public record ColBERTCandidate
{
    public required string Id { get; init; }
    public float[][]? TokenEmbeddings { get; init; }
    public string? Content { get; init; }
    public double? InitialScore { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// ColBERT score result.
/// </summary>
public record ColBERTScore
{
    public required string DocumentId { get; init; }
    public required float Score { get; init; }
    public float? NormalizedScore { get; init; }
    public int QueryTokenCount { get; init; }
    public int DocumentTokenCount { get; init; }
}

/// <summary>
/// Ranked result from ColBERT scoring.
/// </summary>
public record ColBERTRankedResult
{
    public required string Id { get; init; }
    public required float ColBERTScore { get; init; }
    public float? NormalizedScore { get; init; }
    public double? InitialScore { get; init; }
    public double? CombinedScore { get; init; }
    public int OriginalRank { get; init; }
    public int NewRank { get; init; }
    public string? Content { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Compressed embeddings for storage efficiency.
/// </summary>
public record ColBERTCompressedEmbeddings
{
    public required byte[] Data { get; init; }
    public required ColBERTCompressionType CompressionType { get; init; }
    public required int OriginalDimension { get; init; }
    public required int TokenCount { get; init; }
    public float? QuantizationScale { get; init; }
    public float? QuantizationOffset { get; init; }
}

/// <summary>
/// Compression type for ColBERT embeddings.
/// </summary>
public enum ColBERTCompressionType
{
    /// <summary>No compression, full float32</summary>
    None,

    /// <summary>Float16 (half precision)</summary>
    Float16,

    /// <summary>Scalar 8-bit quantization (4x compression)</summary>
    Scalar8Bit,

    /// <summary>Binary quantization (32x compression)</summary>
    Binary,

    /// <summary>Product quantization</summary>
    ProductQuantization
}

/// <summary>
/// Options for ColBERT scoring.
/// </summary>
public class ColBERTOptions
{
    /// <summary>
    /// Maximum number of query tokens to consider.
    /// </summary>
    public int MaxQueryTokens { get; set; } = 32;

    /// <summary>
    /// Maximum number of document tokens to consider.
    /// </summary>
    public int MaxDocumentTokens { get; set; } = 512;

    /// <summary>
    /// Weight for combining ColBERT score with initial score.
    /// Combined = (1 - Weight) * InitialScore + Weight * ColBERTScore
    /// </summary>
    public float ColBERTWeight { get; set; } = 0.5f;

    /// <summary>
    /// Normalize scores by query token count.
    /// </summary>
    public bool NormalizeByQueryLength { get; set; } = true;

    /// <summary>
    /// Use SIMD acceleration when available.
    /// </summary>
    public bool UseSimd { get; set; } = true;

    /// <summary>
    /// Number of parallel workers for batch processing.
    /// </summary>
    public int Parallelism { get; set; } = Environment.ProcessorCount;
}

/// <summary>
/// Options for embedding compression.
/// </summary>
public class ColBERTCompressionOptions
{
    public ColBERTCompressionType CompressionType { get; set; } = ColBERTCompressionType.Scalar8Bit;

    /// <summary>
    /// Number of subvectors for product quantization.
    /// </summary>
    public int ProductQuantizationSubvectors { get; set; } = 8;

    /// <summary>
    /// Codebook size for product quantization.
    /// </summary>
    public int ProductQuantizationCodebookSize { get; set; } = 256;
}
