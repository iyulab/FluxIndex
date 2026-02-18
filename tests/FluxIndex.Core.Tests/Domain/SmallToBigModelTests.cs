using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Xunit;

namespace FluxIndex.Core.Tests.Domain;

#region SmallToBigResult Tests

public class SmallToBigResultTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var result = new SmallToBigResult();

        Assert.NotNull(result.PrimaryChunk);
        Assert.Empty(result.ContextChunks);
        Assert.Equal(0.0, result.RelevanceScore);
        Assert.Equal(0, result.WindowSize);
        Assert.Equal(string.Empty, result.ExpansionReason);
        Assert.NotNull(result.Strategy);
        Assert.NotNull(result.Metadata);
    }

    [Fact]
    public void CombinedText_CombinesPrimaryAndContext()
    {
        var result = new SmallToBigResult
        {
            PrimaryChunk = new DocumentChunk { Content = "Primary content" },
            ContextChunks = new List<DocumentChunk>
            {
                new() { Content = "Context A" },
                new() { Content = "Context B" }
            }
        };

        var combined = result.CombinedText;

        Assert.Contains("Primary content", combined);
        Assert.Contains("Context A", combined);
        Assert.Contains("Context B", combined);
        Assert.Contains("관련 컨텍스트", combined);
    }

    [Fact]
    public void CombinedText_EmptyContext_StillContainsPrimary()
    {
        var result = new SmallToBigResult
        {
            PrimaryChunk = new DocumentChunk { Content = "Only primary" }
        };

        Assert.Contains("Only primary", result.CombinedText);
    }

    [Fact]
    public void ContextQuality_DelegatesToMetadata()
    {
        var result = new SmallToBigResult
        {
            Metadata = new SmallToBigMetadata { ContextQualityScore = 0.87 }
        };

        Assert.Equal(0.87, result.ContextQuality);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var result = new SmallToBigResult
        {
            PrimaryChunk = new DocumentChunk { Content = "Primary" },
            ContextChunks = [new DocumentChunk { Content = "Ctx1" }],
            RelevanceScore = 0.95,
            WindowSize = 5,
            ExpansionReason = "High complexity query",
            Strategy = new ExpansionStrategy
            {
                Type = ExpansionStrategyType.Adaptive,
                Confidence = 0.9
            },
            Metadata = new SmallToBigMetadata
            {
                SearchTimeMs = 45.5,
                ContextQualityScore = 0.88
            }
        };

        Assert.Equal(0.95, result.RelevanceScore);
        Assert.Equal(5, result.WindowSize);
        Assert.Equal(ExpansionStrategyType.Adaptive, result.Strategy.Type);
        Assert.Equal(0.88, result.ContextQuality);
    }
}

#endregion

#region SmallToBigOptions Tests

public class SmallToBigOptionsTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var options = new SmallToBigOptions();

        Assert.Equal(10, options.MaxResults);
        Assert.Equal(0.1, options.MinRelevanceScore);
        Assert.Equal(3, options.DefaultWindowSize);
        Assert.Equal(10, options.MaxWindowSize);
        Assert.True(options.EnableAdaptiveWindowing);
        Assert.True(options.EnableSemanticExpansion);
        Assert.True(options.EnableHierarchicalExpansion);
        Assert.True(options.EnableSequentialExpansion);
        Assert.Equal(0.5, options.ContextQualityThreshold);
        Assert.Equal(0.9, options.DeduplicationThreshold);
        Assert.Equal(30000, options.TimeoutMs);
    }

    [Fact]
    public void CanBeCustomized()
    {
        var options = new SmallToBigOptions
        {
            MaxResults = 50,
            MinRelevanceScore = 0.3,
            DefaultWindowSize = 5,
            MaxWindowSize = 20,
            EnableAdaptiveWindowing = false,
            EnableSemanticExpansion = false,
            TimeoutMs = 60000
        };

        Assert.Equal(50, options.MaxResults);
        Assert.Equal(0.3, options.MinRelevanceScore);
        Assert.Equal(5, options.DefaultWindowSize);
        Assert.False(options.EnableAdaptiveWindowing);
        Assert.False(options.EnableSemanticExpansion);
        Assert.Equal(60000, options.TimeoutMs);
    }
}

