using Qdrant.Client.Grpc;

namespace FluxIndex.Storage.Qdrant;

/// <summary>
/// Walks a Qdrant scroll to completion, one page at a time.
/// </summary>
/// <remarks>
/// <para>
/// Qdrant's scroll is a paging primitive: a response carries at most <c>limit</c> points and a
/// <see cref="ScrollResponse.NextPageOffset"/> pointing at the rest. Using it as a single-shot fetch
/// with a large limit makes the response size grow with the data rather than with the page size, and
/// a gRPC channel refuses a message over its receive limit (4 MB by default) with
/// <c>ResourceExhausted</c>. A consumer hit exactly that on a ~3.3 MB spreadsheet, and because the
/// failing call was the first step of a rollback, the rollback could not run either.
/// </para>
/// <para>
/// The loop lives here rather than being written out at each call site because this store already
/// had it in one place and not the other: distinct-document counting paged correctly while
/// document-scoped retrieval did not. Two implementations of one rule is how the second one stays
/// wrong without any test noticing.
/// </para>
/// <para>
/// The paging itself is transport-agnostic, so it is expressed over a page-fetching delegate and can
/// be held by tests that never open a channel - the same reason
/// <see cref="QdrantCollectionSiblings"/> is a pure function.
/// </para>
/// </remarks>
internal static class QdrantScroll
{
    /// <summary>
    /// Fetches every page and returns the accumulated items.
    /// </summary>
    /// <param name="fetchPage">
    /// Fetches one page starting at the given offset (null for the first page) and returns its items
    /// together with the offset of the next page, or null when this was the last one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token, passed to every page fetch.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a page reports an offset that does not advance. Continuing would re-fetch the same
    /// page forever; failing names the condition instead of hanging.
    /// </exception>
    public static async Task<List<T>> AllPagesAsync<T>(
        Func<PointId?, CancellationToken, Task<(IReadOnlyList<T> Items, PointId? NextOffset)>> fetchPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetchPage);

        var all = new List<T>();
        PointId? offset = null;

        while (true)
        {
            var (items, nextOffset) = await fetchPage(offset, cancellationToken).ConfigureAwait(false);
            all.AddRange(items);

            if (nextOffset is null)
                return all;

            // A server that keeps handing back the offset it was given would make this loop
            // non-terminating. That is a broken response, not a state to absorb silently.
            if (offset is not null && nextOffset.Equals(offset))
            {
                throw new InvalidOperationException(
                    $"Qdrant scroll did not advance: the page starting at offset '{offset}' reported " +
                    "the same offset as its next page. Refusing to re-fetch it indefinitely.");
            }

            offset = nextOffset;
        }
    }
}
