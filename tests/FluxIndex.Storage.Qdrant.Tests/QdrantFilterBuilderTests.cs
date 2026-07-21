using FluxIndex.Storage.Qdrant;
using Qdrant.Client.Grpc;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Unit tests for QdrantVectorStore.BuildQdrantFilter — the IVectorStore filter contract
/// as pushed down to Qdrant conditions. No Docker required.
/// </summary>
public class QdrantFilterBuilderTests
{
    [Fact]
    public void NullOrEmptyFilters_ReturnsNull()
    {
        Assert.Null(QdrantVectorStore.BuildQdrantFilter(null));
        Assert.Null(QdrantVectorStore.BuildQdrantFilter([]));
    }

    [Fact]
    public void ScalarValue_BecomesKeywordMatch()
    {
        var filter = QdrantVectorStore.BuildQdrantFilter(new Dictionary<string, object>
        {
            ["document_id"] = "hash1"
        });

        var condition = Assert.Single(filter!.Must);
        Assert.Equal("document_id", condition.Field.Key);
        Assert.Equal("hash1", condition.Field.Match.Keyword);
    }

    [Fact]
    public void CollectionValue_BecomesMatchAnyKeywords()
    {
        // document_id ∈ {hash1, hash2, hash3} — single query instead of N-way fan-out.
        var filter = QdrantVectorStore.BuildQdrantFilter(new Dictionary<string, object>
        {
            ["document_id"] = new List<string> { "hash1", "hash2", "hash3" }
        });

        var condition = Assert.Single(filter!.Must);
        Assert.Equal("document_id", condition.Field.Key);
        Assert.Equal(Match.MatchValueOneofCase.Keywords, condition.Field.Match.MatchValueCase);
        Assert.Equal(["hash1", "hash2", "hash3"], condition.Field.Match.Keywords.Strings);
    }

    [Fact]
    public void MultipleKeys_CombineWithMust()
    {
        var filter = QdrantVectorStore.BuildQdrantFilter(new Dictionary<string, object>
        {
            ["document_id"] = new[] { "h1", "h2" },
            ["tenant"] = "t-1"
        });

        Assert.Equal(2, filter!.Must.Count);
        var docCondition = filter.Must.Single(c => c.Field.Key == "document_id");
        Assert.Equal(Match.MatchValueOneofCase.Keywords, docCondition.Field.Match.MatchValueCase);
        // Non-standard key gets the meta_ prefix (metadata storage convention).
        var tenantCondition = filter.Must.Single(c => c.Field.Key == "meta_tenant");
        Assert.Equal("t-1", tenantCondition.Field.Match.Keyword);
    }

    [Fact]
    public void NonScalarValue_Throws_InsteadOfSilentZeroResults()
    {
        // Previously List<string> degraded to its ToString() type name
        // ("System.Collections.Generic.List`1[...]") and matched nothing without any signal.
        // Arbitrary objects still make no sense as filter values — they must throw.
        Assert.Throws<ArgumentException>(() => QdrantVectorStore.BuildQdrantFilter(
            new Dictionary<string, object> { ["document_id"] = new object() }));
    }

    [Fact]
    public void EmptyCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => QdrantVectorStore.BuildQdrantFilter(
            new Dictionary<string, object> { ["document_id"] = new List<string>() }));
    }

    [Fact]
    public void NumericCollection_NormalizesToKeywordStrings()
    {
        var filter = QdrantVectorStore.BuildQdrantFilter(new Dictionary<string, object>
        {
            ["chunk_index"] = new[] { 1, 2 }
        });

        var condition = Assert.Single(filter!.Must);
        Assert.Equal(["1", "2"], condition.Field.Match.Keywords.Strings);
    }
}
