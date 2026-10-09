using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

public class QualityMonitoringServiceTests
{
    [Fact]
    public async Task EvaluateSearchQuality_SearchWithNoResults_IsNotReportedAsAFailedSearch()
    {
        using var service = new QualityMonitoringService(NullLogger<QualityMonitoringService>.Instance);

        var metrics = await service.EvaluateSearchQualityAsync(
            "nothing matches", [], TimeSpan.FromMilliseconds(5), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(metrics.HasResults);
        Assert.Null(metrics.SearchError);
        var alerts = await service.GetQualityAlertsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(alerts, a => a.Type == AlertType.Availability);
        Assert.Contains(alerts, a => a.Type == AlertType.Quality);
    }

    [Fact]
    public async Task EvaluateSearchQuality_MeasuresTheScoresAndHowManyDocumentsTheResultsCome_From()
    {
        using var service = new QualityMonitoringService(NullLogger<QualityMonitoringService>.Instance);
        static VectorSearchResult Hit(string document, double score) =>
            new() { DocumentChunk = new DocumentChunk { Id = Guid.NewGuid().ToString(), DocumentId = document, Content = "x" }, Score = score };

        var metrics = await service.EvaluateSearchQualityAsync(
            "q", [Hit("a", 0.9), Hit("a", 0.7), Hit("b", 0.5), Hit("c", 0.3)], TimeSpan.FromMilliseconds(5),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, metrics.ResultCount);
        Assert.Equal(0.6, metrics.AverageSimilarity, 3);
        Assert.Equal(0.75, metrics.DiversityScore, 3); // three documents across four results
    }

    [Fact]
    public async Task EvaluateSearchQuality_FailedSearch_RaisesAnAvailabilityAlertWithTheError()
    {
        using var service = new QualityMonitoringService(NullLogger<QualityMonitoringService>.Instance);
        var metadata = new Dictionary<string, object> { ["error"] = "vector store unreachable" };

        var metrics = await service.EvaluateSearchQualityAsync(
            "q", [], TimeSpan.FromMilliseconds(5), metadata, TestContext.Current.CancellationToken);

        Assert.Equal("vector store unreachable", metrics.SearchError);
        var alerts = await service.GetQualityAlertsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var availability = Assert.Single(alerts, a => a.Type == AlertType.Availability);
        Assert.Contains("vector store unreachable", availability.Message);
        Assert.DoesNotContain(alerts, a => a.Type == AlertType.Quality);
    }
}
