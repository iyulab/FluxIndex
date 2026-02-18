using FluxIndex.Core.Domain.Models;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

#region HyDEResult Tests

public class HyDEResultTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new HyDEResult();

        Assert.Equal(string.Empty, result.OriginalQuery);
        Assert.Equal(string.Empty, result.HypotheticalDocument);
        Assert.Empty(result.HypotheticalDocuments);
        Assert.Empty(result.QualityScores);
        Assert.Equal(0f, result.QualityScore);
        Assert.Equal(0, result.TokensUsed);
        Assert.Equal(0L, result.GenerationTimeMs);
    }

    [Fact]
    public void DocumentCount_WithMultipleDocuments_ReturnsCount()
    {
        var result = new HyDEResult
        {
            HypotheticalDocuments = new[] { "Doc1", "Doc2", "Doc3" }
        };

        Assert.Equal(3, result.DocumentCount);
    }

    [Fact]
    public void DocumentCount_WithOnlyPrimaryDocument_ReturnsOne()
    {
        var result = new HyDEResult
        {
            HypotheticalDocument = "Primary doc"
        };

        Assert.Equal(1, result.DocumentCount);
    }

    [Fact]
    public void DocumentCount_NoDocuments_ReturnsZero()
    {
        var result = new HyDEResult();

        Assert.Equal(0, result.DocumentCount);
    }

    [Fact]
    public void DocumentCount_EmptyPrimaryAndNoMulti_ReturnsZero()
    {
        var result = new HyDEResult
        {
            HypotheticalDocument = "",
            HypotheticalDocuments = Array.Empty<string>()
        };

        Assert.Equal(0, result.DocumentCount);
    }

    [Fact]
    public void DocumentCount_MultiTakesPriority_OverPrimary()
    {
        var result = new HyDEResult
        {
            HypotheticalDocument = "Primary",
            HypotheticalDocuments = new[] { "A", "B" }
        };

        Assert.Equal(2, result.DocumentCount);
    }

    [Theory]
    [InlineData(0.5f, "doc", true)]
    [InlineData(0.31f, "doc", true)]
    [InlineData(0.3f, "doc", false)]   // not > 0.3
    [InlineData(0.0f, "doc", false)]
    [InlineData(0.5f, "", false)]       // empty document
    [InlineData(0.5f, null, false)]
    public void IsSuccessful_ByQualityAndDocument(float quality, string? document, bool expected)
    {
        var result = new HyDEResult
        {
            QualityScore = quality,
            HypotheticalDocument = document ?? string.Empty
        };

        // Handle null case: HypotheticalDocument defaults to string.Empty
        if (document == null)
        {
            result = new HyDEResult { QualityScore = quality };
        }

        Assert.Equal(expected, result.IsSuccessful);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var result = new HyDEResult
        {
            OriginalQuery = "What is ML?",
            HypotheticalDocument = "Machine learning is...",
            HypotheticalDocuments = new[] { "Doc1", "Doc2" },
            QualityScores = new[] { 0.8f, 0.9f },
            QualityScore = 0.85f,
            TokensUsed = 150,
            GenerationTimeMs = 500
        };

        Assert.Equal("What is ML?", result.OriginalQuery);
        Assert.Equal(150, result.TokensUsed);
        Assert.Equal(500L, result.GenerationTimeMs);
        Assert.Equal(0.85f, result.QualityScore);
    }
}

#endregion

#region HyDEOptions Tests

public class HyDEOptionsTests
{
    [Fact]
    public void CreateDefault_HasExpectedValues()
    {
        var options = HyDEOptions.CreateDefault();

        Assert.Equal(300, options.MaxLength);
        Assert.Equal("informative", options.DocumentStyle);
        Assert.Equal(string.Empty, options.DomainContext);
        Assert.Equal(1, options.DocumentCount);
        Assert.Equal(0.1f, options.TemperatureStep);
        Assert.Empty(options.Perspectives);
        Assert.True(options.EnableParallelGeneration);
    }

    [Fact]
    public void CreateMultiHypothetical_SetsDocumentCount()
    {
        var options = HyDEOptions.CreateMultiHypothetical(5);

        Assert.Equal(5, options.DocumentCount);
        Assert.Equal(0.1f, options.TemperatureStep);
        Assert.True(options.EnableParallelGeneration);
    }

    [Theory]
    [InlineData(0, 1)]   // clamped to 1
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(15, 10)] // clamped to 10
    public void CreateMultiHypothetical_ClampsDocumentCount(int input, int expected)
    {
        var options = HyDEOptions.CreateMultiHypothetical(input);

        Assert.Equal(expected, options.DocumentCount);
    }

    [Fact]
    public void CreateWithPerspectives_SetsCountAndPerspectives()
    {
        var options = HyDEOptions.CreateWithPerspectives("expert", "beginner", "critical");

        Assert.Equal(3, options.DocumentCount);
        Assert.Equal(3, options.Perspectives.Count);
        Assert.Contains("expert", options.Perspectives);
        Assert.Contains("beginner", options.Perspectives);
        Assert.Contains("critical", options.Perspectives);
        Assert.True(options.EnableParallelGeneration);
    }

    [Fact]
    public void CreateWithPerspectives_SinglePerspective()
    {
        var options = HyDEOptions.CreateWithPerspectives("neutral");

        Assert.Equal(1, options.DocumentCount);
        Assert.Single(options.Perspectives);
    }
}

#endregion

#region QuOTEResult Tests

