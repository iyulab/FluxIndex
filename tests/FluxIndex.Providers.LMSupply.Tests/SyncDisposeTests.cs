using Flux.Abstractions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.LMSupply.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

/// <summary>
/// A container disposed with <c>Dispose()</c> throws on a singleton that is only <see cref="IAsyncDisposable"/>. The three
/// LMSupply services implement both, so a console app or test that builds its provider with <c>using</c> exits cleanly.
/// Resolution loads nothing (lazy), so this runs without a model.
/// </summary>
public class SyncDisposeTests
{
    [Fact]
    public void Container_WithAllThreeServicesResolved_DisposesSynchronously()
    {
        var services = new ServiceCollection();
        services.AddLMSupplyEmbedding("fast");
        services.AddLMSupplyReranker("auto");
        services.AddLMSupplyTextCompletion("default");
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IEmbeddingService>();
        _ = provider.GetRequiredService<IReranker>();
        _ = provider.GetRequiredService<ITextCompletionService>();

        var dispose = Record.Exception(provider.Dispose);

        Assert.Null(dispose);
    }
}
