using AwesomeAssertions;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests;

/// <summary>
/// The exact scan behind a scoped search computes distances with a sqlite-vec scalar function. It
/// must be the function of the table's own metric: a scan in another metric would rank candidates
/// in a different order from the KNN it continues, and its scores would not be comparable.
/// </summary>
public class SQLiteVecDistanceFunctionTests
{
    [Theory]
    [InlineData("distance_metric=cosine", "vec_distance_cosine")]
    [InlineData("distance_metric = L2", "vec_distance_l2")]
    [InlineData("distance_metric=l1", "vec_distance_l1")]
    [InlineData("", "vec_distance_l2")] // vec0's default metric
    [InlineData(null, "vec_distance_l2")]
    public void MatchesTheTableMetric(string? options, string expected)
    {
        SQLiteVecVectorStore.VecDistanceFunction(options).Should().Be(expected);
    }

    [Fact]
    public void UnknownMetric_Throws()
    {
        var act = () => SQLiteVecVectorStore.VecDistanceFunction("distance_metric=hamming");

        act.Should().Throw<NotSupportedException>().WithMessage("*hamming*");
    }
}
