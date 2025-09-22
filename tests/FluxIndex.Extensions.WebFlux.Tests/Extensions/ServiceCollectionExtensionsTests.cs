using FluxIndex.Extensions.WebFlux;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
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
            options.MaxDepth = 3;
            options.FollowExternalLinks = true;
            options.ChunkingStrategy = "Custom";
            options.MaxChunkSize = 1024;
            options.ChunkOverlap = 128;
            options.IncludeImages = true;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Check services are registered
        var integrationDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(WebFluxIntegration));
        var options = serviceProvider.GetService<WebFluxOptions>();

        Assert.NotNull(integrationDescriptor);
        Assert.NotNull(options);

        Assert.Equal(3, options.MaxDepth);
        Assert.True(options.FollowExternalLinks);
        Assert.Equal("Custom", options.ChunkingStrategy);
        Assert.Equal(1024, options.MaxChunkSize);
        Assert.Equal(128, options.ChunkOverlap);
        Assert.True(options.IncludeImages);
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