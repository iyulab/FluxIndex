using AwesomeAssertions;
using FluxIndex.Providers.LMSupply.Services;
using LMSupply.Embedder;
using LMSupply.Embedder.Utils;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

/// <summary>
/// The registration is lazy, so the embedding dimension a vector store sizes itself by must be known before the model
/// loads — for every name the LMSupply catalog lists. Until 0.55.1 the provider decided "is this a catalog model" by
/// comparing the resolved entry with the catalog's model list, which holds one entry per repository: an alias whose
/// repository another alias already names (<c>multilingual-e5-small</c> shares <c>fast</c>'s) was not found, and
/// <c>FluxIndexContextBuilder.Build()</c> threw asking for <c>Dimensions</c> — on the path where the other two remedies
/// it offered cannot be reached.
/// </summary>
public class LMSupplyEmbeddingAnnounceIdentityTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("fast")]
    [InlineData("multilingual-e5-small")]
    [InlineData("intfloat/multilingual-e5-small")]
    [InlineData("fast:fp16")]
    public void ACatalogName_AnnouncesItsDimension(string modelId)
    {
        var (_, dimension) = LMSupplyEmbeddingService.AnnounceIdentity(modelId);

        EmbedderModelRegistry.Default.TryResolveCatalog(modelId, out var catalog, out _).Should().BeTrue("the fixture must name a catalog model");
        dimension.Should().Be(catalog!.Dimensions);
    }

    [Fact]
    public void EveryAliasTheCatalogLists_AnnouncesItsDimension()
    {
        var aliases = LocalEmbedder.GetAllModels().Select(m => m.AliasName).Where(a => !string.IsNullOrEmpty(a)).ToList();
        aliases.Should().NotBeEmpty("the fixture must see the catalog");

        aliases.Where(a => LMSupplyEmbeddingService.AnnounceIdentity(a).Dimension is null)
            .Should().BeEmpty("a lazy registration of a catalog alias must not need Dimensions");
    }

    [Theory]
    [InlineData("some-org/not-in-the-catalog")]
    public void AnUnknownRepository_AnnouncesNothing(string modelId)
    {
        // The registry answers an unknown repo id with a 384-dimension placeholder; announcing it would size the store
        // for a model nobody has measured.
        LMSupplyEmbeddingService.AnnounceIdentity(modelId).Dimension.Should().BeNull();
    }
}
