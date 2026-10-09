using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Application.Services.Fusion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using QueryType = FluxIndex.Core.Application.Interfaces.QueryType;

namespace FluxIndex.Core.Tests.Services.Fusion;

/// <summary>
/// Dynamic Alpha Tuning and learning-based fusion classify a query with the same analyzer, so they agree on its
/// type, complexity and technical-term flag. Learning-based fusion used to run its own rules: "for" matched the
/// comparison indicator "or", and "How do I …" was a natural question to one and a reasoning query to the other.
/// </summary>
public class QueryClassificationAgreementTests
{
    private readonly QueryComplexityAnalyzer _analyzer = new(NullLogger<QueryComplexityAnalyzer>.Instance);

    public static TheoryData<string> Queries =>
    [
        "invoices",
        "retention policy for archived invoices",
        "SQL index JSON API",
        "How do I archive old invoices?",
        "Compare TensorFlow vs PyTorch",
        "When did the billing format change?",
        "machine learning algorithms detailed explanation",
        "How does deep learning work and why is it effective?",
        "export the report and then email it",
    ];

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task DatAndLearningBasedFusion_ClassifyAQueryTheSameWay(string query)
    {
        var ct = TestContext.Current.CancellationToken;
        var dat = new DynamicFusionService(_analyzer, NullLogger<DynamicFusionService>.Instance);
        var learning = new LearningBasedFusionService(_analyzer, NullLogger<LearningBasedFusionService>.Instance);
        var analysis = await _analyzer.AnalyzeAsync(query, ct);

        var config = await dat.CalculateDynamicWeightsAsync(query, ct);
        var prediction = await learning.PredictWeightsAsync(query, [], [], ct);

        Assert.Equal(config.QueryType, prediction.Features.QueryType);
        Assert.Equal(config.Complexity, prediction.Features.Complexity);
        Assert.Equal(analysis.ContainsTechnicalTerms, prediction.Features.ContainsTechnicalTerms);
    }

    [Fact]
    public async Task Training_FilesAnExampleUnderTheAnalyzersQueryType()
    {
        // "retention policy for archived invoices" is a simple keyword query to the analyzer; the old learning-based
        // rules filed it as a comparison ("for" contains "or").
        var ct = TestContext.Current.CancellationToken;
        var learning = new LearningBasedFusionService(_analyzer, NullLogger<LearningBasedFusionService>.Instance);
        var before = learning.GetModelStatistics().QueryTypeDistribution;

        await learning.TrainAsync(
            [new FusionTrainingExample
            {
                Query = "retention policy for archived invoices",
                OptimalWeights = new FusionWeights { VectorWeight = 0.4, SparseWeight = 0.6 },
                RelevanceScore = 0.8,
            }],
            ct);

        var after = learning.GetModelStatistics().QueryTypeDistribution;
        Assert.Equal(before[QueryType.SimpleKeyword] + 1, after[QueryType.SimpleKeyword]);
        Assert.Equal(before[QueryType.ComparisonQuery], after[QueryType.ComparisonQuery]);
    }
}
