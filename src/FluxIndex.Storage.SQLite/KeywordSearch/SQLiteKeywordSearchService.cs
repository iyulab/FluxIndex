using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data.Common;
using System.Globalization;

namespace FluxIndex.Storage.SQLite.KeywordSearch;

/// <summary>
/// SQLite-backed implementation of <see cref="IKeywordSearchService"/>.
/// Persists the inverted index in the same database file as the vector store.
/// <para>
/// The index behavior — schema shape, tokenization, BM25 ranking, maintenance — lives in
/// <see cref="RelationalKeywordSearchService"/> so it cannot diverge from the other SQL backends.
/// What remains here is SQLite dialect: its upsert syntax, its in-memory database lifetime, and the
/// statement-length bound that makes an inlined id list safe.
/// </para>
/// </summary>
public sealed class SQLiteKeywordSearchService : RelationalKeywordSearchService
{
    private readonly string _connectionString;

    /// <summary>
    /// An in-memory SQLite database exists only while a connection to it is open — the moment the
    /// last one closes, the schema and every row go with it. Every operation here opens and closes
    /// its own connection, so an in-memory database needs one connection held for the lifetime of
    /// this service. File-backed databases need nothing and this stays null.
    /// </summary>
    private SqliteConnection? _keepAliveConnection;

    private bool IsInMemory =>
        _connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase) ||
        _connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates the service from the configured SQLite options.</summary>
    public SQLiteKeywordSearchService(
        IOptions<SQLiteOptions> options,
        ILogger<SQLiteKeywordSearchService> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;
        _connectionString = opts.UseInMemory
            ? "Data Source=:memory:;Mode=Memory;Cache=Shared"
            : $"Data Source={opts.DatabasePath}";
    }

    /// <summary>Creates the service against an explicit connection string.</summary>
    public SQLiteKeywordSearchService(
        string connectionString,
        ILogger<SQLiteKeywordSearchService> logger)
        : base(logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <inheritdoc />
    protected override string BackendName => "SQLite";

    /// <inheritdoc />
    protected override DbConnection CreateConnection() => new SqliteConnection(_connectionString);

    /// <inheritdoc />
    protected override async Task OnInitializingAsync(CancellationToken cancellationToken)
    {
        if (!IsInMemory)
            return;

        _keepAliveConnection = new SqliteConnection(_connectionString);
        await _keepAliveConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// NOTE: bm25_chunks holds the indexed chunk payload. An earlier revision read it back from the
    /// vector store's private "vectors" table instead, which made this service unusable on its own
    /// and — worse — silently skipped deletion whenever the vector rows had already gone (the natural
    /// order in Indexer.DeleteByDocumentIdAsync), leaving ghost matches behind.
    /// <para>
    /// The NOCASE collation is retained for databases created by earlier versions; lookups normalize
    /// case in managed code, so it is no longer relied upon.
    /// </para>
    /// </summary>
    protected override string SchemaDdl => """
        CREATE TABLE IF NOT EXISTS bm25_terms (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            term TEXT NOT NULL UNIQUE COLLATE NOCASE,
            document_frequency INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS bm25_postings (
            term_id INTEGER NOT NULL,
            chunk_id TEXT NOT NULL,
            term_frequency INTEGER NOT NULL,
            document_length INTEGER NOT NULL,
            PRIMARY KEY (term_id, chunk_id),
            FOREIGN KEY (term_id) REFERENCES bm25_terms(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS bm25_chunks (
            chunk_id TEXT PRIMARY KEY,
            document_id TEXT NOT NULL,
            chunk_index INTEGER NOT NULL DEFAULT 0,
            content TEXT NOT NULL,
            token_count INTEGER NOT NULL DEFAULT 0,
            metadata TEXT
        );

        CREATE TABLE IF NOT EXISTS bm25_chunk_metadata (
            chunk_id TEXT NOT NULL,
            meta_key TEXT NOT NULL,
            meta_value TEXT NOT NULL,
            PRIMARY KEY (chunk_id, meta_key, meta_value)
        );

        CREATE TABLE IF NOT EXISTS bm25_statistics (
            key TEXT PRIMARY KEY,
            value REAL NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_bm25_terms_term ON bm25_terms(term);
        CREATE INDEX IF NOT EXISTS idx_bm25_postings_chunk ON bm25_postings(chunk_id);
        CREATE INDEX IF NOT EXISTS idx_bm25_chunks_document ON bm25_chunks(document_id);
        CREATE INDEX IF NOT EXISTS idx_bm25_chunk_metadata_lookup
            ON bm25_chunk_metadata(meta_key, meta_value);
        """;

    /// <inheritdoc />
    protected override string UpsertChunkSql => """
        INSERT INTO bm25_chunks (chunk_id, document_id, chunk_index, content, token_count, metadata)
        VALUES (@chunkId, @documentId, @chunkIndex, @content, @tokenCount, @metadata)
        ON CONFLICT(chunk_id) DO UPDATE SET
            document_id = excluded.document_id,
            chunk_index = excluded.chunk_index,
            content = excluded.content,
            token_count = excluded.token_count,
            metadata = excluded.metadata;
        """;

    /// <inheritdoc />
    protected override string UpsertTermReturningIdSql => """
        INSERT INTO bm25_terms (term, document_frequency) VALUES (@term, 0)
        ON CONFLICT(term) DO UPDATE SET term = excluded.term
        RETURNING id;
        """;

    /// <inheritdoc />
    protected override string UpsertPostingSql => """
        INSERT OR REPLACE INTO bm25_postings (term_id, chunk_id, term_frequency, document_length)
        VALUES (@termId, @chunkId, @tf, @docLen);
        """;

    /// <inheritdoc />
    protected override string UpsertStatisticSql => """
        INSERT OR REPLACE INTO bm25_statistics (key, value) VALUES ('total_documents', @totalDocs);
        INSERT OR REPLACE INTO bm25_statistics (key, value) VALUES ('avg_doc_length', @avgLength);
        """;

    /// <inheritdoc />
    protected override string? CompactSql => "VACUUM";

    /// <summary>
    /// Term ids are inlined rather than passed as an array parameter, which SQLite has no type for.
    /// They are <see cref="long"/> values read out of the index, so the statement cannot be injected
    /// into — but it does grow with the batch, hence <see cref="TermIdBatchSize"/>.
    /// </summary>
    protected override string BuildTermIdPredicate(
        DbCommand command,
        string columnRef,
        IReadOnlyCollection<long> termIds)
    {
        var ids = string.Join(",", termIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        return $"{columnRef} IN ({ids})";
    }

    /// <summary>
    /// Bounds the inlined id list well below SQLITE_MAX_SQL_LENGTH (1 MB by default). Indexing a
    /// large batch can touch tens of thousands of distinct terms, and an unbounded list would fail
    /// the whole statement at some corpus size instead of scaling.
    /// </summary>
    protected override int TermIdBatchSize => 5_000;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAliveConnection?.Dispose();
            _keepAliveConnection = null;
        }

        base.Dispose(disposing);
    }
}
