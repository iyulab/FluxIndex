using AwesomeAssertions;
using FluxIndex.Providers.LMSupply.Services;
using LMSupply;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

/// <summary>
/// The lazily loading service announces the embedding identity from the catalog before the load and
/// verifies it against the loaded model. If the two ever differed, the fingerprint — and with it the
/// sqlite-vec table / collection name — would change after the first embedding call and silently split
/// the index. Loads the real "fast" model (cached after the first run).
/// </summary>
[Trait("Category", "Integration")]
public sealed class LMSupplyEmbeddingLazyLoadIntegrationTests
{
    [Fact]
    public async Task FastAlias_IdentityBeforeLoad_EqualsIdentityAfterLoad()
    {
        var reported = new List<DownloadProgress>();
        await using var service = new LMSupplyEmbeddingService(new LMSupplyEmbeddingOptions
        {
            ModelId = "fast",
            Progress = new Progress<DownloadProgress>(reported.Add),
            LoadTimeout = TimeSpan.FromMinutes(10),
        });

        var before = service.GetIdentity();
        service.IsLoaded.Should().BeFalse();

        await service.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        service.IsLoaded.Should().BeTrue();
        var after = service.GetIdentity();
        after.Should().Be(before);
        after.Fingerprint.Should().Be(before.Fingerprint);

        var embedding = await service.GenerateEmbeddingAsync("lazy load keeps the identity stable", TestContext.Current.CancellationToken);
        embedding.Should().HaveCount(after.Dimension);
    }

    [Fact]
    public async Task ConcurrentFirstCalls_ShareOneLoad()
    {
        await using var service = new LMSupplyEmbeddingService(new LMSupplyEmbeddingOptions { ModelId = "fast" });

        var calls = Enumerable.Range(0, 4)
            .Select(i => service.GenerateEmbeddingAsync($"concurrent call {i}", TestContext.Current.CancellationToken))
            .ToArray();
        var results = await Task.WhenAll(calls);

        results.Should().AllSatisfy(v => v.Should().HaveCount(384));
        service.IsLoaded.Should().BeTrue();
    }
}
