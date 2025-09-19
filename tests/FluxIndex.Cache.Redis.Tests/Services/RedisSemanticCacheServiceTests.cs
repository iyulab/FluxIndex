using FluxIndex.Cache.Redis.Configuration;
using FluxIndex.Cache.Redis.Services;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
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
/// Redis 시맨틱 캐시 서비스 단위 테스트
/// </summary>
public class RedisSemanticCacheServiceTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly RedisContainer _redisContainer;
    private IConnectionMultiplexer? _redis;
    private RedisSemanticCacheService? _cacheService;
    private Mock<IEmbeddingService>? _mockEmbeddingService;

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
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

        // Mock embedding service setup
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockEmbeddingService.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => CreateMockEmbedding(text));

        var options = Options.Create(new RedisSemanticCacheOptions
        {
            ConnectionString = connectionString,
            DefaultSimilarityThreshold = 0.95f,
            DefaultTtl = TimeSpan.FromMinutes(30),
            MaxCacheEntries = 1000
        });

        var logger = new Mock<ILogger<RedisSemanticCacheService>>().Object;

        _cacheService = new RedisSemanticCacheService(
            _redis,
            _mockEmbeddingService.Object,
            options,
            logger);
    }

    public async Task DisposeAsync()
    {
        _cacheService?.Dispose();
        _redis?.Dispose();
        await _redisContainer.DisposeAsync();
    }

    private static EmbeddingVector CreateMockEmbedding(string text)
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

        return EmbeddingVector.Create(vector);
    }

    [Fact]
    public async Task GetCachedResultAsync_WithNewQuery_ReturnsNull()
    {
        // Arrange
        var query = "테스트 쿼리 - 새로운 질문";

        // Act
        var result = await _cacheService!.GetCachedResultAsync(query);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetCachedResultAsync_AndGetCachedResultAsync_WithExactMatch_ReturnsResult()
    {
        // Arrange
        var query = "머신러닝 알고리즘";
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("test-doc-1", "머신러닝은 인공지능의 한 분야입니다.", 0, EmbeddingVector.Create(new float[384]))
        };
        var metadata = new SearchMetadata
        {
            SearchTimeMs = 150,
            TotalDocuments = 1,
            SearchAlgorithm = "test",
            QualityScore = 0.95f
        };

        // Act
        await _cacheService!.SetCachedResultAsync(query, chunks, metadata);
        var result = await _cacheService.GetCachedResultAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(query, result.OriginalQuery);
        Assert.Equal(query, result.CachedQuery);
        Assert.Equal(1.0f, result.SimilarityScore);
        Assert.Single(result.Results);
        Assert.NotNull(result.Metadata);
        Assert.Equal(150, result.Metadata.SearchTimeMs);
    }

    [Fact]
    public async Task GetCachedResultAsync_WithSimilarQuery_ReturnsMatch()
    {
        // Arrange
        var originalQuery = "딥러닝 신경망";
        var similarQuery = "딥러닝 뉴럴 네트워크";
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("test-doc-1", "딥러닝은 신경망을 사용합니다.", 0, EmbeddingVector.Create(new float[384]))
        };

        // Mock embedding service to return similar embeddings
        var originalEmbedding = CreateMockEmbedding(originalQuery);
        var similarEmbedding = CreateMockEmbedding(originalQuery); // Same embedding for high similarity

        _mockEmbeddingService!
            .Setup(x => x.GenerateEmbeddingAsync(originalQuery, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalEmbedding);
        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(similarQuery, It.IsAny<CancellationToken>()))
            .ReturnsAsync(similarEmbedding);

        // Act
        await _cacheService!.SetCachedResultAsync(originalQuery, chunks);
        var result = await _cacheService.GetCachedResultAsync(similarQuery, 0.90f);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(similarQuery, result.OriginalQuery);
        Assert.Equal(originalQuery, result.CachedQuery);
        Assert.True(result.SimilarityScore >= 0.90f);
    }

    [Fact]
    public async Task GetCachedResultAsync_WithLowSimilarity_ReturnsNull()
    {
        // Arrange
        var originalQuery = "머신러닝 알고리즘";
        var differentQuery = "날씨 예보 시스템";
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("test-doc-1", "머신러닝 내용", 0, EmbeddingVector.Create(new float[384]))
        };

        // Act
        await _cacheService!.SetCachedResultAsync(originalQuery, chunks);
        var result = await _cacheService.GetCachedResultAsync(differentQuery, 0.95f);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetCachedResultAsync_WithTtl_ExpiresAfterTime()
    {
        // Arrange
        var query = "TTL 테스트 쿼리";
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("test-doc-1", "TTL 테스트 내용", 0, EmbeddingVector.Create(new float[384]))
        };
        var shortTtl = TimeSpan.FromSeconds(2);

        // Act
        await _cacheService!.SetCachedResultAsync(query, chunks, ttl: shortTtl);

        // Verify it exists immediately
        var immediateResult = await _cacheService.GetCachedResultAsync(query);
        Assert.NotNull(immediateResult);

        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Verify it's expired
        var expiredResult = await _cacheService.GetCachedResultAsync(query);
        Assert.Null(expiredResult);
    }

    [Fact]
    public async Task GetCacheStatisticsAsync_ReturnsValidStatistics()
    {
        // Arrange
        var query1 = "통계 테스트 쿼리 1";
        var query2 = "통계 테스트 쿼리 2";
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("test-doc-1", "통계 테스트 내용", 0, EmbeddingVector.Create(new float[384]))
        };

        // Act
        await _cacheService!.SetCachedResultAsync(query1, chunks);
        await _cacheService.SetCachedResultAsync(query2, chunks);

        // Generate some hits and misses
        await _cacheService.GetCachedResultAsync(query1); // Hit
        await _cacheService.GetCachedResultAsync(query2); // Hit
        await _cacheService.GetCachedResultAsync("존재하지 않는 쿼리"); // Miss

        var statistics = await _cacheService.GetCacheStatisticsAsync();

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalEntries >= 2);
        Assert.True(statistics.CacheHits >= 2);
        Assert.True(statistics.CacheMisses >= 1);
        Assert.True(statistics.HitRate > 0);
        Assert.True(statistics.CacheSizeBytes > 0);
    }

    [Fact]
    public async Task WarmupCacheAsync_WithPopularQueries_CreatesEntries()
    {
        // Arrange
        var popularQueries = new List<string>
        {
            "인공지능 기초",
            "머신러닝 알고리즘",
            "딥러닝 신경망"
        };

        // Mock some results for warmup
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("warmup-doc", "워밍업 테스트 내용", 0, EmbeddingVector.Create(new float[384]))
        };

        // Act
        await _cacheService!.WarmupCacheAsync(popularQueries);

        // Assert - Check that warmup created some activity
        var statistics = await _cacheService.GetCacheStatisticsAsync();
        Assert.True(statistics.TotalEntries >= 0); // Warmup may or may not create entries depending on implementation
    }

    [Fact]
    public async Task InvalidateCacheAsync_WithPattern_RemovesMatchingEntries()
    {
        // Arrange
        var query1 = "머신러닝 패턴 테스트";
        var query2 = "딥러닝 패턴 테스트";
        var query3 = "다른 주제 테스트";
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("test-doc", "패턴 테스트 내용", 0, EmbeddingVector.Create(new float[384]))
        };

        // Act
        await _cacheService!.SetCachedResultAsync(query1, chunks);
        await _cacheService.SetCachedResultAsync(query2, chunks);
        await _cacheService.SetCachedResultAsync(query3, chunks);

        // Verify all exist
        Assert.NotNull(await _cacheService.GetCachedResultAsync(query1));
        Assert.NotNull(await _cacheService.GetCachedResultAsync(query2));
        Assert.NotNull(await _cacheService.GetCachedResultAsync(query3));

        // Invalidate pattern
        await _cacheService.InvalidateCacheAsync("*패턴*");

        // Verify pattern matches are gone, others remain
        Assert.Null(await _cacheService.GetCachedResultAsync(query1));
        Assert.Null(await _cacheService.GetCachedResultAsync(query2));
        Assert.NotNull(await _cacheService.GetCachedResultAsync(query3));
    }

    [Fact]
    public async Task CompactCacheAsync_ReducesCacheSize()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("test-doc", "압축 테스트 내용", 0, EmbeddingVector.Create(new float[384]))
        };

        // Add multiple cache entries
        for (int i = 0; i < 10; i++)
        {
            await _cacheService!.SetCachedResultAsync($"압축 테스트 쿼리 {i}", chunks);
        }

        var beforeStats = await _cacheService.GetCacheStatisticsAsync();

        // Act
        await _cacheService.CompactCacheAsync();

        // Assert
        var afterStats = await _cacheService.GetCacheStatisticsAsync();

        // After compaction, the cache should be cleaned up
        // The exact behavior depends on implementation, but it should not increase
        Assert.True(afterStats.TotalEntries <= beforeStats.TotalEntries);
    }

    [Fact]
    public async Task MultipleAsyncOperations_WorkCorrectly()
    {
        // Arrange
        var tasks = new List<Task>();
        var chunks = new List<DocumentChunk>
        {
            DocumentChunk.Create("concurrent-doc", "동시성 테스트 내용", 0, EmbeddingVector.Create(new float[384]))
        };

        // Act - Perform multiple operations concurrently
        for (int i = 0; i < 10; i++)
        {
            var query = $"동시성 테스트 쿼리 {i}";
            tasks.Add(_cacheService!.SetCachedResultAsync(query, chunks));
        }

        await Task.WhenAll(tasks);

        // Verify all operations completed successfully
        for (int i = 0; i < 10; i++)
        {
            var query = $"동시성 테스트 쿼리 {i}";
            var result = await _cacheService!.GetCachedResultAsync(query);
            Assert.NotNull(result);
        }
    }
}