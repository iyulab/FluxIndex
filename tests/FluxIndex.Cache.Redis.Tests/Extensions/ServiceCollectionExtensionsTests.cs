using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Cache.Redis.Extensions;
using FluxIndex.Cache.Redis.Tests.Infrastructure;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Cache.Redis.Tests.Extensions;

/// <summary>
/// 서비스 컬렉션 확장 메서드 테스트
/// </summary>
public class ServiceCollectionExtensionsTests : RedisTestBase
{
    public ServiceCollectionExtensionsTests(ITestOutputHelper output) : base(output)
    {
    }

    [SkippableFact]
    public void AddRedisSemanticCache_WithConnectionString_RegistersServices()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Add required IEmbeddingService dependency
        services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

        // Act
        services.AddRedisSemanticCache(ConnectionString);

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        // Verify services are registered
        Assert.NotNull(serviceProvider.GetService<IConnectionMultiplexer>());
        Assert.NotNull(serviceProvider.GetService<ISemanticCacheService>());
        Assert.NotNull(serviceProvider.GetService<IOptions<RedisSemanticCacheOptions>>());

        // Verify options are configured
        var options = serviceProvider.GetRequiredService<IOptions<RedisSemanticCacheOptions>>();
        Assert.Equal(ConnectionString, options.Value.ConnectionString);
    }

    [SkippableFact]
    public void AddRedisSemanticCache_WithOptionsAction_RegistersServicesWithCustomConfiguration()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Add required IEmbeddingService dependency
        services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

        // Act
        services.AddRedisSemanticCache(options =>
        {
            options.ConnectionString = ConnectionString;
            options.KeyPrefix = "custom:test:";
            options.DefaultSimilarityThreshold = 0.8f;
            options.MaxCacheEntries = 5000;
            options.DefaultTtl = TimeSpan.FromMinutes(45);
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        var configuredOptions = serviceProvider.GetRequiredService<IOptions<RedisSemanticCacheOptions>>();
        Assert.Equal(ConnectionString, configuredOptions.Value.ConnectionString);
        Assert.Equal("custom:test:", configuredOptions.Value.KeyPrefix);
        Assert.Equal(0.8f, configuredOptions.Value.DefaultSimilarityThreshold);
        Assert.Equal(5000, configuredOptions.Value.MaxCacheEntries);
        Assert.Equal(TimeSpan.FromMinutes(45), configuredOptions.Value.DefaultTtl);
    }

    [SkippableFact]
    public void AddRedisSemanticCacheWithExistingConnection_RegistersSemanticCacheOnly()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Add required IEmbeddingService dependency
        services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

        // Pre-register Redis connection
        services.AddSingleton<IConnectionMultiplexer>(provider =>
            ConnectionMultiplexer.Connect(ConnectionString));

        // Act
        services.AddRedisSemanticCacheWithExistingConnection(options =>
        {
            options.ConnectionString = ConnectionString;
            options.KeyPrefix = "existing:";
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        // Verify semantic cache service is registered
        Assert.NotNull(serviceProvider.GetService<ISemanticCacheService>());
        Assert.NotNull(serviceProvider.GetService<IOptions<RedisSemanticCacheOptions>>());

        var options = serviceProvider.GetRequiredService<IOptions<RedisSemanticCacheOptions>>();
        Assert.Equal("existing:", options.Value.KeyPrefix);
    }

    [SkippableFact]
    public void AddRedisDistributedCacheWithSemanticCache_RegistersBothServices()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Add required IEmbeddingService dependency
        services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

        // Act
        services.AddRedisDistributedCacheWithSemanticCache(
            ConnectionString,
            configureCache: cacheOptions =>
            {
                cacheOptions.InstanceName = "TestInstance";
            },
            configureSemanticCache: semanticOptions =>
            {
                semanticOptions.KeyPrefix = "distributed:test:";
                semanticOptions.DefaultSimilarityThreshold = 0.9f;
            });

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        // Verify distributed cache is registered
        Assert.NotNull(serviceProvider.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>());

        // Verify semantic cache is registered
        Assert.NotNull(serviceProvider.GetService<ISemanticCacheService>());

        // Verify semantic cache options
        var semanticOptions = serviceProvider.GetRequiredService<IOptions<RedisSemanticCacheOptions>>();
        Assert.Equal("distributed:test:", semanticOptions.Value.KeyPrefix);
        Assert.Equal(0.9f, semanticOptions.Value.DefaultSimilarityThreshold);
    }

    [Fact]
    public void AddRedisSemanticCache_WithNullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddRedisSemanticCache("test-connection-string"));
    }

    [Fact]
    public void AddRedisSemanticCache_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // This test doesn't need Docker - testing argument validation only
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddRedisSemanticCache((string)null!));
    }

    [Fact]
    public void AddRedisSemanticCache_WithNullOptionsAction_ThrowsArgumentNullException()
    {
        // This test doesn't need Docker - testing argument validation only
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddRedisSemanticCache((Action<RedisSemanticCacheOptions>)null!));
    }

    [SkippableFact]
    public void Multiple_AddRedisSemanticCache_Calls_DoNotDuplicate()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Add required IEmbeddingService dependency
        services.AddSingleton<IEmbeddingService, MockEmbeddingService>();

        // Act
        services.AddRedisSemanticCache(ConnectionString);
        services.AddRedisSemanticCache(options =>
        {
            options.ConnectionString = ConnectionString;
            options.KeyPrefix = "second:";
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();

        // Verify all required dependencies are registered
        Assert.NotNull(serviceProvider.GetService<IConnectionMultiplexer>());
        Assert.NotNull(serviceProvider.GetService<IEmbeddingService>());
        Assert.NotNull(serviceProvider.GetService<IOptions<RedisSemanticCacheOptions>>());

        // Should only have one semantic cache service registered (TryAdd behavior)
        var semanticCacheServices = serviceProvider.GetServices<ISemanticCacheService>();
        Assert.Single(semanticCacheServices);
    }

    [SkippableFact]
    public async Task RegisteredSemanticCacheService_CanBeUsed()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Mock embedding service for testing
        var mockEmbeddingService = new MockEmbeddingService();
        services.AddSingleton<IEmbeddingService>(mockEmbeddingService);

        services.AddRedisSemanticCache(options =>
        {
            options.ConnectionString = ConnectionString;
            options.KeyPrefix = "integration:test:";
        });

        var serviceProvider = services.BuildServiceProvider();

        // Verify all dependencies are available before getting the service
        Assert.NotNull(serviceProvider.GetService<IConnectionMultiplexer>());
        Assert.NotNull(serviceProvider.GetService<IEmbeddingService>());
        Assert.NotNull(serviceProvider.GetService<IOptions<RedisSemanticCacheOptions>>());

        // Act
        var semanticCache = serviceProvider.GetRequiredService<ISemanticCacheService>();

        // Basic smoke test - ensure the service can be instantiated and called
        var statistics = await semanticCache.GetCacheStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalEntries >= 0);
    }

    private class MockEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            var vector = new float[384]; // Standard embedding size
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = 0.1f; // Simple mock vector
            }
            return Task.FromResult(vector);
        }

        public Task<IEnumerable<float[]>> GenerateEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var results = texts.Select(_ => new float[384]).ToArray();
            return Task.FromResult<IEnumerable<float[]>>(results);
        }

        public int GetEmbeddingDimension() => 384;

        public string GetModelName() => "mock-model";

        public int GetMaxTokens() => 8192;

        public Task<int> CountTokensAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text.Length / 4); // Simple approximation
        }
    }
}