using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Services.SelfRAG;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// <c>SelfRAGOptions.SearchTimeout</c> was declared and dropped where the service built the adaptive
/// search's options — the one place it could have taken effect. It must reach the search as its Timeout.
/// </summary>
public class SelfRAGSearchTimeoutTests
{
    [Fact]
    public async Task SearchAsync_PassesSearchTimeout_ToTheAdaptiveSearch()
    {
        var adaptive = Substitute.For<IAdaptiveSearchService>();
        adaptive.SearchAsync(Arg.Any<string>(), Arg.Any<AdaptiveSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new AdaptiveSearchResult());
        var analyzer = Substitute.For<IQueryComplexityAnalyzer>();
        analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new QueryAnalysis());
        var service = new SelfRAGService(adaptive, analyzer, NullLogger<SelfRAGService>.Instance);

        await service.SearchAsync("q", new SelfRAGOptions { SearchTimeout = TimeSpan.FromSeconds(7), MaxIterations = 1 }, TestContext.Current.CancellationToken);

        await adaptive.Received().SearchAsync(
            Arg.Any<string>(),
            Arg.Is<AdaptiveSearchOptions?>(o => o != null && o.Timeout == TimeSpan.FromSeconds(7)),
            Arg.Any<CancellationToken>());
    }
}
