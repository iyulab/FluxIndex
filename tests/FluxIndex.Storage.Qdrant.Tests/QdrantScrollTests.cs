using AwesomeAssertions;
using FluxIndex.Storage.Qdrant;
using Qdrant.Client.Grpc;
using Xunit;

namespace FluxIndex.Storage.Qdrant.Tests;

/// <summary>
/// Pins that a scroll is walked to completion one page at a time.
/// </summary>
/// <remarks>
/// <para>
/// The defect these guard against was reported from a consumer's deployment: document-scoped
/// retrieval issued a single unpaged scroll requesting payload and vectors, so the response grew
/// with the document rather than with a page size, and a ~3.3 MB spreadsheet exceeded the gRPC
/// receive limit with <c>ResourceExhausted</c>. Because that call was the first step of a rollback,
/// the rollback could not run either - a large document did not merely fail to index, it could
/// leave two generations in the store.
/// </para>
/// <para>
/// Like <see cref="QdrantCollectionSiblingsTests"/>, these carry no <c>Category=Integration</c> trait
/// and touch no container: every Docker-backed test here is excluded by CI, so a guard pinned only
/// there would never run. The paging rule is therefore expressed over a page-fetching delegate,
/// which is what makes it holdable without a server.
/// </para>
/// </remarks>
public class QdrantScrollTests
{
    private static PointId Offset(ulong n) => new() { Num = n };

    [Fact]
    public async Task SinglePage_ReturnsItemsAndStops()
    {
        var calls = 0;

        var all = await QdrantScroll.AllPagesAsync<string>((offset, _) =>
        {
            calls++;
            offset.Should().BeNull("the first page is fetched without an offset");
            return Task.FromResult<(IReadOnlyList<string>, PointId?)>((["a", "b"], null));
        });

        all.Should().Equal("a", "b");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task MultiplePages_AreAccumulatedInOrder()
    {
        // The reported failure is precisely this case collapsed into one call: everything the
        // document holds, in a single response.
        var pages = new Dictionary<ulong, (string[] Items, PointId? Next)>
        {
            [0] = (["a", "b"], Offset(1)),
            [1] = (["c", "d"], Offset(2)),
            [2] = (["e"], null)
        };

        var all = await QdrantScroll.AllPagesAsync<string>((offset, _) =>
        {
            var key = offset?.Num ?? 0;
            var (items, next) = pages[key];
            return Task.FromResult<(IReadOnlyList<string>, PointId?)>((items, next));
        });

        all.Should().Equal("a", "b", "c", "d", "e");
    }

    [Fact]
    public async Task EmptyResult_ReturnsEmptyWithoutASecondFetch()
    {
        var calls = 0;

        var all = await QdrantScroll.AllPagesAsync<string>((_, _) =>
        {
            calls++;
            return Task.FromResult<(IReadOnlyList<string>, PointId?)>(([], null));
        });

        all.Should().BeEmpty();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task OffsetThatDoesNotAdvance_Throws_RatherThanLoopingForever()
    {
        // A server repeating the offset it was handed would make the loop non-terminating. Failing
        // names the condition; hanging would be diagnosed as a stuck process with no cause.
        var act = async () => await QdrantScroll.AllPagesAsync<string>((offset, _) =>
            Task.FromResult<(IReadOnlyList<string>, PointId?)>((["x"], offset ?? Offset(7))));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*did not advance*");
    }

    [Fact]
    public async Task Cancellation_IsPassedToEveryPageFetch()
    {
        using var cts = new CancellationTokenSource();

        await QdrantScroll.AllPagesAsync<string>((_, ct) =>
        {
            ct.Should().Be(cts.Token);
            return Task.FromResult<(IReadOnlyList<string>, PointId?)>((["a"], null));
        }, cts.Token);
    }
}
