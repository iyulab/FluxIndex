using FluxIndex.AI.OpenAI;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.AI.OpenAI.Tests.Extensions;

/// <summary>
/// Tests for service collection extensions
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOpenAIEmbedding_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOpenAIEmbedding(options =>
        {
            options.ApiKey = "test-key";
            options.ModelName = "text-embedding-3-small";
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        var options = serviceProvider.GetService<IOptions<OpenAIOptions>>();

        Assert.NotNull(embeddingService);
        Assert.NotNull(options);
        Assert.Equal("test-key", options.Value.ApiKey);
        Assert.Equal("text-embedding-3-small", options.Value.ModelName);
    }

    [Fact]
    public void AddAzureOpenAIEmbedding_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddAzureOpenAIEmbedding(options =>
        {
            options.ApiKey = "azure-test-key";
            options.Endpoint = "https://test.openai.azure.com";
            options.ModelName = "text-embedding-ada-002";
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        var options = serviceProvider.GetService<IOptions<OpenAIOptions>>();

        Assert.NotNull(embeddingService);
        Assert.NotNull(options);
        Assert.Equal("azure-test-key", options.Value.ApiKey);
        Assert.Equal("https://test.openai.azure.com", options.Value.Endpoint);
        Assert.Equal("text-embedding-ada-002", options.Value.ModelName);
    }

    [Fact]
    public void AddOpenAIEmbedding_ShouldRegisterMemoryCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddOpenAIEmbedding(options =>
        {
            options.ApiKey = "test-key";
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var cache = serviceProvider.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        Assert.NotNull(cache);
    }
}