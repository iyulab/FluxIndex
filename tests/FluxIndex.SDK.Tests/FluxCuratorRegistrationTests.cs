using AwesomeAssertions;
using FluxCurator.Core;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Integrations.FluxCurator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace FluxIndex.SDK.Tests;

/// <summary>
/// Both registration paths put a curator in the container under the contract a consumer resolves: <see cref="IFluxCurator"/>.
/// </summary>
public class FluxCuratorRegistrationTests
{
    [Fact]
    public async Task AddFluxIndexFluxCurator_RegistersIFluxCurator()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IEmbeddingService>());
        services.AddFluxIndexFluxCurator();
        await using var provider = services.BuildServiceProvider();

        var curator = provider.GetService<IFluxCurator>();

        curator.Should().NotBeNull();
    }

    [Fact]
    public async Task AddFluxCuratorBasic_RegistersIFluxCuratorThatChunks()
    {
        var services = new ServiceCollection();
        services.AddFluxCuratorBasic();
        await using var provider = services.BuildServiceProvider();

        var curator = provider.GetRequiredService<IFluxCurator>();
        var chunks = await curator.ChunkAsync("First sentence here. Second sentence here.", TestContext.Current.CancellationToken);

        chunks.Should().NotBeEmpty();
    }
}
