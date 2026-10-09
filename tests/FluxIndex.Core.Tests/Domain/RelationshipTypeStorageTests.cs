using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

/// <summary>
/// One <see cref="RelationshipType"/> serves the enrichment's relationships (stored in chunk metadata as JSON numbers) and
/// the hierarchy graph (stored by member name). Neither stored form may change meaning.
/// </summary>
public class RelationshipTypeStorageTests
{
    // The values chunk metadata already holds. Moving one re-labels every stored relationship.
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

    [Fact]
    public void AStoredRelationshipListReadsBackAsTheSameTypes()
    {
        var metadata = new Dictionary<string, object>();
        MetadataHelper.SerializeRelationships(metadata,
        [
            new ChunkRelationship { SourceChunkId = "a", TargetChunkId = "b", Type = RelationshipType.Similarity },
            new ChunkRelationship { SourceChunkId = "a", TargetChunkId = "c", Type = RelationshipType.Reference },
        ]);

        var read = MetadataHelper.DeserializeRelationships(metadata);

        Assert.NotNull(read);
        Assert.Equal([RelationshipType.Similarity, RelationshipType.Reference], read!.Select(r => r.Type));
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
