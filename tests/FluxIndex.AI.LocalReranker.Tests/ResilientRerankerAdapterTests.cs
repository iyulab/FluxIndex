using FluentAssertions;
using FluxIndex.AI.LocalReranker;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.AI.LocalReranker.Tests;

public class ResilientRerankerAdapterTests
{
    private readonly LocalRerankerOptions _defaultOptions = new()
    {
        ModelId = "fast",
        WarmupOnStartup = false
    };

    [Fact]
    public void Constructor_ShouldInitializeWithFallback_WhenModelUnavailable()
    {
        // Arrange - Use invalid model to trigger fallback
        var options = Options.Create(new LocalRerankerOptions
        {
            ModelId = "invalid-model-that-does-not-exist",
            WarmupOnStartup = true // Force warmup to trigger failure
        });

        // Act
        using var adapter = new ResilientRerankerAdapter(options);

        // Assert - Should not throw, should fall back to algorithmic
        adapter.Should().NotBeNull();
        adapter.CurrentMethod.Should().Be(RerankMethod.Algorithmic);
        adapter.IsSemanticAvailable.Should().BeFalse();
    }

    [Fact]
    public void GetModelInfo_ShouldReturnValidInfo_WhenFallbackMode()
    {
        // Arrange
        var options = Options.Create(new LocalRerankerOptions
        {
            ModelId = "invalid-model",
            WarmupOnStartup = true
        });
        using var adapter = new ResilientRerankerAdapter(options);

        // Act
        var modelInfo = adapter.GetModelInfo();

        // Assert
        modelInfo.Should().NotBeNull();
        modelInfo.Name.Should().Contain("Algorithmic");
        modelInfo.RequiresApiKey.Should().BeFalse();
        modelInfo.Capabilities.Should().ContainKey("current_method");
        modelInfo.Capabilities["current_method"].Should().Be("Algorithmic");
        modelInfo.Capabilities.Should().ContainKey("has_fallback");
        modelInfo.Capabilities["has_fallback"].Should().Be(true);
    }

    [Fact]
    public async Task RerankAsync_WithEmptyCandidates_ShouldReturnEmpty()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new ResilientRerankerAdapter(options);
        var candidates = Enumerable.Empty<RetrievalCandidate>();

        // Act
        var results = await adapter.RerankAsync("test query", candidates);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RerankAsync_ShouldReorderCandidates_InFallbackMode()
    {
        // Arrange - Force fallback mode
        var options = Options.Create(new LocalRerankerOptions
        {
            ModelId = "invalid-model",
            WarmupOnStartup = true
        });
        using var adapter = new ResilientRerankerAdapter(options);

        var candidates = new List<RetrievalCandidate>
        {
            new()
            {
                Id = "1",
                DocumentId = "doc1",
                ChunkId = "chunk1",
                Content = "The weather today is sunny and warm.",
                InitialScore = 0.8f,
                InitialRank = 1
            },
            new()
            {
                Id = "2",
                DocumentId = "doc2",
                ChunkId = "chunk2",
                Content = "Machine learning is a subset of artificial intelligence.",
                InitialScore = 0.7f,
                InitialRank = 2
            }
        };

        // Act
        var results = await adapter.RerankAsync("What is machine learning?", candidates);

        // Assert
        results.Should().NotBeEmpty();
        var resultList = results.ToList();
        resultList.All(r => r.RerankScore >= 0 && r.RerankScore <= 1).Should().BeTrue();
        resultList.All(r => r.NewRank > 0).Should().BeTrue();
    }

    [Fact]
    public async Task RerankAsync_ShouldPreserveMetadata_InFallbackMode()
    {
        // Arrange
        var options = Options.Create(new LocalRerankerOptions
        {
            ModelId = "invalid-model",
            WarmupOnStartup = true
        });
        using var adapter = new ResilientRerankerAdapter(options);

        var metadata = new Dictionary<string, object>
        {
            ["author"] = "John Doe",
            ["category"] = "Technology"
        };

        var candidates = new List<RetrievalCandidate>
        {
            new()
            {
                Id = "1",
                DocumentId = "doc1",
                ChunkId = "chunk1",
                Content = "Test content about technology",
                InitialScore = 0.8f,
                InitialRank = 1,
                Metadata = metadata
            }
        };

        // Act
        var results = await adapter.RerankAsync("technology", candidates);

        // Assert
        var result = results.FirstOrDefault();
        result.Should().NotBeNull();
        result!.Metadata.Should().NotBeNull();
        result.Metadata.Should().ContainKey("author");
        result.Metadata!["author"].Should().Be("John Doe");
    }

    [Fact]
    public async Task RerankAsync_WithIncludeExplanation_ShouldIndicateFallback()
    {
        // Arrange
        var options = Options.Create(new LocalRerankerOptions
        {
            ModelId = "invalid-model",
            WarmupOnStartup = true
        });
        using var adapter = new ResilientRerankerAdapter(options);

        var candidates = new List<RetrievalCandidate>
        {
            new()
            {
                Id = "1",
                DocumentId = "doc1",
                ChunkId = "chunk1",
                Content = "Machine learning enables computers to learn.",
                InitialScore = 0.7f,
                InitialRank = 1
            }
        };

        var rerankOptions = new RerankOptions { IncludeExplanation = true };

        // Act
        var results = await adapter.RerankAsync("machine learning", candidates, rerankOptions);

        // Assert
        var result = results.FirstOrDefault();
        result.Should().NotBeNull();
        result!.Explanation.Should().NotBeNullOrEmpty();
        result.Explanation.Should().Contain("Fallback");
    }

    [Fact]
    public void AddResilientLocalReranker_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddResilientLocalReranker(options =>
        {
            options.ModelId = "invalid-model";
            options.WarmupOnStartup = true;
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var reranker = serviceProvider.GetService<IReranker>();
        reranker.Should().NotBeNull();
        reranker.Should().BeOfType<ResilientRerankerAdapter>();
    }

    [Fact]
    public void AddResilientLocalReranker_ShouldRegisterAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddResilientLocalReranker(options =>
        {
            options.ModelId = "invalid-model";
            options.WarmupOnStartup = true;
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var reranker1 = scope1.ServiceProvider.GetService<IReranker>();
        var reranker2 = scope2.ServiceProvider.GetService<IReranker>();

        reranker1.Should().BeSameAs(reranker2);
    }

    [Fact]
    public async Task Dispose_ShouldPreventFurtherOperations()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        var adapter = new ResilientRerankerAdapter(options);

        // Act
        adapter.Dispose();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await adapter.RerankAsync("test", new List<RetrievalCandidate>
            {
                new() { Id = "1", Content = "test" }
            });
        });
    }

    [Fact]
    public async Task DisposeAsync_ShouldPreventFurtherOperations()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        var adapter = new ResilientRerankerAdapter(options);

        // Act
        await adapter.DisposeAsync();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await adapter.RerankAsync("test", new List<RetrievalCandidate>
            {
                new() { Id = "1", Content = "test" }
            });
        });
    }
}
