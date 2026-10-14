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
            DefaultTtl = TimeSpan.FromMinutes(5)
        });

        var logger = Substitute.For<ILogger<RedisSemanticCacheService>>();
        var embeddingService = Substitute.For<IEmbeddingService>();

        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => CreateMockEmbedding(callInfo.ArgAt<string>(0)));
        // The cache embeds lookups in the query role (FluxIndex 0.68.0); this symmetric double answers both alike.
        embeddingService.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        // Create a simple hash-based embedding for testing — seeded by a process-stable hash
        // (string.GetHashCode() differs per process, see FluxIndex.Core.Tests.TestHash)
        var hash = FluxIndex.Core.Tests.TestHash.Fnv1a(text);
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
        var result = await _cacheService!.GetCachedResultAsync(query, FilterScope.Unscoped, cancellationToken: TestContext.Current.CancellationToken);

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
        await _cacheService!.SetCachedResultAsync(query, FilterScope.Unscoped, results, cancellationToken: TestContext.Current.CancellationToken);
        var cachedResult = await _cacheService.GetCachedResultAsync(query, FilterScope.Unscoped, 0.9f, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(cachedResult);
        Assert.Equal(query, cachedResult.CachedQuery);
        Assert.Single(cachedResult.Results);
        Assert.True(cachedResult.SimilarityScore >= 0.9f);
    }

    [Fact]
    public async Task TheSameQuestionInTwoScopes_IsAnsweredFromItsOwnScope()
    {
        SkipIfDockerNotAvailable();
        var ct = TestContext.Current.CancellationToken;
        var query = "scoped question";
        var tenantA = FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "a" });
        var tenantB = FilterScope.Of(new Dictionary<string, object> { ["tenant"] = "b" });

        await _cacheService!.SetCachedResultAsync(query, tenantA, [new CacheDocumentChunk { Id = "a", DocumentId = "doc-a", Content = "tenant a" }], cancellationToken: ct);
        await _cacheService.SetCachedResultAsync(query, tenantB, [new CacheDocumentChunk { Id = "b", DocumentId = "doc-b", Content = "tenant b" }], cancellationToken: ct);

        var a = await _cacheService.GetCachedResultAsync(query, tenantA, cancellationToken: ct);
        var b = await _cacheService.GetCachedResultAsync(query, tenantB, cancellationToken: ct);

        Assert.Equal("doc-a", Assert.Single(a!.Results).DocumentId);
        Assert.Equal("doc-b", Assert.Single(b!.Results).DocumentId);
        Assert.Equal(query, a.CachedQuery);
        Assert.Null(await _cacheService.GetCachedResultAsync(query, FilterScope.Unscoped, cancellationToken: ct));

        // Invalidation by pattern matches the query, whichever scope it is stored in.
        await _cacheService.InvalidateCacheAsync(query, ct);
        Assert.Null(await _cacheService.GetCachedResultAsync(query, tenantA, cancellationToken: ct));
        Assert.Null(await _cacheService.GetCachedResultAsync(query, tenantB, cancellationToken: ct));
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

        await _cacheService!.SetCachedResultAsync(query, FilterScope.Unscoped, results, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _cacheService.InvalidateCacheAsync("무효화*", TestContext.Current.CancellationToken);
        var cachedResult = await _cacheService.GetCachedResultAsync(query, FilterScope.Unscoped, cancellationToken: TestContext.Current.CancellationToken);

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