public class QuOTEResultTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new QuOTEResult();

        Assert.Equal(string.Empty, result.OriginalQuery);
        Assert.Empty(result.ExpandedQueries);
        Assert.Empty(result.RelatedQuestions);
        Assert.Empty(result.QueryWeights);
        Assert.Equal(0f, result.QualityScore);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var result = new QuOTEResult
        {
            OriginalQuery = "What is RAG?",
            ExpandedQueries = new[] { "Retrieval augmented generation", "RAG architecture" },
            RelatedQuestions = new[] { "How does RAG work?", "What are RAG benefits?" },
            QueryWeights = new Dictionary<string, float>
            {
                ["Retrieval augmented generation"] = 0.8f,
                ["RAG architecture"] = 0.6f
            },
            QualityScore = 0.9f
        };

        Assert.Equal("What is RAG?", result.OriginalQuery);
        Assert.Equal(2, result.ExpandedQueries.Count);
        Assert.Equal(2, result.RelatedQuestions.Count);
        Assert.Equal(2, result.QueryWeights.Count);
        Assert.Equal(0.9f, result.QualityScore);
    }
}

#endregion

#region QuOTEOptions Tests

public class QuOTEOptionsTests
{
    [Fact]
    public void CreateDefault_HasExpectedValues()
    {
        var options = QuOTEOptions.CreateDefault();

        Assert.Equal(3, options.MaxExpansions);
        Assert.Equal(5, options.MaxRelatedQuestions);
        Assert.Equal(0.7f, options.DiversityLevel);
        Assert.Empty(options.DomainWeights);
    }

    [Fact]
    public void DefaultConstructor_SameAsFactory()
    {
        var constructed = new QuOTEOptions();
        var factory = QuOTEOptions.CreateDefault();

        Assert.Equal(constructed.MaxExpansions, factory.MaxExpansions);
        Assert.Equal(constructed.MaxRelatedQuestions, factory.MaxRelatedQuestions);
        Assert.Equal(constructed.DiversityLevel, factory.DiversityLevel);
    }
}

#endregion

#region QueryDecompositionResult Tests

public class QueryDecompositionResultTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new QueryDecompositionResult();

        Assert.Equal(string.Empty, result.OriginalQuery);
        Assert.Empty(result.SubQueries);
        Assert.Equal(0f, result.Confidence);
        Assert.Equal(QueryRelationshipType.Sequential, result.RelationshipType);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var result = new QueryDecompositionResult
        {
            OriginalQuery = "Compare ML and DL and explain their applications",
            SubQueries = new[] { "What is ML?", "What is DL?", "Applications of ML and DL" },
            Confidence = 0.85f,
            RelationshipType = QueryRelationshipType.Parallel
        };

        Assert.Equal(3, result.SubQueries.Count);
        Assert.Equal(0.85f, result.Confidence);
        Assert.Equal(QueryRelationshipType.Parallel, result.RelationshipType);
    }
}

#endregion

#region QueryIntentResult Tests

public class QueryIntentResultTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new QueryIntentResult();

        Assert.Equal(string.Empty, result.OriginalQuery);
        Assert.Equal(string.Empty, result.PrimaryIntent);
        Assert.Equal(string.Empty, result.Domain);
        Assert.Equal(QueryType.Informational, result.QueryType);
        Assert.Equal(QueryComplexity.Simple, result.Complexity);
        Assert.Empty(result.Keywords);
        Assert.Equal(0f, result.Confidence);
        Assert.Equal(string.Empty, result.RecommendedStrategy);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var result = new QueryIntentResult
        {
            OriginalQuery = "How do I fix the authentication bug?",
            PrimaryIntent = "Troubleshooting",
            Domain = "Software Engineering",
            QueryType = QueryType.Troubleshooting,
            Complexity = QueryComplexity.Medium,
            Keywords = new[] { "fix", "authentication", "bug" },
            Confidence = 0.92f,
            RecommendedStrategy = "hybrid_search"
        };

        Assert.Equal(QueryType.Troubleshooting, result.QueryType);
        Assert.Equal(QueryComplexity.Medium, result.Complexity);
        Assert.Equal(3, result.Keywords.Count);
        Assert.Equal(0.92f, result.Confidence);
    }
}

#endregion

#region Enum Tests

public class QueryTransformationEnumTests
{
    [Fact]
    public void QueryRelationshipType_HasFourValues()
    {
        var values = Enum.GetValues<QueryRelationshipType>();

        Assert.Equal(4, values.Length);
        Assert.Contains(QueryRelationshipType.Sequential, values);
        Assert.Contains(QueryRelationshipType.Parallel, values);
        Assert.Contains(QueryRelationshipType.Hierarchical, values);
        Assert.Contains(QueryRelationshipType.Conditional, values);
    }

    [Fact]
    public void QueryType_HasNineValues()
    {
        var values = Enum.GetValues<QueryType>();

        Assert.Equal(9, values.Length);
    }

    [Fact]
    public void QueryComplexity_HasFourValues()
    {
        var values = Enum.GetValues<QueryComplexity>();

        Assert.Equal(4, values.Length);
        Assert.Contains(QueryComplexity.Simple, values);
        Assert.Contains(QueryComplexity.VeryComplex, values);
    }

    [Fact]
    public void QueryIntent_HasFiveValues()
    {
        var values = Enum.GetValues<QueryIntent>();

        Assert.Equal(5, values.Length);
        Assert.Contains(QueryIntent.Learning, values);
        Assert.Contains(QueryIntent.Reference, values);
    }
}

#endregion
