using AwesomeAssertions;
using FluxIndex.SDK;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// <c>Retriever.SearchAsync</c> used to build its Core options inline with hardcoded 0.7/0.3, so a caller's weights
/// were silently discarded. The hybrid knobs now live on <see cref="SearchOptions"/> and pass through
/// <see cref="HybridSearchOptionsMapper"/> as given; these pin that contract.
/// </summary>
public class HybridSearchOptionsMapperTests
{
    [Fact]
    public void FromSearchOptions_HonoursCallerWeights()
    {
        var options = new SearchOptions
        {
            TopK = 25,
            VectorWeight = 0.2f,
            KeywordWeight = 0.8f,
            FusionMethod = Core.Domain.Models.FusionMethod.WeightedSum,
            MinSimilarity = 0.15f
        };

        var core = HybridSearchOptionsMapper.FromSearchOptions(options);

        core.VectorWeight.Should().BeApproximately(0.2, 0.0001);
        core.SparseWeight.Should().BeApproximately(0.8, 0.0001);
        core.FusionMethod.Should().Be(Core.Domain.Models.FusionMethod.WeightedSum);
        core.MaxResults.Should().Be(25);
        core.VectorOptions.MinScore.Should().BeApproximately(0.15, 0.0001, "MinSimilarity is a floor on the vector leg");
        core.MinFusedScore.Should().Be(0, "a similarity-sized value compared with the fused score drops every result");
    }

    [Fact]
    public void FromSearchOptions_UnsetKnobs_LeaveFusionToTheService()
    {
        var options = new SearchOptions { TopK = 10, MinSimilarity = 0.0f };

        var core = HybridSearchOptionsMapper.FromSearchOptions(options);

        core.VectorWeight.Should().BeNull("no weights named, so the service chooses them per query");
        core.SparseWeight.Should().BeNull();
        core.FusionMethod.Should().BeNull();
        core.MaxResults.Should().Be(10);
    }

    [Fact]
    public void FromSearchOptions_PassesAPartialChoiceThrough()
    {
        var core = HybridSearchOptionsMapper.FromSearchOptions(
            new SearchOptions { FusionMethod = Core.Domain.Models.FusionMethod.RelativeScoreFusion });

        core.FusionMethod.Should().Be(Core.Domain.Models.FusionMethod.RelativeScoreFusion);
        core.VectorWeight.Should().BeNull("the service fills the knobs the caller left unset");
    }

    [Fact]
    public void FromSearchOptions_CarriesRrfK_AndKeepsTheServiceDefaultWhenUnset()
    {
        var explicitK = HybridSearchOptionsMapper.FromSearchOptions(new SearchOptions { RrfK = 20 });
        var defaultK = HybridSearchOptionsMapper.FromSearchOptions(new SearchOptions());

        explicitK.RrfK.Should().Be(20);
        defaultK.RrfK.Should().Be(new Core.Domain.Models.HybridSearchOptions().RrfK);
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
