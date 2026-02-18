using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

#region CacheResult Tests

public class CacheResultTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new CacheResult();

        Assert.Equal(string.Empty, result.CachedResponse);
        Assert.Empty(result.SearchResults);
        Assert.Equal(0f, result.SimilarityScore);
        Assert.Equal(string.Empty, result.OriginalQuery);
        Assert.Null(result.Expiry);
        Assert.NotNull(result.Metadata);
        Assert.Equal(CacheHitType.Exact, result.HitType);
        Assert.Equal(0f, result.QualityScore);
    }

    [Fact]
    public void Create_SetsAllProperties()
    {
        var searchResults = new List<SearchResult>
        {
            new() { Content = "Result 1" },
            new() { Content = "Result 2" },
            new() { Content = "Result 3" }
        };
        var metadata = new CacheMetadata { CacheKey = "test-key" };

        var result = CacheResult.Create(
            cachedResponse: "Test response",
            searchResults: searchResults,
            similarityScore: 0.95f,
            originalQuery: "test query",
            metadata: metadata,
            expiry: TimeSpan.FromHours(1),
            hitType: CacheHitType.Semantic);

        Assert.Equal("Test response", result.CachedResponse);
        Assert.Equal(3, result.SearchResults.Count);
        Assert.Equal(0.95f, result.SimilarityScore);
        Assert.Equal("test query", result.OriginalQuery);
        Assert.Equal(TimeSpan.FromHours(1), result.Expiry);
        Assert.Equal("test-key", result.Metadata.CacheKey);
        Assert.Equal(CacheHitType.Semantic, result.HitType);
    }

    [Fact]
    public void Create_NullMetadata_CreatesDefaultMetadata()
    {
        var result = CacheResult.Create(
            cachedResponse: "response",
            searchResults: new List<SearchResult>(),
            similarityScore: 0.8f,
            originalQuery: "query");

        Assert.NotNull(result.Metadata);
        Assert.Equal(string.Empty, result.Metadata.CacheKey);
    }

    [Fact]
    public void Create_DefaultHitType_IsSemantic()
    {
        var result = CacheResult.Create(
            cachedResponse: "response",
            searchResults: new List<SearchResult>(),
            similarityScore: 0.8f,
            originalQuery: "query");

        Assert.Equal(CacheHitType.Semantic, result.HitType);
    }

    [Theory]
    [InlineData(0.9f, 0, 0.63f)]   // 0.9*0.7 + 0/10*0.3 = 0.63
    [InlineData(0.9f, 5, 0.78f)]   // 0.9*0.7 + 0.5*0.3 = 0.63+0.15 = 0.78
    [InlineData(0.9f, 10, 0.93f)]  // 0.9*0.7 + 1.0*0.3 = 0.63+0.30 = 0.93
    [InlineData(0.9f, 20, 0.93f)]  // capped at 10: 0.9*0.7 + 1.0*0.3 = 0.93
    [InlineData(0.0f, 0, 0.0f)]    // 0.0*0.7 + 0.0*0.3 = 0.0
    [InlineData(1.0f, 10, 1.0f)]   // 1.0*0.7 + 1.0*0.3 = 1.0
    public void Create_CalculatesQualityScore_Correctly(float similarity, int resultCount, float expected)
    {
        var results = Enumerable.Range(0, resultCount)
            .Select(_ => new SearchResult { Content = "r" })
            .ToList();

        var cacheResult = CacheResult.Create(
            cachedResponse: "resp",
            searchResults: results,
            similarityScore: similarity,
            originalQuery: "q");

        Assert.Equal(expected, cacheResult.QualityScore, 0.01f);
    }

    [Fact]
    public void IsExpired_NoExpiry_ReturnsFalse()
    {
        var result = new CacheResult
        {
            CachedAt = DateTime.UtcNow.AddHours(-48),
            Expiry = null
        };

        Assert.False(result.IsExpired);
    }

    [Fact]
    public void IsExpired_WithinExpiry_ReturnsFalse()
    {
        var result = new CacheResult
        {
            CachedAt = DateTime.UtcNow,
            Expiry = TimeSpan.FromHours(1)
        };

        Assert.False(result.IsExpired);
    }

    [Fact]
    public void IsExpired_PastExpiry_ReturnsTrue()
    {
        var result = new CacheResult
        {
            CachedAt = DateTime.UtcNow.AddHours(-2),
            Expiry = TimeSpan.FromHours(1)
        };

        Assert.True(result.IsExpired);
    }
}

