using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxIndex.Core.Tests;

public class RankFusionServiceTests
{
    private readonly ILogger<RankFusionService> _logger;
    private readonly RankFusionService _service;

    public RankFusionServiceTests()
    {
        _logger = NullLogger<RankFusionService>.Instance;
        _service = new RankFusionService(_logger);
    }

    [Fact]
    public void FuseWithRRF_EmptyResultSets_ReturnsEmpty()
    {
        // Arrange
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>();

        // Act
        var results = _service.FuseWithRRF(resultSets);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void FuseWithRRF_NullResultSets_ReturnsEmpty()
    {
        // Act
        var results = _service.FuseWithRRF(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void FuseWithRRF_SingleResultSet_ReturnsRankedResults()
    {
        // Arrange
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["vector_search"] = new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.9 },
                new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.8 },
                new RankedResult { DocumentId = "doc3", ChunkId = "chunk3", Score = 0.7 }
            }
        };

        // Act
        var results = _service.FuseWithRRF(resultSets, k: 60, topN: 10).ToList();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].Rank); // Highest RRF score should be rank 1
        Assert.True(results[0].Score > 0);
    }

    [Fact]
    public void FuseWithRRF_MultipleResultSets_CombinesCorrectly()
    {
        // Arrange
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["vector_search"] = new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.9 },
                new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.8 }
            },
            ["keyword_search"] = new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.85 }, // Same doc appears again
                new RankedResult { DocumentId = "doc3", ChunkId = "chunk3", Score = 0.75 }
            }
        };

        // Act
        var results = _service.FuseWithRRF(resultSets, k: 60, topN: 10).ToList();

        // Assert
        Assert.Equal(3, results.Count);

        // doc1 appears in both result sets, should have highest combined RRF score
        var doc1Result = results.FirstOrDefault(r => r.DocumentId == "doc1");
        Assert.NotNull(doc1Result);
        Assert.Equal(1, doc1Result.Rank); // Should be ranked first
    }

    [Fact]
    public void FuseWithRRF_TopNLimit_ReturnsCorrectCount()
    {
        // Arrange
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["search1"] = Enumerable.Range(1, 20)
                .Select(i => new RankedResult
                {
                    DocumentId = $"doc{i}",
                    ChunkId = $"chunk{i}",
                    Score = 1.0 / i
                })
        };

        var topN = 5;

        // Act
        var results = _service.FuseWithRRF(resultSets, topN: topN).ToList();

        // Assert
        Assert.Equal(topN, results.Count);
    }

    [Theory]
    [InlineData(60)]   // Default k value
    [InlineData(1)]    // Minimum practical k
    [InlineData(100)]  // Higher k value
    public void FuseWithRRF_DifferentKValues_CalculatesCorrectly(int k)
    {
        // Arrange
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["search"] = new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.9 },
                new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.8 }
            }
        };

        // Act
        var results = _service.FuseWithRRF(resultSets, k: k).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Score > 0));
    }

    [Fact]
    public void FuseWithWeights_EmptyResultSets_ReturnsEmpty()
    {
        // Arrange
        var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, double weight)>();

        // Act
        var results = _service.FuseWithWeights(resultSets);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void FuseWithWeights_NullResultSets_ReturnsEmpty()
    {
        // Act
        var results = _service.FuseWithWeights(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void FuseWithWeights_InvalidWeights_ThrowsException()
    {
        // Arrange
        var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, double weight)>
        {
            ["search1"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.9 }
            }, 0.0),
            ["search2"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.8 }
            }, 0.0)
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.FuseWithWeights(resultSets));
    }

    [Fact]
    public void FuseWithWeights_EqualWeights_CombinesCorrectly()
    {
        // Arrange
        var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, double weight)>
        {
            ["vector_search"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.9 },
                new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.7 }
            }, 1.0),
            ["keyword_search"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.8 },
                new RankedResult { DocumentId = "doc3", ChunkId = "chunk3", Score = 0.6 }
            }, 1.0)
        };

        // Act
        var results = _service.FuseWithWeights(resultSets, topN: 10).ToList();

        // Assert
        Assert.Equal(3, results.Count);

        // doc1 appears in both, should have combined weighted score
        var doc1Result = results.FirstOrDefault(r => r.DocumentId == "doc1");
        Assert.NotNull(doc1Result);
    }

    [Fact]
    public void FuseWithWeights_DifferentWeights_PrioritizesHigherWeight()
    {
        // Arrange
        var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, double weight)>
        {
            ["high_priority"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.6 }
            }, 3.0), // 3x weight
            ["low_priority"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.9 }
            }, 1.0)  // 1x weight
        };

        // Act
        var results = _service.FuseWithWeights(resultSets).ToList();

        // Assert
        Assert.Equal(2, results.Count);

        // doc1 should rank higher due to higher weight, despite lower raw score
        Assert.Equal("doc1", results[0].DocumentId);
    }

    [Fact]
    public void FuseWithWeights_TopNLimit_ReturnsCorrectCount()
    {
        // Arrange
        var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, double weight)>
        {
            ["search"] = (Enumerable.Range(1, 20)
                .Select(i => new RankedResult
                {
                    DocumentId = $"doc{i}",
                    ChunkId = $"chunk{i}",
                    Score = 1.0 / i
                }), 1.0)
        };

        var topN = 7;

        // Act
        var results = _service.FuseWithWeights(resultSets, topN: topN).ToList();

        // Assert
        Assert.Equal(topN, results.Count);
    }

    [Fact]
    public void NormalizeScores_EmptyResults_ReturnsEmpty()
    {
        // Arrange
        var results = new List<RankedResult>();

        // Act
        var normalized = _service.NormalizeScores(results).ToList();

        // Assert
        Assert.Empty(normalized);
    }

    [Fact]
    public void NormalizeScores_ValidResults_NormalizesToZeroOne()
    {
        // Arrange
        var results = new List<RankedResult>
        {
            new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 10.0 },
            new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 5.0 },
            new RankedResult { DocumentId = "doc3", ChunkId = "chunk3", Score = 0.0 }
        };

        // Act
        var normalized = _service.NormalizeScores(results).ToList();

        // Assert
        Assert.Equal(3, normalized.Count);
        Assert.All(normalized, r =>
        {
            Assert.True(r.Score >= 0.0);
            Assert.True(r.Score <= 1.0);
        });

        // Highest score should be 1.0
        Assert.Equal(1.0, normalized.Max(r => r.Score));

        // Lowest score should be 0.0
        Assert.Equal(0.0, normalized.Min(r => r.Score));
    }

    [Fact]
    public void NormalizeScores_UniformScores_ReturnsUniformOnes()
    {
        // Arrange
        var results = new List<RankedResult>
        {
            new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.5 },
            new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.5 },
            new RankedResult { DocumentId = "doc3", ChunkId = "chunk3", Score = 0.5 }
        };

        // Act
        var normalized = _service.NormalizeScores(results).ToList();

        // Assert
        Assert.Equal(3, normalized.Count);
        Assert.All(normalized, r => Assert.Equal(1.0, r.Score));
    }

    [Fact]
    public void FuseWithRRF_AssignsSourceName_Correctly()
    {
        // Arrange
        var sourceName = "test_vector_search";
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            [sourceName] = new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.9 }
            }
        };

        // Act
        var results = _service.FuseWithRRF(resultSets).ToList();

        // Assert
        Assert.Single(results);
        Assert.Equal(sourceName, results[0].Source);
    }

    [Fact]
    public void FuseWithRRF_MergesDuplicates_CombinesSources()
    {
        // Arrange
        var resultSets = new Dictionary<string, IEnumerable<RankedResult>>
        {
            ["source1"] = new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.9 }
            },
            ["source2"] = new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.8 }
            }
        };

        // Act
        var results = _service.FuseWithRRF(resultSets).ToList();

        // Assert
        Assert.Single(results);
        Assert.Contains("source1", results[0].Source);
        Assert.Contains("source2", results[0].Source);
    }

    [Fact]
    public void FuseWithWeights_NormalizesWeights_Correctly()
    {
        // Arrange - weights don't sum to 1
        var resultSets = new Dictionary<string, (IEnumerable<RankedResult> results, double weight)>
        {
            ["search1"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc1", ChunkId = "chunk1", Score = 0.5 }
            }, 2.0),
            ["search2"] = (new List<RankedResult>
            {
                new RankedResult { DocumentId = "doc2", ChunkId = "chunk2", Score = 0.5 }
            }, 8.0)  // Total weight = 10
        };

        // Act
        var results = _service.FuseWithWeights(resultSets).ToList();

        // Assert - Should normalize weights to sum to 1 internally
        Assert.Equal(2, results.Count);

        // doc2 should have higher score due to higher weight (8/10 vs 2/10)
        var doc2Result = results.FirstOrDefault(r => r.DocumentId == "doc2");
        var doc1Result = results.FirstOrDefault(r => r.DocumentId == "doc1");

        Assert.NotNull(doc2Result);
        Assert.NotNull(doc1Result);
        Assert.True(doc2Result.Score > doc1Result.Score);
    }

    [Fact]
    public void Constructor_NullLogger_UsesNullLogger()
    {
        // Act
        var service = new RankFusionService(null);

        // Assert - Should create without throwing
        Assert.NotNull(service);
    }
}
