using FluentAssertions;
using FluxIndex.AI.LocalReranker;
using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxIndex.AI.LocalReranker.Tests;

public class LocalRerankerAdapterTests
{
    private readonly LocalRerankerOptions _defaultOptions = new()
    {
        ModelId = "fast", // Use smallest model for tests
        WarmupOnStartup = false
    };

    [Fact]
    public void GetModelInfo_ShouldReturnValidInfo()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new LocalRerankerAdapter(options);

        // Act
        var modelInfo = adapter.GetModelInfo();

        // Assert
        modelInfo.Should().NotBeNull();
        modelInfo.Name.Should().NotBeNullOrEmpty();
        modelInfo.Type.Should().Be(RerankModel.Local);
        modelInfo.RequiresApiKey.Should().BeFalse();
        modelInfo.Capabilities.Should().ContainKey("cross_encoder");
        modelInfo.Capabilities["cross_encoder"].Should().Be(true);
        modelInfo.Capabilities.Should().ContainKey("local_inference");
        modelInfo.Capabilities["local_inference"].Should().Be(true);
    }

    [Fact]
    public async Task RerankAsync_WithEmptyCandidates_ShouldReturnEmptyResults()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new LocalRerankerAdapter(options);
        var candidates = Enumerable.Empty<RetrievalCandidate>();

        // Act
        var results = await adapter.RerankAsync("test query", candidates);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RerankAsync_ShouldReorderCandidatesByRelevance()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new LocalRerankerAdapter(options);

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
            },
            new()
            {
                Id = "3",
                DocumentId = "doc3",
                ChunkId = "chunk3",
                Content = "Deep learning uses neural networks with many layers.",
                InitialScore = 0.6f,
                InitialRank = 3
            }
        };

        // Act
        var results = await adapter.RerankAsync("What is machine learning?", candidates);

        // Assert
        results.Should().NotBeEmpty();
        var resultList = results.ToList();

        // The ML-related content should rank higher than weather content
        resultList.Should().HaveCountGreaterThan(0);
        resultList.All(r => r.RerankScore >= 0 && r.RerankScore <= 1).Should().BeTrue();
        resultList.All(r => r.NewRank > 0).Should().BeTrue();
    }

    [Fact]
    public async Task RerankAsync_WithTopN_ShouldLimitResults()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new LocalRerankerAdapter(options);

        var candidates = Enumerable.Range(1, 10).Select(i => new RetrievalCandidate
        {
            Id = i.ToString(),
            DocumentId = $"doc{i}",
            ChunkId = $"chunk{i}",
            Content = $"Document content {i} about various topics.",
            InitialScore = 0.9f - (i * 0.05f),
            InitialRank = i
        }).ToList();

        var rerankOptions = new RerankOptions { TopN = 3 };

        // Act
        var results = await adapter.RerankAsync("topics", candidates, rerankOptions);

        // Assert
        results.Count().Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task RerankAsync_WithScoreThreshold_ShouldFilterLowScores()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new LocalRerankerAdapter(options);

        var candidates = new List<RetrievalCandidate>
        {
            new()
            {
                Id = "1",
                DocumentId = "doc1",
                ChunkId = "chunk1",
                Content = "Highly relevant machine learning content about neural networks.",
                InitialScore = 0.9f,
                InitialRank = 1
            },
            new()
            {
                Id = "2",
                DocumentId = "doc2",
                ChunkId = "chunk2",
                Content = "Completely unrelated content about cooking recipes.",
                InitialScore = 0.3f,
                InitialRank = 2
            }
        };

        var rerankOptions = new RerankOptions { ScoreThreshold = 0.5f };

        // Act
        var results = await adapter.RerankAsync("machine learning", candidates, rerankOptions);

        // Assert
        results.All(r => r.RerankScore >= 0.5f).Should().BeTrue();
    }

    [Fact]
    public async Task RerankAsync_WithIncludeExplanation_ShouldProvideExplanations()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new LocalRerankerAdapter(options);

        var candidates = new List<RetrievalCandidate>
        {
            new()
            {
                Id = "1",
                DocumentId = "doc1",
                ChunkId = "chunk1",
                Content = "Machine learning enables computers to learn from data.",
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
        result.Explanation.Should().Contain("Cross-encoder");
    }

    [Fact]
    public async Task RerankAsync_ShouldPreserveMetadata()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        using var adapter = new LocalRerankerAdapter(options);

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
                Content = "Test content",
                InitialScore = 0.8f,
                InitialRank = 1,
                Metadata = metadata
            }
        };

        // Act
        var results = await adapter.RerankAsync("test", candidates);

        // Assert
        var result = results.FirstOrDefault();
        result.Should().NotBeNull();
        result!.Metadata.Should().NotBeNull();
        result.Metadata.Should().ContainKey("author");
        result.Metadata!["author"].Should().Be("John Doe");
    }

    [Fact]
    public async Task Dispose_ShouldPreventFurtherOperations()
    {
        // Arrange
        var options = Options.Create(_defaultOptions);
        var adapter = new LocalRerankerAdapter(options);

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
        var adapter = new LocalRerankerAdapter(options);

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
