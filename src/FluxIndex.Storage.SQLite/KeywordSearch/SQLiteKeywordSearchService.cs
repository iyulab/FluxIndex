using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

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

    /// <summary>The store options' per-connection PRAGMAs, when the service was built from them; <c>null</c> for an explicit connection string.</summary>
    private readonly string? _connectionPragmas;

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
        ILogger<SQLiteKeywordSearchService> logger,
        ITextAnalyzer? analyzer = null,
        KeywordFieldOptions? fields = null)
        : base(logger, analyzer, fields)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;
        _connectionString = opts.UseInMemory
            ? "Data Source=:memory:;Mode=Memory;Cache=Shared"
            : $"Data Source={opts.DatabasePath}";
        _connectionPragmas = SQLiteConnectionPragmas.For(opts);
    }

    /// <summary>Shares a vector store's database and applies that store's per-connection PRAGMAs.</summary>
    internal SQLiteKeywordSearchService(
        SQLiteOptions storeOptions,
        ILogger<SQLiteKeywordSearchService> logger,
        ITextAnalyzer? analyzer,
        KeywordFieldOptions? fields)
        : this(storeOptions.GetConnectionString(), logger, analyzer, fields)
    {
        _connectionPragmas = SQLiteConnectionPragmas.For(storeOptions);
    }

    /// <summary>Creates the service against an explicit connection string.</summary>
    public SQLiteKeywordSearchService(
        string connectionString,
        ILogger<SQLiteKeywordSearchService> logger,
        ITextAnalyzer? analyzer = null,
        KeywordFieldOptions? fields = null)
        : base(logger, analyzer, fields)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <inheritdoc />
    protected override string BackendName => "SQLite";

    /// <inheritdoc />
    protected override DbConnection CreateConnection() => new SqliteConnection(_connectionString);

    /// <summary>Whether the database file is in WAL mode, once seen; until then every connection asks.</summary>
    private volatile bool _walConfirmed;

    /// <inheritdoc />
    /// <remarks>
    /// Built from store options, every connection gets their per-connection PRAGMAs (<c>busy_timeout</c>, <c>synchronous</c>,
    /// ...). Built from a bare connection string, a file in WAL mode gets <c>synchronous=NORMAL</c> on each connection — the setting the sqlite-vec store applies to
    /// the same file. With the default (<c>FULL</c>) every commit waits for a sync of the log, which made indexing one
    /// document at a time cost about 2 ms per document in this index alone. In WAL mode <c>NORMAL</c> keeps the database
    /// consistent through a crash of the application; a power loss can drop the last commits. A file in another journal
    /// mode is left at its default, where <c>NORMAL</c> would be weaker.
    /// </remarks>
    protected override async Task OnConnectionOpenedAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (_connectionPragmas is not null)
        {
            await SQLiteConnectionPragmas.ApplyAsync(connection, _connectionPragmas, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsInMemory)
            return;

        await using var command = connection.CreateCommand();
        if (!_walConfirmed)
        {
            command.CommandText = "PRAGMA journal_mode";
            var mode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                return;
            _walConfirmed = true;
        }

        command.CommandText = "PRAGMA synchronous=NORMAL";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

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

        CREATE TABLE IF NOT EXISTS bm25_field_postings (
            term_id INTEGER NOT NULL,
            chunk_id TEXT NOT NULL,
            field TEXT NOT NULL,
            term_frequency INTEGER NOT NULL,
            field_length INTEGER NOT NULL,
            document_length INTEGER NOT NULL,
            PRIMARY KEY (term_id, chunk_id, field),
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

        -- The UNIQUE constraint on bm25_terms.term already indexes the column; a second index on it only doubled
        -- the cost of every term insert and document-frequency update. Dropped where an earlier release created it.
        DROP INDEX IF EXISTS idx_bm25_terms_term;
        CREATE INDEX IF NOT EXISTS idx_bm25_postings_chunk ON bm25_postings(chunk_id);
        CREATE INDEX IF NOT EXISTS idx_bm25_field_postings_chunk ON bm25_field_postings(chunk_id);
        CREATE INDEX IF NOT EXISTS idx_bm25_chunks_document ON bm25_chunks(document_id);
        CREATE INDEX IF NOT EXISTS idx_bm25_chunk_metadata_lookup
            ON bm25_chunk_metadata(meta_key, meta_value);
        """;

    /// <inheritdoc />
    protected override string InsertTermIfAbsentSql => """
        INSERT INTO bm25_terms (term, document_frequency) VALUES (@term, 0)
        ON CONFLICT(term) DO NOTHING;
        """;

    /// <summary>
    /// The terms travel as one JSON array parameter, read back with <c>json_each</c>: term text is not safe to inline the
    /// way ids are, and one parameter per term made binding a statement cost the square of its term count, because
    /// SQLite resolves a parameter name by scanning the statement's names.
    /// </summary>
    protected override string BuildTermTextPredicate(DbCommand command, string columnRef, IReadOnlyCollection<string> terms)
    {
        var buffer = new ArrayBufferWriter<byte>();
        // Relaxed escaping keeps non-ASCII text as UTF-8 instead of \u escapes; the value is a bound parameter, not markup.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var term in terms)
                writer.WriteStringValue(term);
            writer.WriteEndArray();
        }

        AddParameter(command, "@terms", Encoding.UTF8.GetString(buffer.WrittenSpan));
        return $"{columnRef} IN (SELECT value FROM json_each(@terms))";
    }

    /// <summary>Inserts the missing terms through one prepared statement, rebinding the term for each row.</summary>
    protected override async Task InsertTermsAsync(DbConnection connection, IReadOnlyList<string> orderedTerms, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(orderedTerms);

        await using var command = connection.CreateCommand();
        command.CommandText = InsertTermIfAbsentSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@term";
        command.Parameters.Add(parameter);
        foreach (var term in orderedTerms)
        {
            parameter.Value = term;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Inserts row by row through one prepared statement, rebinding its parameters for each row.
    /// </summary>
    /// <remarks>
    /// The default multi-row <c>VALUES</c> list carries thousands of named parameters per statement, and SQLite resolves
    /// a parameter name by scanning the statement's names, so binding one statement costs the square of its parameter
    /// count. A 10,000-chunk batch spent most of its time there. SQLite runs in-process, so the round-trips the multi-row
    /// form saves on a server cost nothing here, and reusing one compiled statement is the documented fast path for bulk
    /// inserts.
    /// </remarks>
    protected override async Task InsertRowsAsync(
        DbConnection connection,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyList<Array> values,
        string conflictClause,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);

        var count = values[0].Length;
        if (count == 0)
            return;

        await using var command = connection.CreateCommand();
        var parameters = new DbParameter[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            parameters[c] = command.CreateParameter();
            parameters[c].ParameterName = "@c" + c.ToString(CultureInfo.InvariantCulture);
            command.Parameters.Add(parameters[c]);
        }

        command.CommandText =
            $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters.Select(p => p.ParameterName))}) {conflictClause}";

        for (var r = 0; r < count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
                parameters[c].Value = values[c].GetValue(r) ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override string UpsertStatisticSql => """
        INSERT OR REPLACE INTO bm25_statistics (key, value) VALUES (@key, @value);
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
