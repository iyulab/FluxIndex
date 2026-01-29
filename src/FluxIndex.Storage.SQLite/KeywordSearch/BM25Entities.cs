namespace FluxIndex.Storage.SQLite.KeywordSearch;

/// <summary>
/// Entity for BM25 term index.
/// </summary>
public class BM25TermEntity
{
    /// <summary>
    /// Auto-generated term ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The term (word) itself.
    /// </summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>
    /// Document frequency: number of documents containing this term.
    /// </summary>
    public int DocumentFrequency { get; set; }
}

/// <summary>
/// Entity for BM25 posting list.
/// </summary>
public class BM25PostingEntity
{
    /// <summary>
    /// Reference to the term.
    /// </summary>
    public int TermId { get; set; }

    /// <summary>
    /// The chunk ID where this term appears.
    /// </summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>
    /// How many times this term appears in the chunk.
    /// </summary>
    public int TermFrequency { get; set; }

    /// <summary>
    /// Total length of the document in terms.
    /// </summary>
    public int DocumentLength { get; set; }

    // Navigation property
    public BM25TermEntity? Term { get; set; }
}

/// <summary>
/// Entity for BM25 index statistics.
/// </summary>
public class BM25StatisticsEntity
{
    /// <summary>
    /// Statistics key (e.g., "total_documents", "avg_doc_length").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Statistics value.
    /// </summary>
    public double Value { get; set; }
}
