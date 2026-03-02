using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Providers.LMSupply.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.Providers.LMSupply.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLMSupplyEmbedding_RegistersIEmbeddingService()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyEmbedding("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IEmbeddingService) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLMSupplyReranker_RegistersIReranker()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyReranker("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IReranker) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLMSupplyTextCompletion_RegistersITextCompletionService()
    {
        var services = new ServiceCollection();

        services.AddLMSupplyTextCompletion("test-model");

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITextCompletionService) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLMSupplyEmbedding_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyEmbedding();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLMSupplyReranker_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyReranker();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLMSupplyTextCompletion_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddLMSupplyTextCompletion();

        result.Should().BeSameAs(services);
    }
}
