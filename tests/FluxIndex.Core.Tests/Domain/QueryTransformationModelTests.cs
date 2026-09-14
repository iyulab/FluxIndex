using FluxIndex.Core.Domain.Models;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

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
    public void QueryIntent_HasFiveValues()
    {
        var values = Enum.GetValues<QueryIntent>();

        Assert.Equal(5, values.Length);
        Assert.Contains(QueryIntent.Learning, values);
        Assert.Contains(QueryIntent.Reference, values);
    }
}

#endregion
