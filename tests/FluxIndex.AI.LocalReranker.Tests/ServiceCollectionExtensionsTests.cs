using FluentAssertions;
using FluxIndex.AI.LocalReranker;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxIndex.AI.LocalReranker.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLocalReranker_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalReranker();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var reranker = serviceProvider.GetService<IReranker>();
        reranker.Should().NotBeNull();
        reranker.Should().BeOfType<LocalRerankerAdapter>();
    }

    [Fact]
    public void AddLocalReranker_WithOptions_ShouldApplyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalReranker(options =>
        {
            options.ModelId = "quality";
            options.UseGpu = true;
            options.BatchSize = 64;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var reranker = serviceProvider.GetService<IReranker>();
        reranker.Should().NotBeNull();

        var modelInfo = reranker!.GetModelInfo();
        modelInfo.Capabilities.Should().ContainKey("model_id");
        modelInfo.Capabilities["model_id"].Should().Be("quality");
    }

    [Fact]
    public void AddLocalReranker_ShouldRegisterAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalReranker(options => options.ModelId = "fast");
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var reranker1 = scope1.ServiceProvider.GetService<IReranker>();
        var reranker2 = scope2.ServiceProvider.GetService<IReranker>();

        // Same instance should be returned (singleton)
        reranker1.Should().BeSameAs(reranker2);
    }

    [Fact]
    public void AddLocalReranker_WithNullOptions_ShouldUseDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalReranker(null);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var reranker = serviceProvider.GetService<IReranker>();
        reranker.Should().NotBeNull();

        var modelInfo = reranker!.GetModelInfo();
        modelInfo.Capabilities["model_id"].Should().Be("default");
    }

    [Fact]
    public void AddLocalRerankerWithWarmup_ShouldRegisterHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLocalRerankerWithWarmup(options => options.ModelId = "fast");

        // Assert
        var hostedServiceDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
                 d.ImplementationType == typeof(LocalRerankerWarmupService));

        hostedServiceDescriptor.Should().NotBeNull();
    }
}