#endregion

#region CacheMetadata Tests

public class CacheMetadataTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var metadata = new CacheMetadata();

        Assert.Equal(string.Empty, metadata.CacheKey);
        Assert.Equal(0, metadata.UsageCount);
        Assert.Equal("FluxIndex", metadata.Source);
        Assert.Empty(metadata.Tags);
        Assert.Empty(metadata.AdditionalData);
        Assert.Equal(0, metadata.EmbeddingDimension);
    }

    [Fact]
    public void UpdateUsageStats_IncrementsCount()
    {
        var metadata = new CacheMetadata();

        metadata.UpdateUsageStats();

        Assert.Equal(1, metadata.UsageCount);
    }

    [Fact]
    public void UpdateUsageStats_CalledMultipleTimes_IncrementsCumulatively()
    {
        var metadata = new CacheMetadata();

        metadata.UpdateUsageStats();
        metadata.UpdateUsageStats();
        metadata.UpdateUsageStats();

        Assert.Equal(3, metadata.UsageCount);
    }

    [Fact]
    public void UpdateUsageStats_UpdatesLastUsedAt()
    {
        var metadata = new CacheMetadata();
        var before = DateTime.UtcNow;

        metadata.UpdateUsageStats();

        Assert.True(metadata.LastUsedAt >= before);
        Assert.True(metadata.LastUsedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var metadata = new CacheMetadata
        {
            CacheKey = "cache:key:123",
            UsageCount = 5,
            Source = "CustomSource",
            Tags = ["rag", "search"],
            AdditionalData = new Dictionary<string, object> { ["version"] = 2 },
            EmbeddingDimension = 1536
        };

        Assert.Equal("cache:key:123", metadata.CacheKey);
        Assert.Equal(5, metadata.UsageCount);
        Assert.Equal("CustomSource", metadata.Source);
        Assert.Equal(2, metadata.Tags.Count);
        Assert.Equal(1536, metadata.EmbeddingDimension);
    }
}

#endregion

#region CacheStatistics Tests

public class CacheStatisticsTests
{
    [Fact]
    public void HitRate_ZeroQueries_ReturnsZero()
    {
        var stats = new CacheStatistics { TotalQueries = 0, CacheHits = 0 };

        Assert.Equal(0f, stats.HitRate);
    }

    [Fact]
    public void HitRate_AllHits_ReturnsOne()
    {
        var stats = new CacheStatistics { TotalQueries = 100, CacheHits = 100 };

        Assert.Equal(1.0f, stats.HitRate);
    }

    [Fact]
    public void HitRate_HalfHits_ReturnsHalf()
    {
        var stats = new CacheStatistics { TotalQueries = 100, CacheHits = 50 };

        Assert.Equal(0.5f, stats.HitRate);
    }

    [Fact]
    public void PerformanceGain_ZeroResponseTime_ReturnsZero()
    {
        var stats = new CacheStatistics
        {
            AverageResponseTime = TimeSpan.Zero,
            CacheResponseTime = TimeSpan.FromMilliseconds(5)
        };

        Assert.Equal(0f, stats.PerformanceGain);
    }

    [Fact]
    public void PerformanceGain_CacheIsFaster_ReturnsPositive()
    {
        var stats = new CacheStatistics
        {
            AverageResponseTime = TimeSpan.FromMilliseconds(100),
            CacheResponseTime = TimeSpan.FromMilliseconds(10)
        };

        // 1 - 10/100 = 0.9
        Assert.Equal(0.9f, stats.PerformanceGain, 0.01f);
    }

