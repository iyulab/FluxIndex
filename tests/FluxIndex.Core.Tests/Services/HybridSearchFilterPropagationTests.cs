using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// Pins that a hybrid query's scope reaches <b>both</b> legs.
///
/// <para>
/// The keyword leg took no filter at all, so a scoped hybrid search fused a scoped vector list with
/// an unscoped keyword list and the unscoped rows survived into the output. The caller saw results
/// and had no way to tell they were the wrong ones — which is why this is pinned at the service, not
/// only at the options mapper: the mapper carrying a filter proves nothing if the service drops it.
/// </para>
/// </summary>
public class HybridSearchFilterPropagationTests
{
    private static DocumentChunk Chunk(string documentId, string content, string? tenant = null)
    {
        var chunk = DocumentChunk.Create(documentId, content, 0, 1);
        chunk.Score = 0.9f;
        if (tenant is not null)
            chunk.Metadata = new Dictionary<string, object> { ["tenant"] = tenant };
        return chunk;
    }

    private static (HybridSearchService Service, IKeywordSearchService Keyword) CreateService()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
                Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DocumentChunk>>([Chunk("doc-v", "vector hit", "alpha")]));

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 0.1f, 0.2f, 0.3f }));

        var keyword = Substitute.For<IKeywordSearchService>();
        keyword.SearchAsync(Arg.Any<string>(), Arg.Any<KeywordSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KeywordSearchResult>>(
                [new KeywordSearchResult { Chunk = Chunk("doc-k", "keyword hit", "alpha"), Score = 1.0 }]));

        var service = new HybridSearchService(
            vectorStore, keyword, embeddingService, NullLogger<HybridSearchService>.Instance);

        return (service, keyword);
    }

    [Fact]
    public async Task QueryLevelFilter_ReachesTheKeywordLeg()
    {
        var (service, keyword) = CreateService();

        await service.SearchAsync("report", new HybridSearchOptions
        {
            Filters = new Dictionary<string, object> { ["tenant"] = "alpha" }
        }, TestContext.Current.CancellationToken);

        await keyword.Received(1).SearchAsync(
            "report",
            Arg.Is<KeywordSearchOptions>(o =>
                o.MetadataFilter != null && o.MetadataFilter.ContainsKey("tenant")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryLevelFilter_ReachesTheVectorLeg()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
                Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DocumentChunk>>([Chunk("doc-v", "vector hit", "alpha")]));

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 0.1f, 0.2f, 0.3f }));

        var keyword = Substitute.For<IKeywordSearchService>();
        keyword.SearchAsync(Arg.Any<string>(), Arg.Any<KeywordSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KeywordSearchResult>>([]));

        var service = new HybridSearchService(
            vectorStore, keyword, embeddingService, NullLogger<HybridSearchService>.Instance);

        await service.SearchAsync("report", new HybridSearchOptions
        {
            Filters = new Dictionary<string, object> { ["tenant"] = "alpha" }
        }, TestContext.Current.CancellationToken);

        await vectorStore.Received(1).SearchAsync(
            Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
            Arg.Is<Dictionary<string, object>?>(f => f != null && f.ContainsKey("tenant")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoFilter_LeavesTheKeywordLegUnscoped()
    {
        var (service, keyword) = CreateService();

        await service.SearchAsync("report", new HybridSearchOptions(), TestContext.Current.CancellationToken);

        await keyword.Received(1).SearchAsync(
            "report",
            Arg.Is<KeywordSearchOptions>(o => o.MetadataFilter == null),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The sparse leg degrades to empty on backend failure so one broken store does not take the
    /// whole search down. A malformed filter is not that: swallowing it would answer a scoped query
    /// with unscoped results and report success.
    /// </summary>
    [Fact]
    public async Task MalformedFilter_Surfaces_RatherThanDegradingToVectorOnly()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
                Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DocumentChunk>>([Chunk("doc-v", "vector hit")]));

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 0.1f, 0.2f, 0.3f }));

        var keyword = Substitute.For<IKeywordSearchService>();
        keyword.SearchAsync(Arg.Any<string>(), Arg.Any<KeywordSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<KeywordSearchResult>>>(_ => throw new ArgumentException("bad filter"));

        var service = new HybridSearchService(
            vectorStore, keyword, embeddingService, NullLogger<HybridSearchService>.Instance);

        var act = () => service.SearchAsync("report", new HybridSearchOptions
        {
            Filters = new Dictionary<string, object> { ["tenant"] = "alpha" }
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// A backend outage still degrades — the distinction above is between a caller error and a
    /// backend error, not "never degrade".
    /// </summary>
    [Fact]
    public async Task BackendFailure_StillDegradesToVectorOnly()
    {
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore
            .SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
                Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DocumentChunk>>([Chunk("doc-v", "vector hit")]));

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { 0.1f, 0.2f, 0.3f }));

        var keyword = Substitute.For<IKeywordSearchService>();
        keyword.SearchAsync(Arg.Any<string>(), Arg.Any<KeywordSearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<KeywordSearchResult>>>(_ => throw new InvalidOperationException("store down"));

        var service = new HybridSearchService(
            vectorStore, keyword, embeddingService, NullLogger<HybridSearchService>.Instance);

        var results = await service.SearchAsync("report", new HybridSearchOptions(), TestContext.Current.CancellationToken);

        results.Should().NotBeEmpty("the vector leg still answered");
    }

    [Fact]
    public async Task PerLegFilter_OverridesTheQueryLevelFilter()
    {
        var (service, keyword) = CreateService();

        var options = new HybridSearchOptions
        {
            Filters = new Dictionary<string, object> { ["tenant"] = "alpha" }
        };
        options.SparseOptions.Filters["tenant"] = "beta";

        await service.SearchAsync("report", options, TestContext.Current.CancellationToken);

        await keyword.Received(1).SearchAsync(
            "report",
            Arg.Is<KeywordSearchOptions>(o =>
                o.MetadataFilter != null && (string)o.MetadataFilter["tenant"] == "beta"),
            Arg.Any<CancellationToken>());
    }
}
