using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Cache.Redis.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Cache.Redis.Tests.Services;

/// <summary>
/// Redis 시맨틱 캐시 서비스 테스트
/// </summary>
public class RedisSemanticCacheServiceTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly RedisContainer _redisContainer;
    private IDatabase? _redis;
    private RedisSemanticCacheService? _cacheService;

    public RedisSemanticCacheServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(6379, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var connectionString = _redisContainer.GetConnectionString();
        _output.WriteLine($"Redis connection string: {connectionString}");

        var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        _redis = redis.GetDatabase();

        var options = Microsoft.Extensions.Options.Options.Create(new RedisSemanticCacheOptions
        {
            ConnectionString = connectionString,
            KeyPrefix = "test:fluxindex:semantic:",
            DefaultSimilarityThreshold = 0.95f,
            DefaultTtl = TimeSpan.FromMinutes(5)
        });

        var logger = new Mock<ILogger<RedisSemanticCacheService>>();
        var embeddingService = new Mock<IEmbeddingService>();

        embeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => CreateMockEmbedding(text));

        _cacheService = new RedisSemanticCacheService(
            redis,
            embeddingService.Object,
            options,
            logger.Object);
    }

    public async Task DisposeAsync()
    {
        _cacheService?.Dispose();
        await _redisContainer.DisposeAsync();
    }

    private static float[] CreateMockEmbedding(string text)
    {
        // Create a simple hash-based embedding for testing
        var hash = text.GetHashCode();
        var vector = new float[384]; // Standard embedding size

        // Generate deterministic vector based on text hash
        var random = new Random(hash);
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(random.NextDouble() - 0.5) * 2; // Range: -1 to 1
        }

        // Normalize vector
        var magnitude = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            magnitude += vector[i] * vector[i];
        }
        magnitude = (float)Math.Sqrt(magnitude);

        if (magnitude > 0)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return vector;
    }

    [Fact]
    public async Task GetCachedResultAsync_WithNewQuery_ReturnsNull()
    {
        // Arrange
        var query = "새로운 테스트 쿼리";

        // Act
        var result = await _cacheService!.GetCachedResultAsync(query);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGetCachedResult_ShouldWorkCorrectly()
    {
        // Arrange
        var query = "테스트 쿼리";
        var results = new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "test-chunk-1",
                Content = "테스트 내용",
                ChunkIndex = 0
            }
        };

        // Act
        await _cacheService!.SetCachedResultAsync(query, results);
        var cachedResult = await _cacheService.GetCachedResultAsync(query, 0.9f);

        // Assert
        Assert.NotNull(cachedResult);
        Assert.Equal(query, cachedResult.CachedQuery);
        Assert.Single(cachedResult.Results);
        Assert.True(cachedResult.SimilarityScore >= 0.9f);
    }

    [Fact]
    public async Task GetCacheStatisticsAsync_ShouldReturnStatistics()
    {
        // Arrange & Act
        var statistics = await _cacheService!.GetCacheStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalEntries >= 0);
    }

    [Fact]
    public async Task InvalidateCacheAsync_ShouldWork()
    {
        // Arrange
        var query = "무효화 테스트 쿼리";
        var results = new List<DocumentChunk> { new DocumentChunk { Content = "내용", ChunkIndex = 0 } };

        await _cacheService!.SetCachedResultAsync(query, results);

        // Act
        await _cacheService.InvalidateCacheAsync("무효화*");
        var cachedResult = await _cacheService.GetCachedResultAsync(query);

        // Assert
        Assert.Null(cachedResult);
    }

    [Fact]
    public async Task CompactCacheAsync_ShouldNotThrow()
    {
        // Act & Assert - Should not throw
        await _cacheService!.CompactCacheAsync();
    }
}