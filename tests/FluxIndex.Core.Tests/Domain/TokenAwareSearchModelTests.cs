using FluxIndex.Core.Application.Models;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

#region QueryAnalysis Tests

public class QueryAnalysisTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var analysis = new QueryAnalysis();

        Assert.Equal(string.Empty, analysis.OriginalQuery);
        Assert.Equal(string.Empty, analysis.NormalizedQuery);
        Assert.Equal(0, analysis.TokenCount);
        Assert.Equal("en", analysis.Language);
        Assert.Equal(0.0, analysis.LanguageConfidence);
        Assert.Equal(QueryIntent.Informational, analysis.Intent);
        Assert.Equal(QueryComplexityLevel.Simple, analysis.Complexity);
        Assert.Empty(analysis.Keywords);
        Assert.Empty(analysis.Entities);
        Assert.Equal(10, analysis.RecommendedTopK);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var analysis = new QueryAnalysis
        {
            OriginalQuery = "How to implement RAG with FluxIndex?",
            NormalizedQuery = "implement rag fluxindex",
            TokenCount = 8,
            Language = "en",
            LanguageConfidence = 0.99,
            Intent = QueryIntent.HowTo,
            Complexity = QueryComplexityLevel.Moderate,
            Keywords = ["implement", "rag", "fluxindex"],
            Entities =
            [
                new QueryEntity { Text = "RAG", Type = EntityType.Concept, Confidence = 0.95 },
                new QueryEntity { Text = "FluxIndex", Type = EntityType.Technology, Confidence = 0.98 }
            ],
            RecommendedStrategy = SearchStrategy.Hybrid,
            RecommendedTopK = 20
        };

        Assert.Equal(8, analysis.TokenCount);
        Assert.Equal(QueryIntent.HowTo, analysis.Intent);
        Assert.Equal(3, analysis.Keywords.Count);
        Assert.Equal(2, analysis.Entities.Count);
        Assert.Equal(SearchStrategy.Hybrid, analysis.RecommendedStrategy);
    }
}

#endregion

#region QueryEntity Tests

public class QueryEntityTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var entity = new QueryEntity();

        Assert.Equal(string.Empty, entity.Text);
        Assert.Equal(EntityType.Technology, entity.Type);
        Assert.Equal(0.0, entity.Confidence);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var entity = new QueryEntity
        {
            Text = "FluxIndex",
            Type = EntityType.Technology,
            Confidence = 0.95
        };

        Assert.Equal("FluxIndex", entity.Text);
        Assert.Equal(EntityType.Technology, entity.Type);
        Assert.Equal(0.95, entity.Confidence);
    }
}

#endregion

#region TokenAwareSearchRequest Tests

public class TokenAwareSearchRequestTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var request = new TokenAwareSearchRequest();

        Assert.Equal(string.Empty, request.Query);
        Assert.Equal(4000, request.MaxTokens);
        Assert.Equal(0.5, request.MinScore);
        Assert.Null(request.Filters);
        Assert.Null(request.ForceStrategy);
        Assert.Equal(0.2, request.DiversityWeight);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var request = new TokenAwareSearchRequest
        {
            Query = "What is machine learning?",
            MaxTokens = 8000,
            MinScore = 0.7,
            Filters = new Dictionary<string, object> { ["category"] = "tech" },
            ForceStrategy = SearchStrategy.Vector,
            DiversityWeight = 0.5
        };

        Assert.Equal("What is machine learning?", request.Query);
        Assert.Equal(8000, request.MaxTokens);
        Assert.NotNull(request.Filters);
        Assert.Equal(SearchStrategy.Vector, request.ForceStrategy);
    }
}

#endregion

#region TokenAwareSearchResult Tests

