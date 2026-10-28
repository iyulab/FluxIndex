using FluxIndex.Core.Application.Utilities;
using Xunit;

namespace FluxIndex.Core.Tests.Utilities;

public class VectorMathUtilitiesTests
{
    #region CosineSimilarity Tests

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        var vector = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = VectorMathUtilities.CosineSimilarity(vector, vector);

        // Assert
        Assert.InRange(result, 0.999f, 1.001f);
    }

    // Nine private copies, some accumulating in double, were replaced by this one: on embedding-sized vectors the single-precision
    // result has to stay within a few millionths of a double-precision reference, or a threshold comparison could move.
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.8)]
    [InlineData(0.99)]
    public void CosineSimilarity_On1024DimensionalVectors_StaysCloseToADoublePrecisionReference(double correlation)
    {
        var random = new Random(20261028);
        for (var pair = 0; pair < 100; pair++)
        {
            var a = new float[1024];
            var b = new float[1024];
            for (var i = 0; i < a.Length; i++)
            {
                a[i] = (float)Gaussian(random);
                b[i] = (float)(correlation * a[i] + Math.Sqrt(1 - correlation * correlation) * Gaussian(random));
            }

            double dot = 0, na = 0, nb = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += (double)a[i] * b[i];
                na += (double)a[i] * a[i];
                nb += (double)b[i] * b[i];
            }

            var reference = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
            Assert.InRange(Math.Abs(VectorMathUtilities.CosineSimilarity(a, b) - reference), 0.0, 5e-6);
        }
    }

    private static double Gaussian(Random random) =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble());

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { 0.0f, 1.0f, 0.0f };

        // Act
        var result = VectorMathUtilities.CosineSimilarity(a, b);

        // Assert
        Assert.InRange(result, -0.001f, 0.001f);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { -1.0f, -2.0f, -3.0f };

        // Act
        var result = VectorMathUtilities.CosineSimilarity(a, b);

        // Assert
        Assert.InRange(result, -1.001f, -0.999f);
    }

    [Fact]
    public void CosineSimilarity_DifferentDimensions_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f };
        var b = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = VectorMathUtilities.CosineSimilarity(a, b);

        // Assert
        Assert.Equal(0f, result);
    }

    [Fact]
    public void CosineSimilarity_NullInput_ReturnsZero()
    {
        // Act & Assert
        Assert.Equal(0f, VectorMathUtilities.CosineSimilarity(null, new float[] { 1, 2, 3 }));
        Assert.Equal(0f, VectorMathUtilities.CosineSimilarity(new float[] { 1, 2, 3 }, null));
    }

    [Fact]
    public void CosineSimilarity_EmptyVectors_ReturnsZero()
    {
        // Arrange
        var empty = Array.Empty<float>();
        var normal = new float[] { 1.0f, 2.0f };

        // Act
        var result = VectorMathUtilities.CosineSimilarity(empty, normal);

        // Assert
        Assert.Equal(0f, result);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        // Arrange
        var zero = new float[] { 0.0f, 0.0f, 0.0f };
        var normal = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = VectorMathUtilities.CosineSimilarity(zero, normal);

        // Assert
        Assert.Equal(0f, result);
    }

    #endregion

    #region FastCosineSimilarity Tests

    [Fact]
    public void FastCosineSimilarity_WithPrecomputedMagnitude_MatchesRegular()
    {
        // Arrange
        var query = new float[] { 1.0f, 2.0f, 3.0f };
        var candidate = new float[] { 4.0f, 5.0f, 6.0f };
        var queryMagnitude = VectorMathUtilities.ComputeMagnitude(query);

        // Act
        var regular = VectorMathUtilities.CosineSimilarity(query, candidate);
        var fast = VectorMathUtilities.FastCosineSimilarity(query, candidate, queryMagnitude);

        // Assert
        Assert.InRange(fast, regular - 0.001f, regular + 0.001f);
    }

    [Fact]
    public void FastCosineSimilarity_ZeroQueryMagnitude_ReturnsZero()
    {
        // Arrange
        var query = new float[] { 1.0f, 2.0f, 3.0f };
        var candidate = new float[] { 4.0f, 5.0f, 6.0f };

        // Act
        var result = VectorMathUtilities.FastCosineSimilarity(query, candidate, 0f);

        // Assert
        Assert.Equal(0f, result);
    }

    #endregion

    #region ComputeMagnitude Tests

    [Fact]
    public void ComputeMagnitude_UnitVector_ReturnsOne()
    {
        // Arrange
        var unitVector = new float[] { 1.0f, 0.0f, 0.0f };

        // Act
        var result = VectorMathUtilities.ComputeMagnitude(unitVector);

        // Assert
        Assert.InRange(result, 0.999f, 1.001f);
    }

    [Fact]
    public void ComputeMagnitude_KnownVector_ReturnsCorrectValue()
    {
        // Arrange: 3-4-5 triangle
        var vector = new float[] { 3.0f, 4.0f };
        var expected = 5.0f;

        // Act
        var result = VectorMathUtilities.ComputeMagnitude(vector);

        // Assert
        Assert.InRange(result, expected - 0.001f, expected + 0.001f);
    }

    [Fact]
    public void ComputeMagnitude_NullVector_ReturnsZero()
    {
        // Act
        var result = VectorMathUtilities.ComputeMagnitude((float[]?)null);

        // Assert
        Assert.Equal(0f, result);
    }

    #endregion

    #region EuclideanDistance Tests

    [Fact]
    public void EuclideanDistance_IdenticalVectors_ReturnsZero()
    {
        // Arrange
        var vector = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = VectorMathUtilities.EuclideanDistance(vector, vector);

        // Assert
        Assert.InRange(result, -0.001f, 0.001f);
    }

    [Fact]
    public void EuclideanDistance_KnownVectors_ReturnsCorrectValue()
    {
        // Arrange: Distance should be 5 (3-4-5 triangle)
        var a = new float[] { 0.0f, 0.0f };
        var b = new float[] { 3.0f, 4.0f };

        // Act
        var result = VectorMathUtilities.EuclideanDistance(a, b);

        // Assert
        Assert.InRange(result, 4.999f, 5.001f);
    }

    #endregion

    #region DotProduct Tests

    [Fact]
    public void DotProduct_KnownVectors_ReturnsCorrectValue()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { 4.0f, 5.0f, 6.0f };
        // 1*4 + 2*5 + 3*6 = 4 + 10 + 18 = 32
        var expected = 32.0f;

        // Act
        var result = VectorMathUtilities.DotProduct(a, b);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region DistanceToSimilarity Tests

    [Fact]
    public void DistanceToSimilarity_CosineDistance_ConvertsCorrectly()
    {
        // Arrange
        var distance = 0.3f;

        // Act
        var similarity = VectorMathUtilities.DistanceToSimilarity(distance, DistanceType.Cosine);

        // Assert
        Assert.Equal(0.7f, similarity);
    }

    [Fact]
    public void DistanceToSimilarity_EuclideanDistance_ConvertsCorrectly()
    {
        // Arrange
        var distance = 1.0f;

        // Act
        var similarity = VectorMathUtilities.DistanceToSimilarity(distance, DistanceType.Euclidean);

        // Assert - 1 / (1 + 1) = 0.5
        Assert.Equal(0.5f, similarity);
    }

    #endregion

    #region Normalize Tests

    [Fact]
    public void Normalize_NonZeroVector_ReturnsUnitVector()
    {
        // Arrange
        var vector = new float[] { 3.0f, 4.0f };

        // Act
        var normalized = VectorMathUtilities.Normalize(vector);
        var magnitude = VectorMathUtilities.ComputeMagnitude(normalized);

        // Assert
        Assert.InRange(magnitude, 0.999f, 1.001f);
    }

    [Fact]
    public void Normalize_ZeroVector_ReturnsSameVector()
    {
        // Arrange
        var zero = new float[] { 0.0f, 0.0f, 0.0f };

        // Act
        var result = VectorMathUtilities.Normalize(zero);

        // Assert
        Assert.Equal(zero, result);
    }

    [Fact]
    public void NormalizeInPlace_ModifiesOriginalVector()
    {
        // Arrange
        var vector = new float[] { 3.0f, 4.0f };

        // Act
        VectorMathUtilities.NormalizeInPlace(vector);
        var magnitude = VectorMathUtilities.ComputeMagnitude(vector);

        // Assert
        Assert.InRange(magnitude, 0.999f, 1.001f);
    }

    #endregion
}