#endregion

#region ExpansionStrategy Tests

public class ExpansionStrategyTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var strategy = new ExpansionStrategy();

        Assert.Equal(ExpansionStrategyType.Conservative, strategy.Type);
        Assert.Empty(strategy.Methods);
        Assert.Equal(0.0, strategy.Confidence);
        Assert.Equal(string.Empty, strategy.Reasoning);
        Assert.NotNull(strategy.ExpectedOutcome);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var strategy = new ExpansionStrategy
        {
            Type = ExpansionStrategyType.Aggressive,
            Methods = [ExpansionMethod.Hierarchical, ExpansionMethod.Semantic, ExpansionMethod.Sequential],
            Confidence = 0.92,
            Reasoning = "Complex multi-hop query requires broad context",
            ExpectedOutcome = new ExpectedOutcome
            {
                ExpectedPrecisionGain = 0.15,
                ExpectedContextRichness = 0.85,
                ExpectedLatencyIncrease = 0.3,
                ExpectedUserSatisfaction = 0.9
            }
        };

        Assert.Equal(ExpansionStrategyType.Aggressive, strategy.Type);
        Assert.Equal(3, strategy.Methods.Count);
        Assert.Equal(0.92, strategy.Confidence);
        Assert.Equal(0.85, strategy.ExpectedOutcome.ExpectedContextRichness);
    }
}

#endregion

#region SmallToBigMetadata Tests

public class SmallToBigMetadataTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var metadata = new SmallToBigMetadata();

        Assert.Equal(0.0, metadata.SearchTimeMs);
        Assert.Equal(0.0, metadata.ContextQualityScore);
        Assert.Equal(0.0, metadata.ExpansionEfficiency);
        Assert.Empty(metadata.MethodContributions);
        Assert.Equal(0.0, metadata.ContextDiversity);
        Assert.Equal(0.0, metadata.InformationRedundancy);
        Assert.Empty(metadata.Properties);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var metadata = new SmallToBigMetadata
        {
            SearchTimeMs = 45.5,
            ContextQualityScore = 0.88,
            ExpansionEfficiency = 0.75,
            MethodContributions = new Dictionary<ExpansionMethod, double>
            {
                [ExpansionMethod.Hierarchical] = 0.4,
                [ExpansionMethod.Sequential] = 0.35,
                [ExpansionMethod.Semantic] = 0.25
            },
            ContextDiversity = 0.82,
            InformationRedundancy = 0.12,
            Properties = new Dictionary<string, object>
            {
                ["query_type"] = "complex",
                ["iteration_count"] = 3
            }
        };

        Assert.Equal(45.5, metadata.SearchTimeMs);
        Assert.Equal(3, metadata.MethodContributions.Count);
        Assert.Equal(0.4, metadata.MethodContributions[ExpansionMethod.Hierarchical]);
        Assert.Equal(2, metadata.Properties.Count);
    }
}

#endregion

#region QueryComplexityAnalysis Tests

public class QueryComplexityAnalysisModelTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var analysis = new QueryComplexityAnalysis();

        Assert.Equal(0.0, analysis.OverallComplexity);
        Assert.Equal(0.0, analysis.LexicalComplexity);
        Assert.Equal(0.0, analysis.SyntacticComplexity);
        Assert.Equal(0.0, analysis.SemanticComplexity);
        Assert.Equal(0.0, analysis.ReasoningComplexity);
        Assert.Equal(0, analysis.RecommendedWindowSize);
        Assert.Equal(0.0, analysis.AnalysisConfidence);
        Assert.NotNull(analysis.Components);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var analysis = new QueryComplexityAnalysis
        {
            OverallComplexity = 0.75,
            LexicalComplexity = 0.6,
            SyntacticComplexity = 0.7,
            SemanticComplexity = 0.8,
            ReasoningComplexity = 0.9,
            RecommendedWindowSize = 7,
            AnalysisConfidence = 0.85,
            Components = new ComplexityComponents
            {
                TokenCount = 25,
                UniqueWordCount = 18,
                AverageWordLength = 5.5,
                PunctuationDensity = 0.08,
                TechnicalTermCount = 3,
                NamedEntityCount = 2,
                QuestionWordCount = 1,
                LogicalOperatorCount = 2
            }
        };

        Assert.Equal(0.75, analysis.OverallComplexity);
        Assert.Equal(7, analysis.RecommendedWindowSize);
        Assert.Equal(25, analysis.Components.TokenCount);
        Assert.Equal(18, analysis.Components.UniqueWordCount);
    }
}

#endregion

#region ComplexityComponents Tests

public class ComplexityComponentsTests
{
    [Fact]
    public void DefaultValues_AreAllZero()
    {
        var components = new ComplexityComponents();

        Assert.Equal(0, components.TokenCount);
        Assert.Equal(0, components.UniqueWordCount);
        Assert.Equal(0.0, components.AverageWordLength);
        Assert.Equal(0.0, components.PunctuationDensity);
        Assert.Equal(0, components.TechnicalTermCount);
        Assert.Equal(0, components.NamedEntityCount);
        Assert.Equal(0, components.QuestionWordCount);
        Assert.Equal(0, components.LogicalOperatorCount);
    }
}

#endregion

#region ExpectedOutcome Tests

public class ExpectedOutcomeTests
{
    [Fact]
    public void DefaultValues_AreAllZero()
    {
        var outcome = new ExpectedOutcome();

        Assert.Equal(0.0, outcome.ExpectedPrecisionGain);
        Assert.Equal(0.0, outcome.ExpectedContextRichness);
        Assert.Equal(0.0, outcome.ExpectedLatencyIncrease);
        Assert.Equal(0.0, outcome.ExpectedUserSatisfaction);
    }

    [Fact]
    public void CanBeFullyInitialized()
    {
        var outcome = new ExpectedOutcome
        {
            ExpectedPrecisionGain = 0.2,
            ExpectedContextRichness = 0.9,
            ExpectedLatencyIncrease = 0.15,
            ExpectedUserSatisfaction = 0.85
        };

        Assert.Equal(0.2, outcome.ExpectedPrecisionGain);
        Assert.Equal(0.9, outcome.ExpectedContextRichness);
    }
}

#endregion

#region Enum Tests

public class SmallToBigEnumTests
{
    [Fact]
    public void ExpansionStrategyType_HasFourValues()
    {
        var values = Enum.GetValues<ExpansionStrategyType>();

        Assert.Equal(4, values.Length);
        Assert.Contains(ExpansionStrategyType.Conservative, values);
        Assert.Contains(ExpansionStrategyType.Balanced, values);
        Assert.Contains(ExpansionStrategyType.Aggressive, values);
        Assert.Contains(ExpansionStrategyType.Adaptive, values);
    }

    [Fact]
    public void ExpansionMethod_HasSixValues()
    {
        var values = Enum.GetValues<ExpansionMethod>();

        Assert.Equal(6, values.Length);
        Assert.Contains(ExpansionMethod.Hierarchical, values);
        Assert.Contains(ExpansionMethod.Sequential, values);
        Assert.Contains(ExpansionMethod.Semantic, values);
        Assert.Contains(ExpansionMethod.Reference, values);
        Assert.Contains(ExpansionMethod.Keyword, values);
        Assert.Contains(ExpansionMethod.Entity, values);
    }
}

#endregion
