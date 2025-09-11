using FluentAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

public class RankFusionServiceTests
{
    private readonly RankFusionService _service;
    private readonly Mock<ILogger<RankFusionService>> _loggerMock;

    public RankFusionServiceTests()
    {
        _loggerMock = new Mock<ILogger<RankFusionService>>();
        _service = new RankFusionService(_loggerMock.Object);
    }

    [Fact]
    public void FuseWithRRF_SingleResultSet_ReturnsOriginalResults()
    {
        // Arrange
        var results = CreateRankedResults("doc1", "doc2", "doc3");
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["source1"] = results
        };

        // Act
        var fused = _service.FuseWithRRF(resultSets, k: 60, topN: 3).ToList();

        // Assert
        fused.Should().HaveCount(3);
        fused[0].DocumentId.Should().Be("doc1");
        fused[1].DocumentId.Should().Be("doc2");
        fused[2].DocumentId.Should().Be("doc3");
    }

    [Fact]
    public void FuseWithRRF_MultipleResultSets_FusesCorrectly()
    {
        // Arrange
        var vectorResults = CreateRankedResults("doc1", "doc2", "doc3", "doc4");
        var keywordResults = CreateRankedResults("doc2", "doc3", "doc5", "doc1");
        
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["vector"] = vectorResults,
            ["keyword"] = keywordResults
        };

        // Act
        var fused = _service.FuseWithRRF(resultSets, k: 60, topN: 5).ToList();

        // Assert
        fused.Should().HaveCount(5);
        
        // doc2 and doc3 appear in both result sets at high ranks, should be top
        fused.Take(2).Select(r => r.DocumentId).Should().Contain(new[] { "doc2", "doc3" });
        
        // All documents should have RRF scores
        fused.All(r => r.Score > 0).Should().BeTrue();
        
        // Scores should be in descending order
        fused.Select(r => r.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public void FuseWithRRF_DifferentKValues_AffectsScoring()
    {
        // Arrange
        var results1 = CreateRankedResults("doc1", "doc2");
        var results2 = CreateRankedResults("doc2", "doc1");
        
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["source1"] = results1,
            ["source2"] = results2
        };

        // Act
        var fusedK10 = _service.FuseWithRRF(resultSets, k: 10, topN: 2).ToList();
        var fusedK60 = _service.FuseWithRRF(resultSets, k: 60, topN: 2).ToList();

        // Assert
        // Both docs appear in both sets, so they should have equal final rank
        fusedK10[0].Score.Should().BeApproximately(fusedK10[1].Score, 0.001f);
        fusedK60[0].Score.Should().BeApproximately(fusedK60[1].Score, 0.001f);
        
        // Lower k value should produce higher scores
        fusedK10[0].Score.Should().BeGreaterThan(fusedK60[0].Score);
    }

    [Fact]
    public void FuseWithRRF_EmptyResultSets_ReturnsEmpty()
    {
        // Arrange
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>();

        // Act
        var fused = _service.FuseWithRRF(resultSets, k: 60, topN: 10);

        // Assert
        fused.Should().BeEmpty();
    }

    [Fact]
    public void FuseWithWeights_AppliesWeightsCorrectly()
    {
        // Arrange
        var highScoreResults = CreateRankedResultsWithScores(
            ("doc1", 0.9f), ("doc2", 0.8f));
        var lowScoreResults = CreateRankedResultsWithScores(
            ("doc3", 0.5f), ("doc4", 0.4f));
        
        var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, float weight)>
        {
            ["high"] = (highScoreResults, 0.8f),
            ["low"] = (lowScoreResults, 0.2f)
        };

        // Act
        var fused = _service.FuseWithWeights(resultSets, topN: 4).ToList();

        // Assert
        fused.Should().HaveCount(4);
        
        // High-weighted results should rank higher
        fused[0].DocumentId.Should().Be("doc1");
        fused[1].DocumentId.Should().Be("doc2");
        
        // Scores should be weighted
        fused[0].Score.Should().BeLessThan(0.9f); // Original score was 0.9, weight is 0.8
    }

    [Fact]
    public void NormalizeScores_NormalizesToZeroOne()
    {
        // Arrange
        var results = CreateRankedResultsWithScores(
            ("doc1", 10f), ("doc2", 5f), ("doc3", 0f));

        // Act
        var normalized = _service.NormalizeScores(results).ToList();

        // Assert
        normalized[0].Score.Should().Be(1.0f); // Max score -> 1
        normalized[1].Score.Should().Be(0.5f); // Middle score -> 0.5
        normalized[2].Score.Should().Be(0.0f); // Min score -> 0
    }

    [Fact]
    public void NormalizeScores_UniformScores_ReturnsOnes()
    {
        // Arrange
        var results = CreateRankedResultsWithScores(
            ("doc1", 0.5f), ("doc2", 0.5f), ("doc3", 0.5f));

        // Act
        var normalized = _service.NormalizeScores(results).ToList();

        // Assert
        normalized.All(r => r.Score == 1.0f).Should().BeTrue();
    }

    [Fact]
    public void FuseWithRRF_MergesMetadata()
    {
        // Arrange
        var result1 = new RankedResult
        {
            DocumentId = "doc1",
            ChunkId = "chunk1",
            Content = "content",
            Metadata = new Dictionary<string, object> { ["key1"] = "value1" }
        };
        
        var result2 = new RankedResult
        {
            DocumentId = "doc1",
            ChunkId = "chunk1",
            Content = "content",
            Metadata = new Dictionary<string, object> { ["key2"] = "value2" }
        };

        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["source1"] = new[] { result1 },
            ["source2"] = new[] { result2 }
        };

        // Act
        var fused = _service.FuseWithRRF(resultSets, k: 60, topN: 1).First();

        // Assert
        fused.Metadata.Should().ContainKey("key1");
        fused.Metadata.Should().ContainKey("key2");
        fused.Source.Should().Contain("source1");
        fused.Source.Should().Contain("source2");
    }

    private IEnumerable<RankedResult> CreateRankedResults(params string[] docIds)
    {
        return docIds.Select((id, index) => new RankedResult
        {
            DocumentId = id,
            ChunkId = $"{id}_chunk",
            Content = $"Content for {id}",
            Score = 1.0f - (index * 0.1f),
            Rank = index + 1
        });
    }

    private IEnumerable<RankedResult> CreateRankedResultsWithScores(params (string docId, float score)[] items)
    {
        return items.Select((item, index) => new RankedResult
        {
            DocumentId = item.docId,
            ChunkId = $"{item.docId}_chunk",
            Content = $"Content for {item.docId}",
            Score = item.score,
            Rank = index + 1
        });
    }
}