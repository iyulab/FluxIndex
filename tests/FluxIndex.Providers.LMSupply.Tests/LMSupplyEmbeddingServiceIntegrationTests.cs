using AwesomeAssertions;
using FluxIndex.Providers.LMSupply.Services;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

/// <summary>
/// Exercises <see cref="LMSupplyEmbeddingService"/> against a real, locally loaded ONNX model
/// instead of a mock <c>IEmbeddingModel</c> — <see cref="LMSupplyEmbeddingServiceTests"/> proves the
/// adapter wiring, this proves the adapter actually produces usable embeddings end to end.
/// </summary>
/// <remarks>
/// Downloads the "fast" (multilingual-e5-small, 384-dim) model on first run and caches it locally
/// — first run can take a while. Assertions here are deliberately model-agnostic (dimension,
/// determinism, distinctness) rather than fixed semantic-similarity thresholds, which are brittle
/// across model/catalog changes.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(LMSupplyEmbeddingCollection.Name)]
public sealed class LMSupplyEmbeddingServiceIntegrationTests
{
    private readonly LMSupplyEmbeddingFixture _fixture;

    public LMSupplyEmbeddingServiceIntegrationTests(LMSupplyEmbeddingFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_RealModel_ReturnsVectorMatchingDeclaredDimension()
    {
        var service = await _fixture.GetServiceAsync();

        var embedding = await service.GenerateEmbeddingAsync("The quick brown fox jumps over the lazy dog.", TestContext.Current.CancellationToken);

        embedding.Should().HaveCount(service.GetEmbeddingDimension());
        embedding.Should().OnlyContain(v => float.IsFinite(v));
        embedding.Should().Contain(v => v != 0f);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_RealModel_IsDeterministicForSameText()
    {
        var service = await _fixture.GetServiceAsync();
        const string text = "FluxIndex retrieves relevant chunks for RAG pipelines.";

        var first = await service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);
        var second = await service.GenerateEmbeddingAsync(text, TestContext.Current.CancellationToken);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_RealModel_DifferentTextsProduceDifferentVectors()
    {
        var service = await _fixture.GetServiceAsync();

        var a = await service.GenerateEmbeddingAsync("Cats are independent household pets.", TestContext.Current.CancellationToken);
        var b = await service.GenerateEmbeddingAsync("Quarterly revenue exceeded analyst expectations.", TestContext.Current.CancellationToken);

        a.Should().NotBeEquivalentTo(b);
    }

    [Fact]
    public async Task GenerateEmbeddingsBatchAsync_RealModel_MatchesIndividualEmbeddings()
    {
        var service = await _fixture.GetServiceAsync();
        string[] texts = ["First sentence.", "Second, unrelated sentence."];

        var batch = (await service.GenerateEmbeddingsBatchAsync(texts, TestContext.Current.CancellationToken)).ToList();
        var individual = new List<float[]>();
        foreach (var t in texts)
        {
            individual.Add(await service.GenerateEmbeddingAsync(t, TestContext.Current.CancellationToken));
        }

        batch.Should().HaveCount(texts.Length);
        for (var i = 0; i < texts.Length; i++)
        {
            batch[i].Should().BeEquivalentTo(individual[i], options => options.WithStrictOrdering());
        }
    }
}
