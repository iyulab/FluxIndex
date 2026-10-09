using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

/// <summary>
/// <see cref="RelationshipType"/> is stored in two forms: the graph stores write the member name, and earlier releases wrote
/// the numeric value into chunk metadata. Neither stored form may change meaning.
/// </summary>
public class RelationshipTypeStorageTests
{
    // The values earlier releases stored. Moving one re-labels every relationship stored by number.
    [Theory]
    [InlineData(RelationshipType.Sequential, 0)]
    [InlineData(RelationshipType.Semantic, 1)]
    [InlineData(RelationshipType.Reference, 2)]
    [InlineData(RelationshipType.Causal, 3)]
    [InlineData(RelationshipType.Hierarchical, 4)]
    [InlineData(RelationshipType.Similarity, 5)]
    [InlineData(RelationshipType.Contradiction, 6)]
    [InlineData(RelationshipType.Elaboration, 7)]
    [InlineData(RelationshipType.Contrastive, 8)]
    [InlineData(RelationshipType.Complementary, 9)]
    public void EachMemberKeepsItsStoredValue(RelationshipType type, int stored)
    {
        Assert.Equal(stored, (int)type);
    }

    // The graph stores write the member name; every name the hierarchy model used before the merge still parses.
    [Theory]
    [InlineData("Sequential")]
    [InlineData("Hierarchical")]
    [InlineData("Semantic")]
    [InlineData("Reference")]
    [InlineData("Causal")]
    [InlineData("Contrastive")]
    [InlineData("Complementary")]
    public void EveryNameTheGraphStoresWroteStillParses(string stored)
    {
        Assert.True(Enum.TryParse<RelationshipType>(stored, out var type));
        Assert.Equal(stored, type.ToString());
    }
}
