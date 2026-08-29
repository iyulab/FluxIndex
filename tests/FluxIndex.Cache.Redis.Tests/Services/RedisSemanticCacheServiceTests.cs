using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Cache.Redis.Services;
using FluxIndex.Cache.Redis.Tests.Infrastructure;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluxIndex.Cache.Redis.Tests.Services;

/// <summary>
/// Redis 시맨틱 캐시 서비스 테스트
/// </summary>
[Trait("Category", "Integration")]
public class RedisSemanticCacheServiceTests : RedisTestBase
{
    private IDatabase? _redis;
    private RedisSemanticCacheService? _cacheService;

    public RedisSemanticCacheServiceTests(ITestOutputHelper output) : base(output)
    {
    }

    protected override async Task OnDockerInitializedAsync()
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        _redis = redis.GetDatabase();

        var options = Microsoft.Extensions.Options.Options.Create(new RedisSemanticCacheOptions
        {
            ConnectionString = ConnectionString,
            KeyPrefix = "test:fluxindex:semantic:",
            DefaultSimilarityThreshold = 0.95f,
            DefaultTtl = TimeSpan.FromMinutes(5)
        });

        var logger = Substitute.For<ILogger<RedisSemanticCacheService>>();
        var embeddingService = Substitute.For<IEmbeddingService>();

        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => CreateMockEmbedding(callInfo.ArgAt<string>(0)));

        _cacheService = new RedisSemanticCacheService(
            redis,
            embeddingService,
            options,
            logger);
    }

    protected override Task OnDockerDisposingAsync()
    {
        _cacheService?.Dispose();
        return Task.CompletedTask;
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
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var query = "새로운 테스트 쿼리";

        // Act
        var result = await _cacheService!.GetCachedResultAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGetCachedResult_ShouldWorkCorrectly()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var query = "테스트 쿼리";
        var results = new List<CacheDocumentChunk>
        {
            new CacheDocumentChunk
            {
                Id = "test-chunk-1",
                Content = "테스트 내용",
                ChunkIndex = 0,
                DocumentId = "test-doc-1"
            }
        };

        // Act
        await _cacheService!.SetCachedResultAsync(query, results, cancellationToken: TestContext.Current.CancellationToken);
        var cachedResult = await _cacheService.GetCachedResultAsync(query, 0.9f, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cachedResult);
        Assert.Equal(query, cachedResult.CachedQuery);
        Assert.Single(cachedResult.Results);
        Assert.True(cachedResult.SimilarityScore >= 0.9f);
    }

    [Fact]
    public async Task GetCacheStatisticsAsync_ShouldReturnStatistics()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange & Act
        var statistics = await _cacheService!.GetCacheStatisticsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalEntries >= 0);
    }

    [Fact]
    public async Task InvalidateCacheAsync_ShouldWork()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Arrange
        var query = "무효화 테스트 쿼리";
        var results = new List<CacheDocumentChunk> { new CacheDocumentChunk { Content = "내용", ChunkIndex = 0, DocumentId = "doc1", Id = "chunk1" } };

        await _cacheService!.SetCachedResultAsync(query, results, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _cacheService.InvalidateCacheAsync("무효화*", TestContext.Current.CancellationToken);
        var cachedResult = await _cacheService.GetCachedResultAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(cachedResult);
    }

    [Fact]
    public async Task CompactCacheAsync_ShouldNotThrow()
    {
        // Skip test if Docker is not available
        SkipIfDockerNotAvailable();

        // Act & Assert - Should not throw
        await _cacheService!.CompactCacheAsync(TestContext.Current.CancellationToken);
    }
}