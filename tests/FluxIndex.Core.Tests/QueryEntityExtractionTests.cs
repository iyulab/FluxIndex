using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Core.Tests;

/// <summary>
/// <see cref="QueryAnalysis.Entities"/> are the names a query mentions — Dynamic Alpha Tuning moves weight toward keyword
/// search when there are two or more, because names match exactly. The analyzer lower-cased the query before looking for
/// capitalised words, so no query ever had an entity and that rule never applied.
/// </summary>
public class QueryEntityExtractionTests
{
    private readonly QueryComplexityAnalyzer _analyzer = new(NullLogger<QueryComplexityAnalyzer>.Instance);

    [Theory]
    [InlineData("choose between Kubernetes and Nomad for small clusters", new[] { "Kubernetes", "Nomad" })]
    // Limit, pinned: a name that opens the query reads as sentence case and is not counted.
    [InlineData("Kubernetes vs Nomad for small clusters", new[] { "Nomad" })]
    [InlineData("compare PostgreSQL and SQLite write throughput", new[] { "PostgreSQL", "SQLite" })]
    [InlineData("deploy to GitHub Actions from Azure", new[] { "GitHub", "Actions", "Azure" })]
    [InlineData("What did Marie Curie discover?", new[] { "Marie", "Curie" })]
    public async Task Names_InTheQuery_AreEntities(string query, string[] expected)
    {
        var analysis = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(expected, analysis.Entities);
    }

    // Positive control: the capital that starts a sentence is not a name.
    [Theory]
    [InlineData("How does deep learning work?")]
    [InlineData("Retention policy for archived invoices")]
    [InlineData("invoices")]
    public async Task SentenceCase_AndLowerCase_HaveNoEntities(string query)
    {
        var analysis = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        Assert.Empty(analysis.Entities);
    }

    // An acronym or a name with an inner capital is a name even at the start.
    [Theory]
    [InlineData("SQL index tuning", "SQL")]
    [InlineData("GitHub rate limits", "GitHub")]
    public async Task LeadingAcronymOrInnerCapital_IsAnEntity(string query, string expected)
    {
        var analysis = await _analyzer.AnalyzeAsync(query, TestContext.Current.CancellationToken);

        Assert.Contains(expected, analysis.Entities);
    }
}
