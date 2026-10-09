using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Core.Tests.Services.Fusion;

/// <summary>
/// Dynamic Alpha Tuning's documented «named entities → more keyword weight» rule, measured on the real analyzer: it now
/// applies to a query naming two things and to nothing else. Before entities were read from the query as written, it never
/// applied (every query had zero entities).
/// </summary>
public class DynamicFusionEntityWeightTests
{
    private readonly QueryComplexityAnalyzer _analyzer = new(NullLogger<QueryComplexityAnalyzer>.Instance);

    [Theory]
    [InlineData("choose between Kubernetes and Nomad for small clusters", true)]
    [InlineData("compare PostgreSQL and SQLite write throughput", true)]
    [InlineData("retention policy for archived invoices", false)]
    [InlineData("How does deep learning work?", false)]
    public async Task VectorWeight_MovesTowardKeywords_OnlyForQueriesNamingTwoThings(string query, bool shifts)
    {
        var fusion = new DynamicFusionService(_analyzer, NullLogger<DynamicFusionService>.Instance);
        var analysis = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        var withEntities = fusion.CalculateDynamicWeights(analysis);
        analysis.Entities = [];
        var withoutEntities = fusion.CalculateDynamicWeights(analysis);

        if (shifts)
            Assert.True(withEntities.VectorWeight < withoutEntities.VectorWeight,
                $"vector {withEntities.VectorWeight:0.000} should be below {withoutEntities.VectorWeight:0.000}");
        else
            Assert.Equal(withoutEntities.VectorWeight, withEntities.VectorWeight, 6);
    }
}
