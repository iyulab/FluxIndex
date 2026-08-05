using FluentAssertions;
using FluxIndex.SDK;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>Retriever.SearchAsync</c> used to build its Core options inline with hardcoded 0.7/0.3, so a
/// caller passing <see cref="HybridSearchOptions"/> had its weights silently discarded — the SDK type
/// exposed knobs that only <c>FluxIndexContext.HybridSearchV2Async</c> honoured. Both entry points now
/// map through <see cref="HybridSearchOptionsMapper"/>; these pin that contract.
/// </summary>
public class HybridSearchOptionsMapperTests
{
    [Fact]
    public void FromSearchOptions_WithHybridOptions_HonoursCallerWeights()
    {
        var options = new HybridSearchOptions
        {
            TopK = 25,
            VectorWeight = 0.2f,
            KeywordWeight = 0.8f,
            MinSimilarity = 0.15f
        };

        var core = HybridSearchOptionsMapper.FromSearchOptions(options);

        core.VectorWeight.Should().BeApproximately(0.2, 0.0001);
        core.SparseWeight.Should().BeApproximately(0.8, 0.0001);
        core.MaxResults.Should().Be(25);
        core.MinFusedScore.Should().BeApproximately(0.15, 0.0001);
    }

    [Fact]
    public void FromSearchOptions_WithPlainSearchOptions_UsesDefaultWeights()
    {
        var options = new SearchOptions { TopK = 10, MinSimilarity = 0.0f };

        var core = HybridSearchOptionsMapper.FromSearchOptions(options);

        core.VectorWeight.Should().BeApproximately(HybridSearchOptionsMapper.DefaultVectorWeight, 0.0001);
        core.SparseWeight.Should().BeApproximately(HybridSearchOptionsMapper.DefaultSparseWeight, 0.0001);
        core.MaxResults.Should().Be(10);
    }

    [Fact]
    public void FromSearchOptions_MapsRerankingStrategyToFusionMethod()
    {
        var weighted = HybridSearchOptionsMapper.FromSearchOptions(
            new HybridSearchOptions { RerankingStrategy = RerankingStrategy.WeightedAverage });
        var rrf = HybridSearchOptionsMapper.FromSearchOptions(
            new HybridSearchOptions { RerankingStrategy = RerankingStrategy.ReciprocalRankFusion });

        weighted.FusionMethod.Should().Be(Core.Domain.Models.FusionMethod.WeightedSum);
        rrf.FusionMethod.Should().Be(Core.Domain.Models.FusionMethod.RRF);
    }

    [Fact]
    public void ToCore_KeepsSdkAndContextPathsOnOneMapping()
    {
        // FluxIndexContext.ConvertToCore delegates here; this pins the shared shape so the two
        // entry points cannot drift apart again.
        var sdk = new HybridSearchOptions { TopK = 7, VectorWeight = 0.4f, KeywordWeight = 0.6f };

        var core = HybridSearchOptionsMapper.ToCore(sdk);

        core.MaxResults.Should().Be(7);
        core.VectorWeight.Should().BeApproximately(0.4, 0.0001);
        core.SparseWeight.Should().BeApproximately(0.6, 0.0001);
    }

    // === Metadata filters ===
    //
    // Vector-only search applied SearchOptions.MetadataFilters and the hybrid path dropped them, so
    // enabling hybrid search widened the result set to the whole index without saying so. A scoping
    // bug that still returns results is the hardest kind to notice.

    [Fact]
    public void FromSearchOptions_CarriesMetadataFiltersToBothLegs()
    {
        var options = new SearchOptions
        {
            TopK = 10,
            MetadataFilters = new Dictionary<string, string> { ["workspace_id"] = "ws-a" }
        };

        var core = HybridSearchOptionsMapper.FromSearchOptions(options);

        core.Filters.Should().ContainKey("workspace_id");
        core.EffectiveVectorFilters.Should().ContainKey("workspace_id");
        core.EffectiveSparseFilters.Should().ContainKey("workspace_id",
            "the keyword leg had no filter at all, so a scoped hybrid query mixed in other scopes");
    }

    [Fact]
    public void ToCore_WithHybridOptions_CarriesMetadataFilters()
    {
        var sdk = new HybridSearchOptions
        {
            TopK = 7,
            MetadataFilters = new Dictionary<string, string> { ["tenant"] = "alpha" }
        };

        var core = HybridSearchOptionsMapper.ToCore(sdk);

        core.EffectiveVectorFilters.Should().ContainKey("tenant");
        core.EffectiveSparseFilters.Should().ContainKey("tenant");
    }

    [Fact]
    public void FromSearchOptions_WithoutFilters_LeavesBothLegsUnscoped()
    {
        var core = HybridSearchOptionsMapper.FromSearchOptions(new SearchOptions { TopK = 10 });

        core.Filters.Should().BeEmpty();
        core.EffectiveVectorFilters.Should().BeEmpty();
        core.EffectiveSparseFilters.Should().BeEmpty();
    }

    /// <summary>
    /// A leg carrying its own filter keeps it, so a caller can still differ per leg on purpose.
    /// </summary>
    [Fact]
    public void EffectiveFilters_PreferTheLegsOwnFilterOverTheQueryLevelOne()
    {
        var core = new Core.Domain.Models.HybridSearchOptions
        {
            Filters = new Dictionary<string, object> { ["scope"] = "query" }
        };
        core.SparseOptions.Filters["scope"] = "sparse";

        core.EffectiveSparseFilters["scope"].Should().Be("sparse");
        core.EffectiveVectorFilters["scope"].Should().Be("query");
    }
}
