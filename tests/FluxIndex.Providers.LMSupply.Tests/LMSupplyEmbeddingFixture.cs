using FluxIndex.Providers.LMSupply.Services;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

/// <summary>
/// Loads a single real ONNX embedding model once and shares it across every test in
/// <see cref="LMSupplyEmbeddingCollection"/> — avoids reloading the model per test.
/// </summary>
public sealed class LMSupplyEmbeddingFixture : IAsyncLifetime
{
    private LMSupplyEmbeddingService? _service;

    /// <summary>
    /// Returns the shared service, loading the model on first call.
    /// </summary>
    public async Task<LMSupplyEmbeddingService> GetServiceAsync()
    {
        _service ??= await LMSupplyEmbeddingService.CreateAsync("fast");
        return _service;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_service is not null)
        {
            await _service.DisposeAsync();
        }
    }
}

/// <summary>
/// xUnit collection definition tying <see cref="LMSupplyEmbeddingServiceIntegrationTests"/> to the
/// shared <see cref="LMSupplyEmbeddingFixture"/>.
/// </summary>
[CollectionDefinition(Name)]
#pragma warning disable CA1711
public sealed class LMSupplyEmbeddingCollection : ICollectionFixture<LMSupplyEmbeddingFixture>
#pragma warning restore CA1711
{
    public const string Name = "LMSupply Embedding Tests";
}
