using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// ColBERT-style late interaction scoring service implementation.
/// Uses MaxSim (maximum similarity) for token-level matching between query and documents.
/// </summary>
/// <remarks>
/// ColBERT (Contextualized Late Interaction over BERT) computes relevance as:
/// Score = Σ max(q_i · d_j) for all query tokens q_i and document tokens d_j
///
/// This provides fine-grained token-level matching while being more efficient
/// than full cross-attention models.
/// </remarks>
public partial class ColBERTService : IColBERTService
{
    private static readonly char[] TokenizeSeparators = [' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\''];

    private readonly IEmbeddingService? _embeddingService;
    private readonly ILogger<ColBERTService> _logger;

    public ColBERTService(
        ILogger<ColBERTService> logger,
        IEmbeddingService? embeddingService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _embeddingService = embeddingService;
    }

    /// <inheritdoc />
    public float ComputeMaxSimScore(
        ReadOnlySpan<float[]> queryEmbeddings,
        ReadOnlySpan<float[]> documentEmbeddings)
    {
        if (queryEmbeddings.Length == 0 || documentEmbeddings.Length == 0)
        {
            return 0f;
        }

        float totalScore = 0f;

        // For each query token, find the maximum similarity with any document token
        for (int q = 0; q < queryEmbeddings.Length; q++)
        {
            var queryToken = queryEmbeddings[q];
            float maxSim = float.MinValue;

            for (int d = 0; d < documentEmbeddings.Length; d++)
            {
                var docToken = documentEmbeddings[d];
                float sim = ComputeCosineSimilarity(queryToken, docToken);
                if (sim > maxSim)
                {
                    maxSim = sim;
                }
            }

            // Clamp to non-negative (MaxSim should be >= 0 for normalized embeddings)
            totalScore += Math.Max(0, maxSim);
        }

        return totalScore;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColBERTScore>> ComputeBatchScoresAsync(
        float[][] queryEmbeddings,
        IEnumerable<ColBERTDocument> documents,
        ColBERTOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ColBERTOptions();
        var stopwatch = Stopwatch.StartNew();

        var documentList = documents.ToList();
        if (documentList.Count == 0 || queryEmbeddings.Length == 0)
        {
            return Array.Empty<ColBERTScore>();
        }

        // Truncate query embeddings if needed
        var queryTokens = TruncateEmbeddings(queryEmbeddings, options.MaxQueryTokens);

        var results = new ColBERTScore[documentList.Count];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Parallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            documentList.Select((doc, idx) => (doc, idx)),
            parallelOptions,
            (item, ct) =>
            {
                var (doc, idx) = item;

                // Truncate document embeddings if needed
                var docTokens = TruncateEmbeddings(doc.TokenEmbeddings, options.MaxDocumentTokens);

                float score = ComputeMaxSimScore(queryTokens, docTokens);

                float? normalizedScore = null;
                if (options.NormalizeByQueryLength && queryTokens.Length > 0)
                {
                    normalizedScore = score / queryTokens.Length;
                }

                results[idx] = new ColBERTScore
                {
                    DocumentId = doc.Id,
                    Score = score,
                    NormalizedScore = normalizedScore,
                    QueryTokenCount = queryTokens.Length,
                    DocumentTokenCount = docTokens.Length
                };

                return ValueTask.CompletedTask;
            });

        stopwatch.Stop();
        LogColBERT3(_logger, documentList.Count, stopwatch.ElapsedMilliseconds);

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ColBERTRankedResult>> RankAsync(
        float[][] queryEmbeddings,
        IEnumerable<ColBERTCandidate> candidates,
        ColBERTOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ColBERTOptions();
        var stopwatch = Stopwatch.StartNew();

        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            return Array.Empty<ColBERTRankedResult>();
        }

        // Generate embeddings for candidates without pre-computed embeddings
        if (_embeddingService != null)
        {
            for (int i = 0; i < candidateList.Count; i++)
            {
                var candidate = candidateList[i];
                if (candidate.TokenEmbeddings == null && !string.IsNullOrEmpty(candidate.Content))
                {
                    var embeddings = await GenerateTokenEmbeddingsAsync(
                        candidate.Content, isQuery: false, cancellationToken);

                    candidateList[i] = candidate with { TokenEmbeddings = embeddings };
                }
            }
        }

        // Filter out candidates without embeddings
        var validCandidates = candidateList
            .Where(c => c.TokenEmbeddings != null)
            .ToList();

        if (validCandidates.Count == 0)
        {
            LogColBERT2(_logger);
            return Array.Empty<ColBERTRankedResult>();
        }

        // Truncate query embeddings
        var queryTokens = TruncateEmbeddings(queryEmbeddings, options.MaxQueryTokens);

        // Compute scores
        var scoredCandidates = new List<(ColBERTCandidate Candidate, float Score, float? NormalizedScore, int OriginalRank)>();

        for (int i = 0; i < validCandidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = validCandidates[i];
            var docTokens = TruncateEmbeddings(candidate.TokenEmbeddings!, options.MaxDocumentTokens);

            float score = ComputeMaxSimScore(queryTokens, docTokens);
            float? normalizedScore = options.NormalizeByQueryLength && queryTokens.Length > 0
                ? score / queryTokens.Length
                : null;

            scoredCandidates.Add((candidate, score, normalizedScore, i));
        }

        // Sort by ColBERT score (or combined score if initial scores present)
        var sorted = scoredCandidates
            .Select(sc =>
            {
                double? combinedScore = null;
                if (sc.Candidate.InitialScore.HasValue)
                {
                    // Combine initial score with ColBERT score
                    var colbertNorm = sc.NormalizedScore ?? sc.Score;
                    combinedScore = (1 - options.ColBERTWeight) * sc.Candidate.InitialScore.Value +
                                    options.ColBERTWeight * colbertNorm;
                }

                return (sc.Candidate, sc.Score, sc.NormalizedScore, sc.OriginalRank, CombinedScore: combinedScore);
            })
            .OrderByDescending(x => x.CombinedScore ?? x.NormalizedScore ?? x.Score)
            .ToList();

        // Build results with new ranks
        var results = new List<ColBERTRankedResult>();
        for (int newRank = 0; newRank < sorted.Count; newRank++)
        {
            var item = sorted[newRank];
            results.Add(new ColBERTRankedResult
            {
                Id = item.Candidate.Id,
                ColBERTScore = item.Score,
                NormalizedScore = item.NormalizedScore,
                InitialScore = item.Candidate.InitialScore,
                CombinedScore = item.CombinedScore,
                OriginalRank = item.OriginalRank,
                NewRank = newRank,
                Content = item.Candidate.Content,
                Metadata = item.Candidate.Metadata
            });
        }

        stopwatch.Stop();
        LogColBERT1(_logger, results.Count, stopwatch.ElapsedMilliseconds);

        return results;
    }

