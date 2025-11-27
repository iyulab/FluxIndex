using FluentAssertions;
using FluxIndex.AI.LocalEmbedder;
using FluxIndex.AI.LocalEmbedder.Services;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.AI.LocalEmbedder.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLocalEmbedder_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedder();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        embeddingService.Should().NotBeNull();
        embeddingService.Should().BeOfType<LocalEmbedderService>();
    }

    [Fact]
    public void AddLocalEmbedder_WithOptions_ShouldApplyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedder(options =>
        {
            options.ModelId = "bge-small-en-v1.5";
            options.ExecutionProvider = LocalEmbedderExecutionProvider.CPU;
            options.PoolingMode = LocalEmbedderPoolingMode.Cls;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        embeddingService.Should().NotBeNull();
        embeddingService!.GetModelName().Should().Be("bge-small-en-v1.5");
    }

    [Fact]
    public void AddLocalEmbedder_WithModelId_ShouldApplyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedder("all-mpnet-base-v2");
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        embeddingService.Should().NotBeNull();
        embeddingService!.GetModelName().Should().Be("all-mpnet-base-v2");
    }

    [Fact]
    public void AddLocalEmbedder_ShouldRegisterAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedder();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var service1 = scope1.ServiceProvider.GetService<IEmbeddingService>();
        var service2 = scope2.ServiceProvider.GetService<IEmbeddingService>();

        // Same instance should be returned (singleton)
        service1.Should().BeSameAs(service2);
    }

    [Fact]
    public void AddLocalEmbedder_WithNullOptions_ShouldUseDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedder((Action<LocalEmbedderOptions>?)null);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        embeddingService.Should().NotBeNull();
        embeddingService!.GetModelName().Should().Be("all-MiniLM-L6-v2");
    }

    [Fact]
    public void AddLocalEmbedderMultilingual_ShouldConfigureMultilingualModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedderMultilingual();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        embeddingService.Should().NotBeNull();
        embeddingService!.GetModelName().Should().Be("multilingual-e5-small");
    }

    [Fact]
    public void AddLocalEmbedderWithCuda_ShouldConfigureCudaProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedderWithCuda("bge-base-en-v1.5");
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        embeddingService.Should().NotBeNull();
        embeddingService!.GetModelName().Should().Be("bge-base-en-v1.5");

        var options = serviceProvider.GetService<IOptions<LocalEmbedderOptions>>();
        options.Should().NotBeNull();
        options!.Value.ExecutionProvider.Should().Be(LocalEmbedderExecutionProvider.CUDA);
    }

    [Fact]
    public void AddLocalEmbedderWithDirectML_ShouldConfigureDirectMLProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedderWithDirectML();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var embeddingService = serviceProvider.GetService<IEmbeddingService>();
        embeddingService.Should().NotBeNull();

        var options = serviceProvider.GetService<IOptions<LocalEmbedderOptions>>();
        options.Should().NotBeNull();
        options!.Value.ExecutionProvider.Should().Be(LocalEmbedderExecutionProvider.DirectML);
    }

    [Fact]
    public void AddLocalEmbedder_ShouldRegisterMemoryCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedder();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var memoryCache = serviceProvider.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        memoryCache.Should().NotBeNull();
    }

    [Fact]
    public void AddLocalEmbedder_ShouldReturnServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var result = services.AddLocalEmbedder();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddLocalEmbedder_MultipleCallsWithDifferentOptions_ShouldUseLastConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalEmbedder(opt => opt.ModelId = "model-1");
        services.AddLocalEmbedder(opt => opt.ModelId = "model-2");
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Last registration wins
        var options = serviceProvider.GetService<IOptions<LocalEmbedderOptions>>();
        options.Should().NotBeNull();
        options!.Value.ModelId.Should().Be("model-2");
    }
}
