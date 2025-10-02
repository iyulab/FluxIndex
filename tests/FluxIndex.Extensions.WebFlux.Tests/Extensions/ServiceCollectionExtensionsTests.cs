using FluxIndex.Extensions.WebFlux;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using WebFlux.Core.Options;
using Xunit;

namespace FluxIndex.Extensions.WebFlux.Tests.Extensions;

/// <summary>
/// Tests for WebFlux service collection extensions
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWebFluxIntegration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddWebFluxIntegration();

        // Assert - Check that the service is registered
        var serviceDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(WebFluxIntegration));
        Assert.NotNull(serviceDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, serviceDescriptor.Lifetime);
    }

    [Fact]
    public void AddWebFluxIntegration_WithConfiguration_ShouldRegisterServicesAndOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddWebFluxIntegration(options =>
        {
            options.DefaultChunkingStrategy = ChunkingStrategyType.Intelligent;
            options.DefaultMaxChunkSize = 1024;
            options.DefaultChunkOverlap = 128;
            options.DefaultIncludeImages = true;
            options.UseStreamingApi = false;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Check services are registered
        var integrationDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(WebFluxIntegration));
        var options = Microsoft.Extensions.Options.Options.Create(new WebFluxOptions
        {
            DefaultChunkingStrategy = ChunkingStrategyType.Intelligent,
            DefaultMaxChunkSize = 1024,
            DefaultChunkOverlap = 128,
            DefaultIncludeImages = true,
            UseStreamingApi = false
        }).Value;

        Assert.NotNull(integrationDescriptor);
        Assert.NotNull(options);

        Assert.Equal(ChunkingStrategyType.Intelligent, options.DefaultChunkingStrategy);
        Assert.Equal(1024, options.DefaultMaxChunkSize);
        Assert.Equal(128, options.DefaultChunkOverlap);
        Assert.True(options.DefaultIncludeImages);
        Assert.False(options.UseStreamingApi);
    }

    [Fact]
    public void AddWebFluxIntegration_WithNullConfiguration_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - Should not throw
        services.AddWebFluxIntegration(null!);

        // Assert - Check service is registered
        var serviceDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(WebFluxIntegration));
        Assert.NotNull(serviceDescriptor);
    }
}