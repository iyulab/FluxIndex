using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Xunit;

namespace FluxIndex.Core.Tests.Utilities;

public class SearchResultProcessorTests
{
    #region FilterAndSort Tests

    [Fact]
    public void FilterAndSort_FiltersAndSortsByScore()
    {
        // Arrange
        var results = new List<VectorSearchResult>
        {
            CreateResult("chunk-1", 0.9f),
            CreateResult("chunk-2", 0.5f),
            CreateResult("chunk-3", 0.8f),
            CreateResult("chunk-4", 0.3f)
        };

        // Act
        var filtered = SearchResultProcessor.FilterAndSort(results, minScore: 0.5f, topK: 10).ToList();

        // Assert
        Assert.Equal(3, filtered.Count);
        Assert.Equal("chunk-1", filtered[0].Id);
        Assert.Equal("chunk-3", filtered[1].Id);
        Assert.Equal("chunk-2", filtered[2].Id);
    }

    [Fact]
    public void FilterAndSort_RespectsTopK()
    {
        // Arrange
        var results = Enumerable.Range(1, 10)
            .Select(i => CreateResult($"chunk-{i}", 0.9f - (i * 0.01f)))
            .ToList();

        // Act
        var filtered = SearchResultProcessor.FilterAndSort(results, minScore: 0f, topK: 3).ToList();

        // Assert
        Assert.Equal(3, filtered.Count);
    }

    [Fact]
    public void FilterAndSort_EmptyResults_ReturnsEmpty()
    {
        // Act
        var filtered = SearchResultProcessor.FilterAndSort([], minScore: 0f, topK: 10).ToList();

        // Assert
        Assert.Empty(filtered);
    }

    #endregion

    #region FilterAndSortWithScores Tests

    [Fact]
    public void FilterAndSortWithScores_ReturnsResultsWithScores()
    {
        // Arrange
        var results = new List<VectorSearchResult>
        {
            CreateResult("chunk-1", 0.9f),
            CreateResult("chunk-2", 0.7f)
        };

        // Act
        var filtered = SearchResultProcessor.FilterAndSortWithScores(results, minScore: 0f, topK: 10).ToList();

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.Equal(0.9f, filtered[0].Score);
        Assert.Equal(0.7f, filtered[1].Score);
    }

    #endregion

    #region ApplyRRF Tests

    [Fact]
    public void ApplyRRF_MergesResultSets()
    {
        // Arrange
        var set1 = new List<VectorSearchResult>
        {
            CreateResult("chunk-a", 0.9f),
            CreateResult("chunk-b", 0.8f)
        };
        var set2 = new List<VectorSearchResult>
        {
            CreateResult("chunk-a", 0.85f),
            CreateResult("chunk-c", 0.7f)
        };

        // Act
        var merged = SearchResultProcessor.ApplyRRF(new[] { set1, set2 }, topK: 10).ToList();

        // Assert
        Assert.Equal(3, merged.Count);
        // chunk-a should be first (appears in both sets)
        Assert.Equal("chunk-a", merged[0].Chunk.Id);
    }

    [Fact]
    public void ApplyRRF_RespectsTopK()
    {
        // Arrange
        var results = Enumerable.Range(1, 10)
            .Select(i => new List<VectorSearchResult> { CreateResult($"chunk-{i}", 0.9f) })
            .ToList();

        // Act
        var merged = SearchResultProcessor.ApplyRRF(results, topK: 3).ToList();

        // Assert
        Assert.Equal(3, merged.Count);
    }

    #endregion

    #region ShouldTrimCandidates Tests

    [Fact]
    public void ShouldTrimCandidates_ReturnsTrue_WhenExceedsMultiplier()
    {
        // Act
        var shouldTrim = SearchResultProcessor.ShouldTrimCandidates(currentCount: 25, targetTopK: 10);

        // Assert
        Assert.True(shouldTrim);
    }