    /// <inheritdoc />
    public async Task<float[][]> GenerateTokenEmbeddingsAsync(
        string text,
        bool isQuery,
        CancellationToken cancellationToken = default)
    {
        if (_embeddingService == null)
        {
            throw new InvalidOperationException(
                "Embedding service not available. Cannot generate token embeddings.");
        }

        // For now, we use sentence-level embedding as a single token
        // A proper ColBERT implementation would use a token-level embedding model
        // (like ColBERT v2 trained model) that outputs per-token embeddings

        // Simulate token-level by chunking text into words/phrases
        var tokens = TokenizeText(text, isQuery);

        if (tokens.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var embeddings = new List<float[]>();

        // Generate embedding for each token (word/phrase)
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                token, cancellationToken);

            embeddings.Add(embedding);
        }

        return embeddings.ToArray();
    }

    /// <inheritdoc />
    public Task<ColBERTCompressedEmbeddings> CompressEmbeddingsAsync(
        float[][] embeddings,
        ColBERTCompressionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ColBERTCompressionOptions();

        if (embeddings.Length == 0)
        {
            return Task.FromResult(new ColBERTCompressedEmbeddings
            {
                Data = Array.Empty<byte>(),
                CompressionType = options.CompressionType,
                OriginalDimension = 0,
                TokenCount = 0
            });
        }

        var dimension = embeddings[0].Length;
        byte[] compressedData;
        float? scale = null;
        float? offset = null;

        switch (options.CompressionType)
        {
            case ColBERTCompressionType.None:
                compressedData = CompressNone(embeddings);
                break;

            case ColBERTCompressionType.Float16:
                compressedData = CompressFloat16(embeddings);
                break;

            case ColBERTCompressionType.Scalar8Bit:
                (compressedData, scale, offset) = CompressInt8(embeddings);
                break;

            case ColBERTCompressionType.Binary:
                compressedData = CompressBinary(embeddings);
                break;

            default:
                compressedData = CompressNone(embeddings);
                break;
        }

        return Task.FromResult(new ColBERTCompressedEmbeddings
        {
            Data = compressedData,
            CompressionType = options.CompressionType,
            OriginalDimension = dimension,
            TokenCount = embeddings.Length,
            QuantizationScale = scale,
            QuantizationOffset = offset
        });
    }

    /// <inheritdoc />
    public Task<float[][]> DecompressEmbeddingsAsync(
        ColBERTCompressedEmbeddings compressed,
        CancellationToken cancellationToken = default)
    {
        if (compressed.TokenCount == 0 || compressed.Data.Length == 0)
        {
            return Task.FromResult(Array.Empty<float[]>());
        }

        float[][] embeddings;

        switch (compressed.CompressionType)
        {
            case ColBERTCompressionType.None:
                embeddings = DecompressNone(compressed);
                break;

            case ColBERTCompressionType.Float16:
                embeddings = DecompressFloat16(compressed);
                break;

            case ColBERTCompressionType.Scalar8Bit:
                embeddings = DecompressInt8(compressed);
                break;

            case ColBERTCompressionType.Binary:
                embeddings = DecompressBinary(compressed);
                break;

            default:
                embeddings = DecompressNone(compressed);
                break;
        }

        return Task.FromResult(embeddings);
    }

    #region Private Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        // Use SIMD if available
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            return ComputeCosineSimilaritySIMD(a, b);
        }

        float dot = 0, magA = 0, magB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denominator > 0 ? dot / denominator : 0f;
    }

    private static float ComputeCosineSimilaritySIMD(float[] a, float[] b)
    {
        var vectorSize = Vector<float>.Count;
        var dotProduct = Vector<float>.Zero;
        var magnitudeA = Vector<float>.Zero;
        var magnitudeB = Vector<float>.Zero;

        int i = 0;
        var length = a.Length;

        // Process vectors in chunks
        for (; i <= length - vectorSize; i += vectorSize)
        {
            var vecA = new Vector<float>(a, i);
            var vecB = new Vector<float>(b, i);

            dotProduct += vecA * vecB;
            magnitudeA += vecA * vecA;
            magnitudeB += vecB * vecB;
        }

        // Sum vector components
        float dot = 0, magA = 0, magB = 0;
        for (int j = 0; j < vectorSize; j++)
        {
            dot += dotProduct[j];
            magA += magnitudeA[j];
            magB += magnitudeB[j];
        }

        // Process remaining elements
        for (; i < length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denominator > 0 ? dot / denominator : 0f;
    }

    private static float[][] TruncateEmbeddings(float[][] embeddings, int maxTokens)
    {
        if (embeddings.Length <= maxTokens)
        {
            return embeddings;
        }

        var truncated = new float[maxTokens][];
        Array.Copy(embeddings, truncated, maxTokens);
        return truncated;
    }

    private static List<string> TokenizeText(string text, bool isQuery)
    {
        // Simple word-based tokenization
        // For production, use a proper tokenizer (e.g., WordPiece, BPE)
        var words = text.Split(
            TokenizeSeparators,
            StringSplitOptions.RemoveEmptyEntries);

        // For queries, we might use different tokenization strategy
        // (e.g., keep query as single token or use query expansion)
        if (isQuery)
        {
            // For now, use word-level for queries too
            return words.Where(w => w.Length >= 2).ToList();
        }

        // For documents, filter very short tokens
        return words.Where(w => w.Length >= 2).ToList();
    }

    #region Compression Methods

    private static byte[] CompressNone(float[][] embeddings)
    {
        int tokenCount = embeddings.Length;
        int dimension = embeddings[0].Length;

        var bytes = new byte[tokenCount * dimension * sizeof(float)];

        int byteIdx = 0;
        foreach (var embedding in embeddings)
        {
            foreach (var value in embedding)
            {
                var valueBytes = BitConverter.GetBytes(value);
                bytes[byteIdx++] = valueBytes[0];
                bytes[byteIdx++] = valueBytes[1];
                bytes[byteIdx++] = valueBytes[2];
                bytes[byteIdx++] = valueBytes[3];
            }
        }

        return bytes;
    }

    private static float[][] DecompressNone(ColBERTCompressedEmbeddings compressed)
    {
        var span = MemoryMarshal.Cast<byte, float>(compressed.Data);
        var embeddings = new float[compressed.TokenCount][];

        for (int i = 0; i < compressed.TokenCount; i++)
        {
            embeddings[i] = span.Slice(i * compressed.OriginalDimension, compressed.OriginalDimension).ToArray();
        }

        return embeddings;
    }

    private static byte[] CompressFloat16(float[][] embeddings)
    {
        int tokenCount = embeddings.Length;
        int dimension = embeddings[0].Length;

        var bytes = new byte[tokenCount * dimension * 2]; // 2 bytes per half

        int idx = 0;
        foreach (var embedding in embeddings)
        {
            foreach (var value in embedding)
            {
                var half = (Half)value;
                var halfBytes = BitConverter.GetBytes(half);
                bytes[idx++] = halfBytes[0];
                bytes[idx++] = halfBytes[1];
            }
        }

        return bytes;
    }

    private static float[][] DecompressFloat16(ColBERTCompressedEmbeddings compressed)
    {
        var embeddings = new float[compressed.TokenCount][];

        int idx = 0;
        for (int i = 0; i < compressed.TokenCount; i++)
        {
            embeddings[i] = new float[compressed.OriginalDimension];
            for (int j = 0; j < compressed.OriginalDimension; j++)
            {
                var halfBytes = new byte[2] { compressed.Data[idx], compressed.Data[idx + 1] };
                var half = BitConverter.ToHalf(halfBytes, 0);
                embeddings[i][j] = (float)half;
                idx += 2;
            }
        }

        return embeddings;
    }

    private static (byte[] data, float scale, float offset) CompressInt8(float[][] embeddings)
    {
        int tokenCount = embeddings.Length;
        int dimension = embeddings[0].Length;

        // Find min/max across all values for quantization
        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (var embedding in embeddings)
        {
            foreach (var value in embedding)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }
        }

        float scale = (max - min) / 255f;
        float offset = min;

        if (scale == 0) scale = 1f;

        var bytes = new byte[tokenCount * dimension];

        int idx = 0;
        foreach (var embedding in embeddings)
        {
            foreach (var value in embedding)
            {
                bytes[idx++] = (byte)Math.Clamp((value - offset) / scale, 0, 255);
            }
        }

        return (bytes, scale, offset);
    }

    private static float[][] DecompressInt8(ColBERTCompressedEmbeddings compressed)
    {
        var scale = compressed.QuantizationScale ?? 1f;
        var offset = compressed.QuantizationOffset ?? 0f;

        var embeddings = new float[compressed.TokenCount][];

        int idx = 0;
        for (int i = 0; i < compressed.TokenCount; i++)
        {
            embeddings[i] = new float[compressed.OriginalDimension];
            for (int j = 0; j < compressed.OriginalDimension; j++)
            {
                embeddings[i][j] = compressed.Data[idx++] * scale + offset;
            }
        }

        return embeddings;
    }

    private static byte[] CompressBinary(float[][] embeddings)
    {
        int tokenCount = embeddings.Length;
        int dimension = embeddings[0].Length;
        int bytesPerToken = (dimension + 7) / 8;

        var bytes = new byte[tokenCount * bytesPerToken];

        int byteIdx = 0;
        foreach (var embedding in embeddings)
        {
            for (int i = 0; i < dimension; i += 8)
            {
                byte b = 0;
                for (int bit = 0; bit < 8 && (i + bit) < dimension; bit++)
                {
                    if (embedding[i + bit] > 0)
                    {
                        b |= (byte)(1 << bit);
                    }
                }
                bytes[byteIdx++] = b;
            }
        }

        return bytes;
    }

    private static float[][] DecompressBinary(ColBERTCompressedEmbeddings compressed)
    {
        int bytesPerToken = (compressed.OriginalDimension + 7) / 8;
        var embeddings = new float[compressed.TokenCount][];

        int byteIdx = 0;
        for (int i = 0; i < compressed.TokenCount; i++)
        {
            embeddings[i] = new float[compressed.OriginalDimension];
            for (int j = 0; j < compressed.OriginalDimension; j += 8)
            {
                byte b = compressed.Data[byteIdx++];
                for (int bit = 0; bit < 8 && (j + bit) < compressed.OriginalDimension; bit++)
                {
                    embeddings[i][j + bit] = ((b >> bit) & 1) == 1 ? 1f : -1f;
                }
            }
        }

        return embeddings;
    }

    #endregion

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Debug, Message = "Computed ColBERT scores for {Count} documents in {Time}ms")]
    private static partial void LogColBERT3(ILogger logger, int count, long time);
    [LoggerMessage(Level = LogLevel.Warning, Message = "No valid candidates with embeddings for ColBERT ranking")]
    private static partial void LogColBERT2(ILogger logger);
    [LoggerMessage(Level = LogLevel.Debug, Message = "ColBERT ranked {Count} candidates in {Time}ms")]
    private static partial void LogColBERT1(ILogger logger, int count, long time);

    #endregion
}
