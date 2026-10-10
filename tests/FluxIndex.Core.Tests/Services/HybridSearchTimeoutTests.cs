using AwesomeAssertions;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FluxIndex.Core.Tests.Services;

/// <summary>
/// <see cref="HybridSearchOptions.TimeoutMs"/> bounds the whole hybrid search. The legs degrade a failing backend to an
/// empty list, so a timeout routed through them would look like "no results"; the search has to say it ran out of time.
/// </summary>
public class HybridSearchTimeoutTests
{
    private static readonly float[] Embedding = [0.1f, 0.2f, 0.3f];

    private static HybridSearchService ServiceWithSlowEmbedding(TimeSpan delay)
    {
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.GenerateQueryEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(delay, call.Arg<CancellationToken>());
                return Embedding;
            });

        var chunk = DocumentChunk.Create("doc-1", "slow but found", 0, 1);
        chunk.Score = 0.9f;
        var vectorStore = Substitute.For<IVectorStore>();
        vectorStore.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Dictionary<string, object>?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { chunk }.AsEnumerable());

        var keyword = Substitute.For<IKeywordSearchService>();
        keyword.SearchAsync(Arg.Any<string>(), Arg.Any<KeywordSearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeywordSearchResult>());

        return new HybridSearchService(vectorStore, keyword, embeddings, NullLogger<HybridSearchService>.Instance);
    }

    [Fact]
    public async Task SearchAsync_ThatOutlivesTimeoutMs_ThrowsTimeoutException_NotAShortResult()
    {
        var service = ServiceWithSlowEmbedding(TimeSpan.FromSeconds(3));

        var search = () => service.SearchAsync("query", new HybridSearchOptions { TimeoutMs = 100 }, TestContext.Current.CancellationToken);

        (await search.Should().ThrowAsync<TimeoutException>())
            .WithMessage("*100 ms*")
            .WithInnerException<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchAsync_CancelledByTheCaller_StillThrowsOperationCanceled_NotTimeout()
    {
        var service = ServiceWithSlowEmbedding(TimeSpan.FromSeconds(3));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        caller.CancelAfter(100);

        var search = () => service.SearchAsync("query", new HybridSearchOptions { TimeoutMs = 60_000 }, caller.Token);

        await search.Should().ThrowAsync<OperationCanceledException>("the caller cancelled; that is not a timeout");
    }

    [Fact]
    public async Task SearchAsync_WithTheDefaultOptions_SetsNoTimeLimit()
    {
        new HybridSearchOptions().TimeoutMs.Should().Be(0, "an unset timeout must keep the unbounded search it always was");
        var service = ServiceWithSlowEmbedding(TimeSpan.FromMilliseconds(300));

        var results = await service.SearchAsync("query", new HybridSearchOptions(), TestContext.Current.CancellationToken);

        results.Should().ContainSingle(r => r.Chunk.DocumentId == "doc-1");
    }
}