    [Fact]
    public void ShouldTrimCandidates_ReturnsFalse_WhenBelowThreshold()
    {
        // Act
        var shouldTrim = SearchResultProcessor.ShouldTrimCandidates(currentCount: 15, targetTopK: 10);

        // Assert
        Assert.False(shouldTrim);
    }

    #endregion

    #region TrimCandidates Tests

    [Fact]
    public void TrimCandidates_TrimsToTargetSize()
    {
        // Arrange
        var candidates = Enumerable.Range(1, 20)
            .Select(i => CreateResult($"chunk-{i}", 1.0f - (i * 0.01f)))
            .ToList();

        // Act
        var trimmed = SearchResultProcessor.TrimCandidates(candidates, targetTopK: 5);

        // Assert
        Assert.Equal(5, trimmed.Count);
        // Should keep highest scores
        Assert.Equal(0.99f, trimmed[0].Score);
    }

    #endregion

    #region GetDynamicThreshold Tests

    [Fact]
    public void GetDynamicThreshold_ReturnsLowestScoreInTopK()
    {
        // Arrange
        var candidates = new List<VectorSearchResult>
        {
            CreateResult("a", 0.9f),
            CreateResult("b", 0.8f),
            CreateResult("c", 0.7f),
            CreateResult("d", 0.6f)
        };

        // Act
        var threshold = SearchResultProcessor.GetDynamicThreshold(candidates, targetTopK: 3, fallbackMinScore: 0.5f);

        // Assert
        Assert.Equal(0.7f, threshold);
    }

    [Fact]
    public void GetDynamicThreshold_ReturnsFallback_WhenNotEnoughCandidates()
    {
        // Arrange
        var candidates = new List<VectorSearchResult>
        {
            CreateResult("a", 0.9f)
        };

        // Act
        var threshold = SearchResultProcessor.GetDynamicThreshold(candidates, targetTopK: 5, fallbackMinScore: 0.3f);

        // Assert
        Assert.Equal(0.3f, threshold);
    }

    #endregion

    #region DeduplicateByDocument Tests

    [Fact]
    public void DeduplicateByDocument_KeepsHighestScorePerDocument()
    {
        // Arrange
        var results = new List<VectorSearchResult>
        {
            CreateResultWithDocId("chunk-1", "doc-a", 0.9f),
            CreateResultWithDocId("chunk-2", "doc-a", 0.8f),
            CreateResultWithDocId("chunk-3", "doc-b", 0.85f)
        };

        // Act
        var deduplicated = SearchResultProcessor.DeduplicateByDocument(results).ToList();

        // Assert
        Assert.Equal(2, deduplicated.Count);
        Assert.Contains(deduplicated, r => r.Chunk.Id == "chunk-1"); // doc-a's highest
        Assert.Contains(deduplicated, r => r.Chunk.Id == "chunk-3"); // doc-b
    }

    #endregion

    #region SetChunkScores Tests

    [Fact]
    public void SetChunkScores_SetsScoreOnChunks()
    {
        // Arrange
        var results = new List<VectorSearchResult>
        {
            CreateResult("chunk-1", 0.9f),
            CreateResult("chunk-2", 0.7f)
        };

        // Act
        SearchResultProcessor.SetChunkScores(results);

        // Assert
        Assert.Equal(0.9f, results[0].Chunk.Score);
        Assert.Equal(0.7f, results[1].Chunk.Score);
    }

    #endregion

    #region Helper Methods

    private static VectorSearchResult CreateResult(string id, float score)
    {
        return new VectorSearchResult(
            new DocumentChunk { Id = id, DocumentId = "doc", Content = "content" },
            score);
    }

    private static VectorSearchResult CreateResultWithDocId(string id, string documentId, float score)
    {
        return new VectorSearchResult(
            new DocumentChunk { Id = id, DocumentId = documentId, Content = "content" },
            score);
    }

    #endregion
}
