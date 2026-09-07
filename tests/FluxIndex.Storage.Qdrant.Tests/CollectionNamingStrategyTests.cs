using AwesomeAssertions;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Pins the numeric values of <see cref="CollectionNamingStrategy"/>. A configuration source that
/// binds the strategy as a number rather than a name would otherwise change meaning whenever a
/// member is added or removed, and it would do so silently — the removal of the deprecated
/// dimension-suffix member is exactly the kind of edit that renumbers the rest.
/// </summary>
public class CollectionNamingStrategyTests
{
    [Fact]
    public void Fixed_KeepsTheNumericValueItShippedWith()
    {
        ((int)CollectionNamingStrategy.Fixed).Should().Be(1);
    }

    [Fact]
    public void ModelFingerprint_KeepsTheNumericValueItShippedWith()
    {
        ((int)CollectionNamingStrategy.ModelFingerprint).Should().Be(2);
    }
}