public class TokenAwareSearchResultTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new TokenAwareSearchResult();

        Assert.Equal(string.Empty, result.Query);
        Assert.Equal(0, result.RequestedTokens);
        Assert.Equal(0, result.UsedTokens);
        Assert.Empty(result.Chunks);
        Assert.Equal(0, result.TotalRetrieved);
        Assert.Equal(0, result.TruncatedCount);
        Assert.NotNull(result.Analysis);
        Assert.Equal(0L, result.ElapsedMilliseconds);
    }

    [Theory]
    [InlineData(4000, 2500, 1500)]
    [InlineData(4000, 4000, 0)]
    [InlineData(4000, 0, 4000)]
    [InlineData(8000, 6000, 2000)]
    public void RemainingBudget_CalculatesCorrectly(int requested, int used, int expected)
    {
        var result = new TokenAwareSearchResult
        {
            RequestedTokens = requested,
            UsedTokens = used
        };

        Assert.Equal(expected, result.RemainingBudget);
    }

    [Fact]
    public void RemainingBudget_CanBeNegative()
    {
        var result = new TokenAwareSearchResult
        {
            RequestedTokens = 1000,
            UsedTokens = 1500
        };

        Assert.Equal(-500, result.RemainingBudget);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var result = new TokenAwareSearchResult
        {
            Query = "What is ML?",
            RequestedTokens = 4000,
            UsedTokens = 3200,
            Chunks =
            [
                new SelectedChunk
                {
                    ChunkId = "c1",
                    DocumentId = "d1",
                    Content = "Machine learning...",
                    Score = 0.95,
                    TokenCount = 1600,
                    CumulativeTokens = 1600,
                    ChunkIndex = 0
                },
                new SelectedChunk
                {
                    ChunkId = "c2",
                    DocumentId = "d1",
                    Content = "Deep learning...",
                    Score = 0.88,
                    TokenCount = 1600,
                    CumulativeTokens = 3200,
                    ChunkIndex = 1
                }
            ],
            TotalRetrieved = 15,
            TruncatedCount = 13,
            Strategy = SearchStrategy.Hybrid,
            ElapsedMilliseconds = 120
        };

        Assert.Equal(800, result.RemainingBudget);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal(15, result.TotalRetrieved);
    }
}

#endregion

#region SelectedChunk Tests

public class SelectedChunkTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var chunk = new SelectedChunk();

        Assert.Equal(string.Empty, chunk.ChunkId);
        Assert.Equal(string.Empty, chunk.DocumentId);
        Assert.Equal(string.Empty, chunk.Content);
        Assert.Equal(0.0, chunk.Score);
        Assert.Equal(0, chunk.TokenCount);
        Assert.Equal(0, chunk.CumulativeTokens);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Empty(chunk.Metadata);
        Assert.Empty(chunk.Highlights);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var chunk = new SelectedChunk
        {
            ChunkId = "chunk-42",
            DocumentId = "doc-7",
            Content = "Machine learning is a branch of AI.",
            Score = 0.93,
            TokenCount = 12,
            CumulativeTokens = 50,
            ChunkIndex = 3,
            Metadata = new Dictionary<string, object>
            {
                ["source"] = "manual.pdf",
                ["page"] = 5
            },
            Highlights = ["machine learning", "AI"]
        };

        Assert.Equal("chunk-42", chunk.ChunkId);
        Assert.Equal(12, chunk.TokenCount);
        Assert.Equal(50, chunk.CumulativeTokens);
        Assert.Equal(2, chunk.Metadata.Count);
        Assert.Equal(2, chunk.Highlights.Count);
    }
}

#endregion

#region Enum Tests

public class TokenAwareSearchEnumTests
{
    [Fact]
    public void QueryComplexityLevel_HasFourValues()
    {
        var values = Enum.GetValues<QueryComplexityLevel>();

        Assert.Equal(4, values.Length);
        Assert.Contains(QueryComplexityLevel.Simple, values);
        Assert.Contains(QueryComplexityLevel.Moderate, values);
        Assert.Contains(QueryComplexityLevel.Complex, values);
        Assert.Contains(QueryComplexityLevel.VeryComplex, values);
    }

    [Fact]
    public void QueryIntent_HasSixValues()
    {
        var values = Enum.GetValues<QueryIntent>();

        Assert.Equal(6, values.Length);
        Assert.Contains(QueryIntent.Informational, values);
        Assert.Contains(QueryIntent.HowTo, values);
        Assert.Contains(QueryIntent.Comparison, values);
        Assert.Contains(QueryIntent.Definition, values);
        Assert.Contains(QueryIntent.Troubleshooting, values);
        Assert.Contains(QueryIntent.Listing, values);
    }

    [Fact]
    public void EntityType_HasFiveValues()
    {
        var values = Enum.GetValues<EntityType>();

        Assert.Equal(5, values.Length);
        Assert.Contains(EntityType.Technology, values);
        Assert.Contains(EntityType.Concept, values);
        Assert.Contains(EntityType.Organization, values);
        Assert.Contains(EntityType.Person, values);
        Assert.Contains(EntityType.Other, values);
    }

    [Fact]
    public void SearchStrategy_HasFourValues()
    {
        var values = Enum.GetValues<SearchStrategy>();

        Assert.Equal(4, values.Length);
        Assert.Contains(SearchStrategy.Vector, values);
        Assert.Contains(SearchStrategy.Keyword, values);
        Assert.Contains(SearchStrategy.Hybrid, values);
        Assert.Contains(SearchStrategy.Adaptive, values);
    }
}

#endregion
