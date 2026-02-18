using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluxIndex.Core.Application.Interfaces;

namespace FluxIndex.SDK.Services;

/// <summary>
/// In-memory embedding service for testing and benchmarking.
/// Generates deterministic random embeddings without requiring external API calls.
/// NOT for production use - embeddings are not semantically meaningful.
/// </summary>
public class InMemoryEmbeddingService : IEmbeddingService
{
    private const int DefaultDimensions = 384; // Common for sentence transformers
    private readonly Random _random;
    private readonly int _dimensions;

    public InMemoryEmbeddingService(int dimensions = DefaultDimensions, int? seed = null)
    {
        _dimensions = dimensions;
        _random = seed.HasValue ? new Random(seed.Value) : new Random(42); // Default seed for determinism
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or empty", nameof(text));
        }

        // Generate deterministic embedding based on text hash
        var hashCode = text.GetHashCode();
        var seededRandom = new Random(hashCode);

        var embedding = GenerateRandomEmbedding(_dimensions, seededRandom);
        return Task.FromResult(embedding);
    }

    public async Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var embeddings = new List<float[]>();
        foreach (var text in textList)
        {
            embeddings.Add(await GenerateEmbeddingAsync(text, cancellationToken));
        }

        return embeddings;
    }

    public int GetEmbeddingDimension() => _dimensions;

    public string GetModelName() => "InMemory-Test-Model";

    public int GetMaxTokens() => 8192; // Arbitrary high value for testing

    public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
    {
        // Simple word count approximation (1 token ≈ 0.75 words)
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var tokenCount = (int)(wordCount / 0.75);
        return Task.FromResult(tokenCount);
    }

    /// <summary>
    /// Generate random L2-normalized embedding vector
    /// </summary>
    private static float[] GenerateRandomEmbedding(int dimensions, Random random)
    {
        var embedding = new float[dimensions];

        // Generate random values using Box-Muller transform for normal distribution
        for (int i = 0; i < dimensions; i++)
        {
            var u1 = random.NextDouble();
            var u2 = random.NextDouble();
            var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            embedding[i] = (float)(randStdNormal * 0.1); // Standard deviation 0.1
        }

        // L2 normalization
        var norm = Math.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < dimensions; i++)
            {
                embedding[i] /= (float)norm;
            }
        }

        return embedding;
    }
}
