using FluentAssertions;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FluxIndex.Stack.Api.Tests;

/// <summary>
/// Unit tests for EmbeddingServiceFactory routing logic.
/// Note: Local provider tests require LMSupply model files (Category=Integration).
/// </summary>
public class EmbeddingServiceFactoryTests
{
    private readonly EmbeddingServiceFactory _factory;

    public EmbeddingServiceFactoryTests()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
        var logger = Substitute.For<ILogger<EmbeddingServiceFactory>>();

        _factory = new EmbeddingServiceFactory(cache, loggerFactory, logger);
    }

    [Fact]
    public void SupportedProviders_ContainsExpectedProviders()
    {
        _factory.SupportedProviders.Should().Contain("OpenAI");
        _factory.SupportedProviders.Should().Contain("Azure");
        _factory.SupportedProviders.Should().Contain("Local");
        _factory.SupportedProviders.Should().Contain("LMSupply");
        _factory.SupportedProviders.Should().Contain("GPUStack");
    }

    [Fact]
    public async Task CreateProviderAsync_OpenAI_WithApiKey_ReturnsOpenAIProvider()
    {
        // Act
        var provider = await _factory.CreateProviderAsync(
            providerName: "OpenAI",
            apiKey: "sk-test-key-12345",
            modelName: "text-embedding-3-small");

        // Assert
        provider.Should().BeOfType<OpenAIEmbeddingProvider>();
        provider.ModelName.Should().Be("text-embedding-3-small");
        provider.EmbeddingDimension.Should().Be(1536);
    }

    [Fact]
    public async Task CreateProviderAsync_Azure_WithApiKey_ReturnsOpenAIProvider()
    {
        // Act
        var provider = await _factory.CreateProviderAsync(
            providerName: "Azure",
            apiKey: "azure-key-12345",
            modelName: "text-embedding-3-small",
            endpointUrl: "https://my-resource.openai.azure.com/");

        // Assert
        provider.Should().BeOfType<OpenAIEmbeddingProvider>();
    }

    [Fact]
    public async Task CreateProviderAsync_GPUStack_WithApiKey_ReturnsOpenAIProvider()
    {
        // Act
        var provider = await _factory.CreateProviderAsync(
            providerName: "GPUStack",
            apiKey: "gpu-key-12345",
            modelName: "text-embedding-3-small",
            endpointUrl: "http://localhost:8080/v1");

        // Assert
        provider.Should().BeOfType<OpenAIEmbeddingProvider>();
    }

    [Fact]
    public async Task CreateProviderAsync_OpenAICompatible_WithEndpoint_ReturnsOpenAIProvider()
    {
        // Act
        var provider = await _factory.CreateProviderAsync(
            providerName: "OpenAI-Compatible",
            apiKey: "test-key",
            modelName: "custom-model",
            endpointUrl: "http://localhost:11434/v1");

        // Assert
        provider.Should().BeOfType<OpenAIEmbeddingProvider>();
    }

    [Fact]
    public async Task CreateProviderAsync_UnknownProvider_WithEndpointUrl_ReturnsOpenAIProvider()
    {
        // Unknown provider but with endpoint URL → should route to OpenAI-compatible
        var provider = await _factory.CreateProviderAsync(
            providerName: "CustomProvider",
            apiKey: "test-key",
            modelName: "custom-model",
            endpointUrl: "http://my-server:8080/v1");

        // Assert
        provider.Should().BeOfType<OpenAIEmbeddingProvider>();
    }

    [Fact]
    public async Task CreateProviderAsync_OpenAI_DefaultModel_UsesTextEmbedding3Small()
    {
        // Act — no model name specified
        var provider = await _factory.CreateProviderAsync(
            providerName: "OpenAI",
            apiKey: "sk-test-key-12345",
            modelName: null);

        // Assert — should default to text-embedding-3-small
        provider.Should().BeOfType<OpenAIEmbeddingProvider>();
        provider.ModelName.Should().Be("text-embedding-3-small");
    }
}