    [Fact]
    public void PerformanceGain_SameSpeed_ReturnsZero()
    {
        var stats = new CacheStatistics
        {
            AverageResponseTime = TimeSpan.FromMilliseconds(50),
            CacheResponseTime = TimeSpan.FromMilliseconds(50)
        };

        Assert.Equal(0f, stats.PerformanceGain, 0.001f);
    }

    [Fact]
    public void GeneratePerformanceReport_ReturnsNonEmpty()
    {
        var stats = new CacheStatistics
        {
            TotalQueries = 1000,
            CacheHits = 800,
            CacheMisses = 200,
            AverageResponseTime = TimeSpan.FromMilliseconds(100),
            CacheResponseTime = TimeSpan.FromMilliseconds(5),
            CachedItemsCount = 500,
            MemoryUsageBytes = 1024 * 1024 * 50,
            AverageSimilarityScore = 0.95f,
            CollectionPeriod = TimeSpan.FromHours(24)
        };

        var report = stats.GeneratePerformanceReport();

        Assert.False(string.IsNullOrEmpty(report));
        Assert.Contains("1,000", report);
        Assert.Contains("800", report);
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var stats = new CacheStatistics();

        Assert.Equal(0, stats.TotalQueries);
        Assert.Equal(0, stats.CacheHits);
        Assert.Equal(0, stats.CacheMisses);
        Assert.Equal(0, stats.CachedItemsCount);
        Assert.Equal(0, stats.MemoryUsageBytes);
        Assert.Equal(0f, stats.AverageSimilarityScore);
        Assert.Empty(stats.AdditionalMetrics);
        Assert.Empty(stats.HitsByType);
    }
}

#endregion

#region CacheOptions Tests

public class CacheOptionsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var options = CacheOptions.Default;

        Assert.Equal(0.95f, options.DefaultSimilarityThreshold);
        Assert.Equal(TimeSpan.FromHours(24), options.DefaultExpiry);
        Assert.Equal(10000, options.MaxCacheSize);
        Assert.True(options.EnableStatistics);
        Assert.True(options.EnableCompression);
        Assert.True(options.EnableWarmup);
        Assert.True(options.EnableAutoOptimization);
        Assert.Equal(10, options.BatchSize);
    }

    [Fact]
    public void Development_HasReducedSettings()
    {
        var options = CacheOptions.Development;

        Assert.Equal(TimeSpan.FromMinutes(30), options.DefaultExpiry);
        Assert.Equal(1000, options.MaxCacheSize);
        Assert.True(options.EnableStatistics);
        Assert.False(options.EnableWarmup);
    }

    [Fact]
    public void Production_HasScaledSettings()
    {
        var options = CacheOptions.Production;

        Assert.Equal(TimeSpan.FromHours(24), options.DefaultExpiry);
        Assert.Equal(50000, options.MaxCacheSize);
        Assert.True(options.EnableStatistics);
        Assert.True(options.EnableCompression);
        Assert.True(options.EnableAutoOptimization);
    }

    [Fact]
    public void DefaultCacheKeyPrefix_IsFluxIndex()
    {
        var options = new CacheOptions();

        Assert.Equal("fluxindex:cache:", options.CacheKeyPrefix);
    }

    [Fact]
    public void DefaultMaxMemoryUsageBytes_IsOneGigabyte()
    {
        var options = new CacheOptions();

        Assert.Equal(1024L * 1024 * 1024, options.MaxMemoryUsageBytes);
    }
}

#endregion

#region CacheHitType Tests

public class CacheHitTypeTests
{
    [Fact]
    public void HasFourValues()
    {
        var values = Enum.GetValues<CacheHitType>();

        Assert.Equal(4, values.Length);
        Assert.Contains(CacheHitType.Exact, values);
        Assert.Contains(CacheHitType.Semantic, values);
        Assert.Contains(CacheHitType.Keyword, values);
        Assert.Contains(CacheHitType.Pattern, values);
    }
}

#endregion
