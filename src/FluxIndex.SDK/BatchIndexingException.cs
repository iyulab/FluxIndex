namespace FluxIndex.SDK;

/// <summary>
/// Some documents of an <c>Indexer.IndexBatchAsync</c>
/// batch could not be indexed. The batch went on without them: <see cref="IndexedIds"/> were written, and
/// <see cref="Failures"/> holds why each other document was not.
/// </summary>
public sealed class BatchIndexingException : Exception
{
    /// <summary>Creates the exception for a batch whose <paramref name="failures"/> were left out.</summary>
    /// <param name="indexedIds">The ids written, in input order.</param>
    /// <param name="failures">Each document left out, by id, with the exception that stopped it.</param>
    public BatchIndexingException(IReadOnlyList<string> indexedIds, IReadOnlyDictionary<string, Exception> failures)
        : base(Describe(indexedIds, failures), failures.Values.FirstOrDefault())
    {
        IndexedIds = indexedIds;
        Failures = failures;
    }

    /// <summary>The ids written, in input order — the same list a fully successful batch returns.</summary>
    public IReadOnlyList<string> IndexedIds { get; }

    /// <summary>Each document that was not indexed, by id, with the exception that stopped it.</summary>
    public IReadOnlyDictionary<string, Exception> Failures { get; }

    private static string Describe(IReadOnlyList<string> indexedIds, IReadOnlyDictionary<string, Exception> failures)
    {
        const int shown = 5;
        var examples = string.Join("; ", failures.Take(shown).Select(f => $"{f.Key}: {f.Value.Message}"));
        var more = failures.Count > shown ? $"; and {failures.Count - shown} more" : "";
        return $"{failures.Count} of {failures.Count + indexedIds.Count} documents could not be indexed "
               + $"(the other {indexedIds.Count} were): {examples}{more}";
    }
}
