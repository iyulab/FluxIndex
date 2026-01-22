using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FluxIndex.Core.Application.Utilities;

/// <summary>
/// High-performance vector math utilities for similarity computation.
/// Optimized for hot paths in vector search operations.
/// </summary>
public static class VectorMathUtilities
{
    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// Returns 0 for invalid inputs (null, empty, or mismatched dimensions).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dotProduct = 0f;
        float magnitudeA = 0f;
        float magnitudeB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var magnitude = MathF.Sqrt(magnitudeA) * MathF.Sqrt(magnitudeB);
        return magnitude == 0 ? 0 : dotProduct / magnitude;
    }

    /// <summary>
    /// Computes cosine similarity between two float arrays.
    /// Convenience overload for array inputs.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CosineSimilarity(float[]? a, float[]? b)
    {
        if (a == null || b == null)
            return 0f;

        return CosineSimilarity(a.AsSpan(), b.AsSpan());
    }

    /// <summary>
    /// Optimized cosine similarity when query magnitude is pre-computed.
    /// Useful for batch searches where the same query is compared against many candidates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FastCosineSimilarity(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate, float queryMagnitude)
    {
        if (query.Length != candidate.Length || query.Length == 0 || queryMagnitude == 0)
            return 0f;

        float dotProduct = 0f;
        float candidateMagnitude = 0f;

        for (int i = 0; i < query.Length; i++)
        {
            dotProduct += query[i] * candidate[i];
            candidateMagnitude += candidate[i] * candidate[i];
        }

        candidateMagnitude = MathF.Sqrt(candidateMagnitude);
        return candidateMagnitude == 0 ? 0 : dotProduct / (queryMagnitude * candidateMagnitude);
    }

    /// <summary>
    /// Computes the L2 (Euclidean) magnitude of a vector.
    /// Pre-compute this for repeated similarity comparisons.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeMagnitude(ReadOnlySpan<float> vector)
    {
        if (vector.Length == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }
        return MathF.Sqrt(sum);
    }

    /// <summary>
    /// Computes the L2 magnitude of a float array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeMagnitude(float[]? vector)
    {
        if (vector == null || vector.Length == 0)
            return 0f;

        return ComputeMagnitude(vector.AsSpan());
    }

    /// <summary>
    /// Computes Euclidean (L2) distance between two vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return float.MaxValue;

        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return MathF.Sqrt(sum);
    }

    /// <summary>
    /// Computes dot product between two vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    /// <summary>
    /// Converts cosine distance (0-2) to cosine similarity (1 to -1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistanceToSimilarity(float distance, DistanceType type = DistanceType.Cosine)
    {
        return type switch
        {
            DistanceType.Cosine => 1.0f - distance,
            DistanceType.Euclidean => 1.0f / (1.0f + distance),
            DistanceType.DotProduct => distance, // No conversion needed
            _ => 1.0f - distance
        };
    }

    /// <summary>
    /// Converts cosine similarity to cosine distance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SimilarityToDistance(float similarity, DistanceType type = DistanceType.Cosine)
    {
        return type switch
        {
            DistanceType.Cosine => 1.0f - similarity,
            DistanceType.Euclidean => (1.0f - similarity) / similarity,
            DistanceType.DotProduct => similarity,
            _ => 1.0f - similarity
        };
    }

    /// <summary>
    /// Normalizes a vector to unit length (L2 normalization).
    /// Returns a new array with normalized values.
    /// </summary>
    public static float[] Normalize(ReadOnlySpan<float> vector)
    {
        if (vector.Length == 0)
            return [];

        var magnitude = ComputeMagnitude(vector);
        if (magnitude == 0)
            return vector.ToArray();

        var result = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] / magnitude;
        }
        return result;
    }

    /// <summary>
    /// Normalizes a vector in place.
    /// </summary>
    public static void NormalizeInPlace(Span<float> vector)
    {
        if (vector.Length == 0)
            return;

        var magnitude = ComputeMagnitude(vector);
        if (magnitude == 0)
            return;

        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }
    }
}

/// <summary>
/// Distance metric types for vector similarity calculations.
/// </summary>
public enum DistanceType
{
    /// <summary>
    /// Cosine distance (1 - cosine similarity). Range: [0, 2]
    /// </summary>
    Cosine,

    /// <summary>
    /// Euclidean (L2) distance. Range: [0, infinity)
    /// </summary>
    Euclidean,

    /// <summary>
    /// Negative dot product distance (for inner product similarity).
    /// </summary>
    DotProduct
}
