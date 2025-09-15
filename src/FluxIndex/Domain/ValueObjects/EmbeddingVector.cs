using System;
using System.Linq;

namespace FluxIndex.Domain.Entities;

/// <summary>
/// 임베딩 벡터 값 객체
/// </summary>
public class EmbeddingVector : IEquatable<EmbeddingVector>
{
    public float[] Values { get; }
    public int Dimension => Values?.Length ?? 0;
    public string ModelName { get; }
    public DateTime CreatedAt { get; }

    public EmbeddingVector(float[] values, string modelName)
    {
        if (values == null || values.Length == 0)
            throw new ArgumentException("Embedding values cannot be null or empty", nameof(values));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be empty", nameof(modelName));

        Values = values.ToArray(); // Create a copy to ensure immutability
        ModelName = modelName;
        CreatedAt = DateTime.UtcNow;
    }

    public float CosineSimilarity(EmbeddingVector other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        if (other.Dimension != Dimension)
            throw new ArgumentException($"Dimension mismatch: {Dimension} vs {other.Dimension}");

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (int i = 0; i < Dimension; i++)
        {
            dotProduct += Values[i] * other.Values[i];
            normA += Values[i] * Values[i];
            normB += other.Values[i] * other.Values[i];
        }

        return dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    public bool Equals(EmbeddingVector? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Dimension != other.Dimension) return false;
        
        return Values.SequenceEqual(other.Values) && ModelName == other.ModelName;
    }

    public override bool Equals(object? obj) => Equals(obj as EmbeddingVector);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + ModelName.GetHashCode();
            hash = hash * 23 + Dimension.GetHashCode();
            return hash;
        }
    }
}