using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Utilities;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxIndex.Core.Application.Services.KeywordSearch;

/// <summary>
/// Relational BM25 keyword index shared by every SQL storage backend. Holds the inverted-index
/// schema shape, the tokenizer, the BM25 scoring, and the index maintenance; subclasses supply only
/// what actually differs between SQL dialects (connection, DDL, upsert syntax, id-list predicate).
/// <para>
/// The split exists so the backends cannot drift: BM25 ranking has to mean the same thing whichever
/// store a consumer configured, and a second hand-written copy of the scoring is the surest way to
/// break that quietly. Anything a subclass overrides is dialect syntax, never ranking behavior.
/// </para>
/// </summary>
public abstract partial class RelationalKeywordSearchService : IKeywordSearchService, IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    /// <summary>Logger used for the shared index operations.</summary>
    protected ILogger Logger { get; }

    /// <summary>Backend name used in log messages (e.g. "SQLite", "PostgreSQL").</summary>
    protected abstract string BackendName { get; }

    /// <summary>
    /// The analyzer that turns chunk content and queries into index terms. One instance serves both
    /// the index path and the query path, so the two cannot disagree; <see cref="DefaultTextAnalyzer"/>
    /// unless the consumer supplies one.
    /// </summary>
    protected ITextAnalyzer Analyzer { get; }

    /// <summary>
    /// The metadata fields scored alongside the chunk body (BM25F), and their weights. One instance
    /// serves the index path and the query path, so a field indexed is a field queried; the default
    /// scores <c>title</c> and <c>file_name</c>, <see cref="KeywordFieldOptions.None"/> is body-only.
    /// </summary>
    protected KeywordFieldOptions Fields { get; }

    /// <summary>
    /// Initializes the shared index with the logger used for its operations and, optionally, the
    /// analyzer that defines what a term is (<see cref="DefaultTextAnalyzer"/> when omitted) and the
    /// metadata fields scored beside the body (the <see cref="KeywordFieldOptions"/> default when omitted).
    /// </summary>
    protected RelationalKeywordSearchService(ILogger logger, ITextAnalyzer? analyzer = null, KeywordFieldOptions? fields = null)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Analyzer = analyzer ?? DefaultTextAnalyzer.Instance;
        Fields = fields ?? new KeywordFieldOptions();
        _configuredFields = new HashSet<string>(Fields.Fields.Select(f => f.MetadataKey), StringComparer.Ordinal);
        _documentFrequencyFieldsKey = DocumentFrequencyFieldsKeyPrefix
            + JsonSerializer.Serialize(_configuredFields.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>The metadata keys of <see cref="Fields"/>: the field rows that count toward document frequency.</summary>
    private readonly HashSet<string> _configuredFields;

    /// <summary>
    /// The statistics key recording which field set the stored document frequencies count. Document frequency is
    /// maintained by deltas, so a store opened under a different field set has to be recounted once.
    /// </summary>
    private readonly string _documentFrequencyFieldsKey;

    private const string DocumentFrequencyFieldsKeyPrefix = "df_fields:";

    #region Dialect surface

    /// <summary>Creates a new, unopened connection to the backend.</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>
    /// DDL that creates the index relations and their indexes if absent: <c>bm25_terms</c>,
    /// <c>bm25_postings</c>, <c>bm25_field_postings</c>, <c>bm25_chunks</c>, <c>bm25_chunk_metadata</c>,
    /// <c>bm25_statistics</c>. Every statement is <c>IF NOT EXISTS</c>, so an index created by an
    /// earlier version gains the relations it lacks on first use and keeps ranking as before until
    /// its chunks are re-indexed.
    /// </summary>
    protected abstract string SchemaDdl { get; }

    /// <summary>Upsert for a row of <c>bm25_chunks</c>, keyed on <c>chunk_id</c>.</summary>
    protected abstract string UpsertChunkSql { get; }

    /// <summary>
    /// Inserts a row of <c>bm25_terms</c> (<c>@term</c>, document frequency 0) unless the term already has one,
    /// without locking the existing row: a term row is shared by every writer whose text contains the word.
    /// </summary>
    protected abstract string InsertTermIfAbsentSql { get; }

    /// <summary>
    /// Builds the predicate selecting the rows of <paramref name="terms"/> by their text, adding any parameters it
    /// needs to <paramref name="command"/>. Called with at most <see cref="TermIdBatchSize"/> terms.
    /// </summary>
    protected abstract string BuildTermTextPredicate(DbCommand command, string columnRef, IReadOnlyCollection<string> terms);

    /// <summary>
    /// Whether new terms are registered in a short transaction of their own, committed before the write transaction
    /// starts. False by default: the backend runs one writer at a time, so a second commit would only cost an fsync.
    /// </summary>
    /// <remarks>
    /// Where writers run concurrently it must be true. A term inserted inside the write transaction is invisible
    /// until that transaction commits, and every other writer inserting the same word waits on it for that long -
    /// for a large document, the whole document.
    /// </remarks>
    protected virtual bool RegistersTermsInOwnTransaction => false;

    /// <summary>
    /// Row-lock clause appended to the statement that takes a write transaction's term rows, in id order, just
    /// before their document frequency is updated, or null where the backend has no row locks. It must not conflict
    /// with the lock a posting's foreign key takes on its term row, which other writers hold to their commit.
    /// </summary>
    protected virtual string? TermRowLockClause => null;

    /// <summary>
    /// Row-lock clause for selecting the zero-frequency term rows a write removes, or null to delete them directly.
    /// Where writers run concurrently it should skip rows another writer holds: a term row is held only by a writer
    /// that is about to give it a posting, so waiting for it would only find a row to keep.
    /// </summary>
    protected virtual string? TermCleanupLockClause => null;

    /// <summary>
    /// Serializes write transactions that write or delete the same chunks, taking a transaction-scoped lock per
    /// chunk id in one fixed order. Does nothing by default, for a backend that runs one writer at a time.
    /// </summary>
    /// <remarks>
    /// Document frequency moves by the postings a transaction deleted and wrote, which is exact only if no other
    /// transaction is writing the same chunk at the same time: otherwise both see the chunk empty, both count its
    /// terms as new, and the frequency is counted twice.
    /// </remarks>
    protected virtual Task LockChunksAsync(DbConnection connection, IReadOnlyCollection<string> chunkIds, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Whether a failure means a term row this transaction wrote a posting for was removed by another writer's
    /// cleanup (for example a foreign-key violation on the posting). The transaction is then retried, which registers
    /// the term again. False by default.
    /// </summary>
    protected virtual bool IsTermRowRemovedFailure(DbException exception) => false;

    /// <summary>Upsert for a row of <c>bm25_postings</c>, keyed on (<c>term_id</c>, <c>chunk_id</c>).</summary>
    protected abstract string UpsertPostingSql { get; }

    /// <summary>
    /// Upsert for a row of <c>bm25_field_postings</c>, keyed on (<c>term_id</c>, <c>chunk_id</c>, <c>field</c>),
    /// with parameters <c>@termId</c>, <c>@chunkId</c>, <c>@field</c>, <c>@tf</c>, <c>@fieldLen</c>, <c>@docLen</c>.
    /// </summary>
    protected abstract string UpsertFieldPostingSql { get; }

    /// <summary>Upsert for one row of <c>bm25_statistics</c> (<c>@key</c>, <c>@value</c>), keyed on <c>key</c>.</summary>
    protected abstract string UpsertStatisticSql { get; }

    /// <summary>Statement run by <see cref="OptimizeIndexAsync"/> to compact the store, or null if none applies.</summary>
    protected abstract string? CompactSql { get; }

    /// <summary>
    /// Builds the predicate selecting <paramref name="termIds"/>, adding any parameters it needs to
    /// <paramref name="command"/>. Dialects differ here: an array parameter keeps the statement a
    /// fixed size, whereas an inlined list grows with the batch.
    /// </summary>
    protected abstract string BuildTermIdPredicate(DbCommand command, string columnRef, IReadOnlyCollection<long> termIds);

    /// <summary>
    /// Largest number of term ids handed to <see cref="BuildTermIdPredicate"/> at once. Dialects that
    /// inline the ids override this to keep the statement under their maximum length. Also the most
    /// term rows one statement of the full document-frequency recount covers, so an override keeps that
    /// recount's statements bounded too; the unbounded default recounts in one statement.
    /// </summary>
    protected virtual int TermIdBatchSize => int.MaxValue;

    /// <summary>
    /// The longest metadata value, in UTF-8 bytes, the filter relation (<c>bm25_chunk_metadata</c>) stores as
    /// itself; a longer value is stored, and a filter value compared, as its SHA-256 digest. Null (the default)
    /// stores every value as itself. A backend whose index entries have a size limit sets this below it: a filter
    /// matches whole values only, so the digest answers the same question, and a value that does not fit would
    /// otherwise fail the whole write.
    /// </summary>
    protected virtual int? MaxStoredMetadataValueBytes => null;

    private const string MetadataValueDigestPrefix = "sha256:";

    /// <summary>The form <paramref name="value"/> takes in the filter relation - see <see cref="MaxStoredMetadataValueBytes"/>.</summary>
    private string StoredMetadataValue(string value)
    {
        if (MaxStoredMetadataValueBytes is not { } max || Encoding.UTF8.GetByteCount(value) <= max)
            return value;

        return MetadataValueDigestPrefix
            + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// Hook for backend setup that must happen once, inside the initialization lock, before the
    /// schema is created. Does nothing by default.
    /// </summary>
    protected virtual Task OnInitializingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Runs <see cref="SchemaDdl"/> on <paramref name="connection"/>. A dialect overrides this to serialize schema
    /// creation across processes that start against the same database at once — "if not exists" DDL is not safe to
    /// run concurrently on every backend.
    /// </summary>
    protected virtual async Task ExecuteSchemaDdlAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaDdl;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Creates the keyword index schema if it is not there yet. Every operation does this lazily;
    /// calling it up front is what lets Build() offer the same contract as every other component —
    /// once it returns, the tables exist.
    /// </summary>
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => EnsureInitializedAsync(cancellationToken);

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            await OnInitializingAsync(cancellationToken).ConfigureAwait(false);

            await using (var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
            {
                await ExecuteSchemaDdlAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            await RunWithConcurrencyRetryAsync(
                () => ReconcileDocumentFrequencyFieldsAsync(cancellationToken), cancellationToken).ConfigureAwait(false);

            _initialized = true;
            LogServiceInitialized(Logger, BackendName);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Runs on every connection right after it opens, before any command — the place for per-connection settings.
    /// </summary>
    protected virtual Task OnConnectionOpenedAsync(DbConnection connection, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>Creates and opens a connection.</summary>
    protected async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await OnConnectionOpenedAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    #endregion

    #region Search

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeywordSearchResult>> SearchAsync(
        string query,
        KeywordSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        options ??= new KeywordSearchOptions();

        var terms = Tokenize(query).ToList();
        if (terms.Count == 0)
            return [];

        // Expanded once, outside the per-term loop: an unfilterable entry is a caller error and must
        // surface before any work is done, not once per term.
        var metadataFilter = options.MetadataFilter is { Count: > 0 }
            ? KeywordMetadataFilter.Expand(options.MetadataFilter, nameof(options))
            : null;

        LogSearchStarted(Logger, query, terms.Count);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var totalDocs = await GetStatValueAsync(connection, "total_documents", cancellationToken).ConfigureAwait(false);
        var avgDocLength = await GetStatValueAsync(connection, "avg_doc_length", cancellationToken).ConfigureAwait(false);

        if (totalDocs == 0 || avgDocLength == 0)
            return [];

        var fieldAverageLengths = await LoadFieldAverageLengthsAsync(connection, cancellationToken).ConfigureAwait(false);
        var scores = new Dictionary<string, ScoreAccumulator>(StringComparer.Ordinal);

        // A wide filter — a whole vault's document ids, say — is resolved to its chunk set once and the
        // postings are tested against that set as they are read. Carried in SQL it is bound and
        // resolved again by every statement below, two per query term, and its value list can outgrow
        // the backend's parameter limit. A narrow filter stays in SQL, where it keeps the postings of a
        // common term from being read at all. Scoring and the top-N cut happen after either, so the
        // results are the same.
        HashSet<string>? acceptedChunks = null;
        if (metadataFilter is not null && metadataFilter.Sum(f => f.Accepted.Count) > WideFilterThreshold)
        {
            acceptedChunks = await ResolveAcceptedChunksAsync(connection, metadataFilter, cancellationToken).ConfigureAwait(false);
            if (acceptedChunks.Count == 0)
                return [];
            metadataFilter = null;
        }

        foreach (var term in terms)
        {
            var (termId, documentFrequency) = await TryGetTermAsync(connection, term, cancellationToken).ConfigureAwait(false);
            if (termId is null)
                continue;

            var idf = ComputeIdf(totalDocs, documentFrequency);

            // Everything the index knows about this term per chunk — the body posting and any field
            // postings — is collected first and scored once, so a chunk matched in its title and its
            // body gets one BM25F score rather than two BM25 scores added together.
            var evidence = new Dictionary<string, TermEvidence>(StringComparer.Ordinal);

            await using (var postingCmd = connection.CreateCommand())
            {
                var (scopeJoin, metadataPredicate) = BuildScope(postingCmd, "p", options, metadataFilter);
                postingCmd.CommandText =
                    "SELECT p.chunk_id, p.term_frequency, p.document_length " +
                    $"FROM bm25_postings p{scopeJoin} WHERE p.term_id = @termId{metadataPredicate}";
                AddParameter(postingCmd, "@termId", termId.Value);

                await using var postingReader = await postingCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await postingReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var chunkId = postingReader.GetString(0);
                    if (acceptedChunks is not null && !acceptedChunks.Contains(chunkId))
                        continue;
                    evidence[chunkId] = new TermEvidence
                    {
                        BodyTf = postingReader.GetInt32(1),
                        DocumentLength = postingReader.GetInt32(2),
                    };
                }
            }

            if (Fields.Fields.Count > 0)
            {
                await using var fieldCmd = connection.CreateCommand();
                var (scopeJoin, metadataPredicate) = BuildScope(fieldCmd, "f", options, metadataFilter);
                var fieldPredicate = BuildFieldPredicate(fieldCmd, "f.field");
                fieldCmd.CommandText =
                    "SELECT f.chunk_id, f.field, f.term_frequency, f.field_length, f.document_length " +
                    $"FROM bm25_field_postings f{scopeJoin} WHERE f.term_id = @termId AND {fieldPredicate}{metadataPredicate}";
                AddParameter(fieldCmd, "@termId", termId.Value);

                await using var fieldReader = await fieldCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await fieldReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var chunkId = fieldReader.GetString(0);
                    if (acceptedChunks is not null && !acceptedChunks.Contains(chunkId))
                        continue;
                    var field = fieldReader.GetString(1);
                    var fieldTf = fieldReader.GetInt32(2);
                    var fieldLength = fieldReader.GetInt32(3);
                    var documentLength = fieldReader.GetInt32(4);

                    var weight = FieldWeight(field);
                    var averageLength = fieldAverageLengths.GetValueOrDefault(field);
                    var lengthNorm = averageLength > 0
                        ? 1 - options.B + options.B * (fieldLength / averageLength)
                        : 1;

                    if (!evidence.TryGetValue(chunkId, out var chunkEvidence))
                    {
                        chunkEvidence = new TermEvidence { DocumentLength = documentLength };
                        evidence[chunkId] = chunkEvidence;
                    }

                    chunkEvidence.FieldContribution += weight * fieldTf / lengthNorm;
                    chunkEvidence.FieldTf += fieldTf;
                }
            }

            foreach (var (chunkId, chunkEvidence) in evidence)
            {
                double bm25Score;
                if (chunkEvidence.FieldContribution == 0)
                {
                    // Body-only evidence keeps the classic BM25 expression verbatim, so an index with no
                    // field postings — every index built before fields existed — scores bit-for-bit as
                    // it always did. The BM25F branch below is algebraically the same for a body-only
                    // chunk, but algebra is not floating point.
                    var tf = chunkEvidence.BodyTf;
                    var normalizedTf = tf * (options.K1 + 1) /
                        (tf + options.K1 * (1 - options.B + options.B * (chunkEvidence.DocumentLength / avgDocLength)));
                    bm25Score = idf * normalizedTf;
                }
                else
                {
                    // BM25F: each field's term frequency is length-normalized against that field's own
                    // average, weighted, and summed into one frequency that is then saturated once.
                    var bodyContribution = chunkEvidence.BodyTf == 0
                        ? 0
                        : chunkEvidence.BodyTf / (1 - options.B + options.B * (chunkEvidence.DocumentLength / avgDocLength));
                    var combinedTf = bodyContribution + chunkEvidence.FieldContribution;
                    bm25Score = idf * combinedTf * (options.K1 + 1) / (combinedTf + options.K1);
                }

                var reportedTf = chunkEvidence.BodyTf > 0 ? chunkEvidence.BodyTf : chunkEvidence.FieldTf;

                if (scores.TryGetValue(chunkId, out var existing))
                {
                    existing.MatchedTerms.Add(term);
                    existing.TermFrequencies[term] = reportedTf;
                    scores[chunkId] = existing with { Score = existing.Score + bm25Score, DocumentLength = chunkEvidence.DocumentLength };
                }
                else
                {
                    scores[chunkId] = new ScoreAccumulator(
                        bm25Score,
                        [term],
                        new Dictionary<string, int>(StringComparer.Ordinal) { [term] = reportedTf },
                        chunkEvidence.DocumentLength);
                }
            }
        }

        // Truncate before loading payloads — the chunk table is read for the results only.
        var rankedChunkIds = scores
            .Where(pair => pair.Value.Score >= options.MinScore)
            .OrderByDescending(pair => pair.Value.Score)
            .Take(options.MaxResults)
            .Select(pair => pair.Key)
            .ToList();

        if (rankedChunkIds.Count == 0)
            return [];

        var chunks = await LoadChunksAsync(connection, rankedChunkIds, cancellationToken).ConfigureAwait(false);

        var results = new List<KeywordSearchResult>();
        foreach (var chunkId in rankedChunkIds)
        {
            if (!chunks.TryGetValue(chunkId, out var chunk) || !scores.TryGetValue(chunkId, out var score))
                continue;

            results.Add(new KeywordSearchResult
            {
                Chunk = chunk,
                Score = score.Score,
                MatchedTerms = score.MatchedTerms.Distinct(StringComparer.Ordinal).ToList(),
                TermFrequencies = score.TermFrequencies,
                DocumentLength = score.DocumentLength
            });
        }

        LogSearchCompleted(Logger, results.Count);
        return results;
    }

    /// <summary>
    /// Smoothed inverse document frequency, matching the form Lucene and every mainstream BM25
    /// implementation use. The unsmoothed Robertson variant goes negative once a term appears in more
    /// than half the documents, which silently discards the most common domain vocabulary.
    /// </summary>
    private static double ComputeIdf(double totalDocuments, int documentFrequency)
        => Math.Log(1 + (totalDocuments - documentFrequency + 0.5) / (documentFrequency + 0.5));

    private static async Task<(long? TermId, int DocumentFrequency)> TryGetTermAsync(
        DbConnection connection,
        string term,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, document_frequency FROM bm25_terms WHERE term = @term";
        AddParameter(command, "@term", NormalizeTerm(term));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (null, 0);

        return (reader.GetInt64(0), reader.GetInt32(1));
    }

    private static async Task<Dictionary<string, DocumentChunk>> LoadChunksAsync(
        DbConnection connection,
        List<string> chunkIds,
        CancellationToken cancellationToken)
    {
        var chunks = new Dictionary<string, DocumentChunk>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        var placeholders = new List<string>(chunkIds.Count);
        for (var i = 0; i < chunkIds.Count; i++)
        {
            var name = $"@id{i}";
            placeholders.Add(name);
            AddParameter(command, name, chunkIds[i]);
        }

        command.CommandText =
            "SELECT chunk_id, document_id, chunk_index, content, token_count, metadata " +
            $"FROM bm25_chunks WHERE chunk_id IN ({string.Join(",", placeholders)})";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunk = new DocumentChunk
            {
                Id = reader.GetString(0),
                DocumentId = reader.GetString(1),
                ChunkIndex = reader.GetInt32(2),
                Content = reader.GetString(3),
                TokenCount = reader.GetInt32(4)
            };

            var metadataJson = reader.IsDBNull(5) ? null : reader.GetString(5);
            if (!string.IsNullOrEmpty(metadataJson))
            {
                chunk.Metadata = MetadataValues.Deserialize(metadataJson);
            }

            chunks[chunk.Id] = chunk;
        }

        return chunks;
    }

    private sealed record ScoreAccumulator(
        double Score,
        List<string> MatchedTerms,
        Dictionary<string, int> TermFrequencies,
        int DocumentLength);

    /// <summary>What one term contributes to one chunk before saturation: the body posting and the field postings.</summary>
    private sealed class TermEvidence
    {
        public int BodyTf;
        public int DocumentLength;
        public double FieldContribution;
        public int FieldTf;
    }

    /// <summary>Accepted values above which a metadata filter is resolved once instead of carried in SQL.</summary>
    private const int WideFilterThreshold = 256;

    /// <summary>Values bound per statement while resolving a wide filter — under every backend's parameter limit.</summary>
    private const int FilterValueBatchSize = 900;

    /// <summary>
    /// The chunks a metadata filter accepts: for each key, the chunks carrying any accepted value;
    /// across keys, the intersection. <see cref="FilterKeys.DocumentId"/> reads the chunk's own document id,
    /// as <see cref="BuildMetadataPredicate"/> does for a narrow filter.
    /// </summary>
    private async Task<HashSet<string>> ResolveAcceptedChunksAsync(
        DbConnection connection,
        IReadOnlyList<(string Key, IReadOnlyList<string> Accepted)> metadataFilter,
        CancellationToken cancellationToken)
    {
        HashSet<string>? accepted = null;
        foreach (var (key, values) in metadataFilter)
        {
            var documentScope = string.Equals(key, FilterKeys.DocumentId, StringComparison.Ordinal);
            var forKey = new HashSet<string>(StringComparer.Ordinal);
            foreach (var batch in values.Chunk(FilterValueBatchSize))
            {
                await using var command = connection.CreateCommand();
                var parameters = new string[batch.Length];
                for (var v = 0; v < batch.Length; v++)
                {
                    parameters[v] = $"@mfVal{v}";
                    AddParameter(command, parameters[v], documentScope ? batch[v] : StoredMetadataValue(batch[v]));
                }

                if (documentScope)
                {
                    command.CommandText =
                        $"SELECT chunk_id FROM bm25_chunks WHERE document_id IN ({string.Join(", ", parameters)})";
                }
                else
                {
                    AddParameter(command, "@mfKey", key);
                    command.CommandText =
                        $"SELECT chunk_id FROM bm25_chunk_metadata WHERE meta_key = @mfKey AND meta_value IN ({string.Join(", ", parameters)})";
                }
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    forKey.Add(reader.GetString(0));
                }
            }

            if (accepted is null)
                accepted = forKey;
            else
                accepted.IntersectWith(forKey);

            if (accepted.Count == 0)
                break;
        }

        return accepted ?? [];
    }

    /// <summary>
    /// The document scope and metadata filter a postings read has to carry, as a JOIN fragment and a
    /// predicate fragment against the given table alias. Both the body read and the field read go
    /// through here: a scoped query must not surface a title hit from another document.
    /// </summary>
    private (string ScopeJoin, string MetadataPredicate) BuildScope(
        DbCommand command,
        string alias,
        KeywordSearchOptions options,
        IReadOnlyList<(string Key, IReadOnlyList<string> Accepted)>? metadataFilter)
    {
        // A document-scoped search restricts the postings themselves rather than the results, so
        // the top-N cut still returns N matches inside that document instead of whatever survives
        // filtering the global top N. Same for the metadata filter: the condition restricts the
        // postings, so MaxResults selects the top N *within* the scope.
        var scopeJoin = string.Empty;
        if (!string.IsNullOrWhiteSpace(options.DocumentIdFilter))
        {
            scopeJoin = $" JOIN bm25_chunks c ON c.chunk_id = {alias}.chunk_id AND c.document_id = @documentIdFilter";
            AddParameter(command, "@documentIdFilter", options.DocumentIdFilter);
        }

        var metadataPredicate = metadataFilter is null
            ? string.Empty
            : BuildMetadataPredicate(command, $"{alias}.chunk_id", metadataFilter);

        return (scopeJoin, metadataPredicate);
    }

    /// <summary>
    /// Restricts a field read to the fields currently configured. Rows a previous configuration
    /// wrote for a field that is no longer listed stay in the table until the chunk is re-indexed,
    /// and must not score.
    /// </summary>
    private string BuildFieldPredicate(DbCommand command, string columnRef)
    {
        var names = new string[Fields.Fields.Count];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = $"@field{i}";
            AddParameter(command, names[i], Fields.Fields[i].MetadataKey);
        }

        return $"{columnRef} IN ({string.Join(", ", names)})";
    }

    private double FieldWeight(string field)
    {
        foreach (var candidate in Fields.Fields)
        {
            if (string.Equals(candidate.MetadataKey, field, StringComparison.Ordinal))
                return candidate.Weight;
        }

        return 0;
    }

    private static string FieldAverageLengthKey(string field) => "avg_field_length:" + field;

    private async Task<Dictionary<string, double>> LoadFieldAverageLengthsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var averages = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var field in Fields.Fields)
        {
            averages[field.MetadataKey] = await GetStatValueAsync(
                connection, FieldAverageLengthKey(field.MetadataKey), cancellationToken).ConfigureAwait(false);
        }

        return averages;
    }

    #endregion

    #region Index management

    /// <inheritdoc />
    public async Task IndexChunkAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await IndexChunksAsync([chunk], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Indexes every chunk under a single transaction. Committing per chunk costs an fsync each,
    /// which is the difference between seconds and minutes on a document set of a few thousand chunks.
    /// </summary>
    public async Task IndexChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var chunkList = chunks.Where(c => c is not null).ToList();
        if (chunkList.Count == 0)
            return;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        // Tokenized once, outside the retry: the token stream does not change between attempts, and
        // re-deriving it would pay the whole cost again for a failure that was purely a lock cycle.
        // A chunk with no body terms is not indexed, fields or not: the body is what the index is of,
        // and a title-only row would be a chunk the store cannot show a snippet for. It still goes
        // through the transaction, because the rows a previous version of it left must be removed.
        var tokenized = chunkList
            .Select(c => (Chunk: c, Terms: Tokenize(c.Content).ToList(), FieldTerms: TokenizeFields(c)))
            .ToList();

        await RunWithConcurrencyRetryAsync(
            () => IndexTokenizedChunksAsync(tokenized, [], cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One transaction: the listed documents' previous chunks are removed and the new ones indexed together, with
    /// every term row both sides touch acquired in the same sorted pass as indexing and deletion.
    /// </remarks>
    public async Task ReplaceDocumentsAsync(
        IReadOnlyCollection<string> documentIds,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(chunks);

        var replaced = documentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var tokenized = chunks
            .Where(c => c is not null)
            .Select(c => (Chunk: c, Terms: Tokenize(c.Content).ToList(), FieldTerms: TokenizeFields(c)))
            .ToList();
        if (replaced.Count == 0 && tokenized.Count == 0)
            return;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await RunWithConcurrencyRetryAsync(
            () => IndexTokenizedChunksAsync(tokenized, replaced, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one write transaction, retrying it whole when it loses a lock conflict.
    /// </summary>
    /// <remarks>
    /// A serialization failure is the expected outcome of concurrent writers, not a defect in the batch:
    /// the whole transaction rolled back, so retrying it is safe and is the only thing that can succeed.
    /// Every transaction that writes term rows goes through here - indexing and deletion alike. Deletion
    /// used to run without it, so a re-index that removed a document's previous chunks while another
    /// document was being indexed failed its caller outright.
    /// </remarks>
    private bool IsRetryableWriteFailure(Exception exception)
        => exception is TermRowRemovedException
            || (exception is DbException db && (IsTransientConcurrencyFailure(db) || IsTermRowRemovedFailure(db)));

    private async Task RunWithConcurrencyRetryAsync(Func<Task> transaction, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await transaction().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsRetryableWriteFailure(ex) && attempt < MaxConcurrencyRetries)
            {
                attempt++;
                if (ex is DbException db && IsTransientConcurrencyFailure(db))
                    LogConcurrencyRetry(Logger, attempt, db.SqlState ?? "unknown");
                else
                    LogTermRowRemovedRetry(Logger, attempt);
                await Task.Delay(ConcurrencyRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The terms of each configured field for a chunk: the field's metadata value(s), projected to
    /// text the same way the filter dimension projects them, then analyzed by the one analyzer the
    /// body uses. A field the chunk does not carry produces nothing.
    /// </summary>
    private List<(string Field, List<string> Terms)> TokenizeFields(DocumentChunk chunk)
    {
        var result = new List<(string Field, List<string> Terms)>();
        if (Fields.Fields.Count == 0 || chunk.Metadata is not { Count: > 0 })
            return result;

        var projected = KeywordMetadataFilter.Project(chunk.Metadata).ToList();
        foreach (var field in Fields.Fields)
        {
            var text = string.Join(
                " ",
                projected.Where(row => string.Equals(row.Key, field.MetadataKey, StringComparison.Ordinal)).Select(row => row.Value));
            if (text.Length == 0)
                continue;

            var terms = Tokenize(text).ToList();
            if (terms.Count > 0)
                result.Add((field.MetadataKey, terms));
        }

        return result;
    }

    /// <summary>Attempts one transaction. Retried as a whole by the caller on a serialization failure.</summary>
    private async Task IndexTokenizedChunksAsync(
        IReadOnlyList<(DocumentChunk Chunk, List<string> Terms, List<(string Field, List<string> Terms)> FieldTerms)> tokenized,
        IReadOnlyList<string> replacedDocumentIds,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var affectedTermIds = new HashSet<long>();
            var statistics = new StatisticsDelta();
            var frequencies = new DocumentFrequencyDelta();

            // Chunks of a replaced document that this batch does not write again are removed in the same transaction.
            var written = new HashSet<string>(tokenized.Select(t => t.Chunk.Id), StringComparer.Ordinal);
            var stale = new List<string>();
            foreach (var documentId in replacedDocumentIds)
            {
                stale.AddRange((await ReadChunkIdsForDocumentAsync(connection, documentId, cancellationToken).ConfigureAwait(false))
                    .Where(id => !written.Contains(id)));
            }

            await LockChunksAsync(connection, [.. written, .. stale], cancellationToken).ConfigureAwait(false);

            // Ids for every term the batch writes, body and fields alike, taken without locking a term row. The rows
            // the replaced chunks held are learned from the postings their deletion removes; all of them are locked
            // only at the end, just before their document frequency moves (ApplyDocumentFrequencyDeltaAsync).
            var termIds = await AcquireTermIdsAsync(
                connection,
                TermAcquisitionOrder(tokenized
                    .Select(t => (IEnumerable<string>)t.Terms)
                    .Concat(tokenized.SelectMany(t => t.FieldTerms.Select(f => (IEnumerable<string>)f.Terms)))),
                cancellationToken).ConfigureAwait(false);

            foreach (var id in termIds.Values)
                affectedTermIds.Add(id);

            await DeleteChunkRowsAsync(connection, stale, affectedTermIds, statistics, frequencies, cancellationToken).ConfigureAwait(false);

            var indexedChunks = 0;
            foreach (var (chunk, terms, fieldTerms) in tokenized)
            {
                if (await IndexChunkCoreAsync(connection, chunk, terms, fieldTerms, termIds, affectedTermIds, statistics, frequencies, cancellationToken).ConfigureAwait(false))
                    indexedChunks++;
            }

            await ApplyDocumentFrequencyDeltaAsync(connection, frequencies, affectedTermIds, cancellationToken).ConfigureAwait(false);
            await ApplyStatisticsDeltaAsync(connection, statistics, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            LogChunksIndexed(Logger, indexedChunks);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// The order in which a transaction registers its terms: every distinct normalized term of the batch,
    /// sorted ordinally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inserting a term row another writer has inserted but not yet committed waits for that writer, so two
    /// registrations inserting shared new words in different orders could wait on each other in both
    /// directions. One total order removes the cycle. This is a pure function so the rule is held by tests that
    /// need no database.
    /// </para>
    /// <para>
    /// Term rows are not locked here. A transaction used to take every term row it would write at its start
    /// and hold the locks to commit; any other writer sharing a word then waited for the whole batch - with a
    /// few large documents in flight, longer than the command timeout. The rows are now locked only at the end,
    /// in id order, just before document frequency moves (<see cref="ApplyDocumentFrequencyDeltaAsync"/>).
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> TermAcquisitionOrder(IEnumerable<IEnumerable<string>> perChunkTerms)
        => perChunkTerms
            .SelectMany(terms => terms)
            .Select(NormalizeTerm)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Makes sure every term in <paramref name="orderedTerms"/> has a row and returns the ids by normalized
    /// term, without locking any term row.
    /// </summary>
    /// <remarks>
    /// Where <see cref="RegistersTermsInOwnTransaction"/> holds, new rows are committed before this returns, so a
    /// writer sharing a new word waits for this short registration at most, never for the caller's transaction.
    /// A term row can be removed by another writer's zero-frequency cleanup between registration and the end of
    /// the caller's transaction; <see cref="LockTermRowsAsync"/> detects that and the transaction is retried.
    /// The ids are read on the caller's transaction, which must see rows committed after it began (READ COMMITTED, the
    /// default): under REPEATABLE READ the new rows would stay invisible and every attempt would be retried away.
    /// </remarks>
    private async Task<Dictionary<string, long>> AcquireTermIdsAsync(
        DbConnection connection,
        IReadOnlyList<string> orderedTerms,
        CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, long>(orderedTerms.Count, StringComparer.Ordinal);
        if (orderedTerms.Count == 0)
            return ids;

        if (RegistersTermsInOwnTransaction)
        {
            await using var registration = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await registration.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await InsertTermsAsync(registration, orderedTerms, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await InsertTermsAsync(connection, orderedTerms, cancellationToken).ConfigureAwait(false);
        }

        foreach (var batch in orderedTerms.Chunk(LookupBatchSize))
        {
            await using var command = connection.CreateCommand();
            var predicate = BuildTermTextPredicate(command, "term", batch);
            command.CommandText = $"SELECT term, id FROM bm25_terms WHERE {predicate}";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids[reader.GetString(0)] = reader.GetInt64(1);
        }

        // Registered a moment ago and gone already: a concurrent cleanup removed a row no chunk held yet.
        if (ids.Count != orderedTerms.Count)
            throw new TermRowRemovedException();

        return ids;
    }

    /// <summary>
    /// Inserts the terms that have no row yet, in the given order, without locking existing rows. The default runs
    /// <see cref="InsertTermIfAbsentSql"/> once per term; a dialect that can insert a whole ordered set in one
    /// statement overrides this, which keeps a registration short enough that a writer waiting on it barely waits.
    /// </summary>
    protected virtual async Task InsertTermsAsync(DbConnection connection, IReadOnlyList<string> orderedTerms, CancellationToken cancellationToken)
    {
        foreach (var term in orderedTerms)
        {
            await using var termCmd = connection.CreateCommand();
            termCmd.CommandText = InsertTermIfAbsentSql;
            AddParameter(termCmd, "@term", term);
            await termCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes the term rows a transaction is about to update, in id order, and checks that every term it wrote a
    /// posting for still has its row.
    /// </summary>
    /// <remarks>
    /// Every writer locks its term rows here and nowhere else, all in one order, so writers sharing vocabulary
    /// wait for each other only for the few statements between this and commit, and never in a cycle.
    /// </remarks>
    private async Task LockTermRowsAsync(
        DbConnection connection,
        HashSet<long> affectedTermIds,
        DocumentFrequencyDelta frequencies,
        CancellationToken cancellationToken)
    {
        var present = new HashSet<long>();
        foreach (var batch in affectedTermIds.Order().Chunk(LookupBatchSize))
        {
            await using var command = connection.CreateCommand();
            var predicate = BuildTermIdPredicate(command, "bm25_terms.id", batch);
            command.CommandText = $"SELECT id FROM bm25_terms WHERE {predicate} ORDER BY id {TermRowLockClause}";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                present.Add(reader.GetInt64(0));
        }

        if (frequencies.WrittenTermIds().Any(id => !present.Contains(id)))
            throw new TermRowRemovedException();
    }

    /// <summary>Largest batch of terms or ids a lookup statement carries.</summary>
    private int LookupBatchSize => Math.Min(TermIdBatchSize, 10_000);

    /// <summary>
    /// A term row the transaction depends on was removed by a concurrent writer's zero-frequency cleanup. The
    /// transaction is retried, which registers the term again.
    /// </summary>
    private sealed class TermRowRemovedException : Exception
    {
        public TermRowRemovedException()
            : base("A keyword index term row was removed by a concurrent writer before this transaction used it.")
        {
        }
    }

    /// <summary>Attempts allowed after the first, when a transaction loses a lock cycle.</summary>
    private const int MaxConcurrencyRetries = 4;

    private static TimeSpan ConcurrencyRetryDelay(int attempt)
        // Backing off by attempt, with jitter: two transactions that deadlocked are by definition
        // running at the same time, and retrying both immediately reproduces the collision.
        => TimeSpan.FromMilliseconds((25 * attempt) + Random.Shared.Next(0, 25));

    /// <summary>
    /// Whether a database failure is a concurrency conflict that a retry can resolve, rather than a
    /// defect in the statement or the data.
    /// </summary>
    /// <remarks>
    /// The base implementation recognizes the SQL-standard classes every backend here reports:
    /// <c>40P01</c> (deadlock detected) and <c>40001</c> (serialization failure). A dialect whose
    /// driver signals contention differently overrides this.
    /// </remarks>
    protected virtual bool IsTransientConcurrencyFailure(DbException exception)
        => exception.SqlState is "40P01" or "40001";

    /// <summary>
    /// Writes one chunk's payload and postings. Re-indexing an existing chunk replaces its postings
    /// wholesale rather than layering new ones on top, so document frequency cannot drift.
    /// </summary>
    private async Task<bool> IndexChunkCoreAsync(
        DbConnection connection,
        DocumentChunk chunk,
        List<string> terms,
        List<(string Field, List<string> Terms)> fieldTerms,
        Dictionary<string, long> termIds,
        HashSet<long> affectedTermIds,
        StatisticsDelta statistics,
        DocumentFrequencyDelta frequencies,
        CancellationToken cancellationToken)
    {
        // The previous rows go whatever the new content is. Returning before this when the new content
        // analyzes to no terms left the old postings in place, so text the chunk no longer holds kept
        // matching.
        await DeletePostingsAsync(connection, chunk.Id, affectedTermIds, statistics, frequencies, cancellationToken).ConfigureAwait(false);

        if (terms.Count == 0)
        {
            await DeleteChunkPayloadAsync(connection, chunk.Id, cancellationToken).ConfigureAwait(false);
            return false;
        }

        statistics.AddDocument(terms.Count);

        await using (var chunkCmd = connection.CreateCommand())
        {
            chunkCmd.CommandText = UpsertChunkSql;
            AddParameter(chunkCmd, "@chunkId", chunk.Id);
            AddParameter(chunkCmd, "@documentId", chunk.DocumentId);
            AddParameter(chunkCmd, "@chunkIndex", chunk.ChunkIndex);
            AddParameter(chunkCmd, "@content", chunk.Content);
            AddParameter(chunkCmd, "@tokenCount", chunk.TokenCount);
            AddParameter(
                chunkCmd,
                "@metadata",
                chunk.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(chunk.Metadata) : null);
            await chunkCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await WriteFilterableMetadataAsync(connection, chunk, cancellationToken).ConfigureAwait(false);

        var termFrequencies = terms
            .GroupBy(t => t, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var (term, frequency) in termFrequencies)
        {
            // The row was already acquired, in the batch's sorted order, before any chunk was
            // written. Upserting it here instead is what let two transactions take the same rows in
            // opposite orders.
            var termId = termIds[NormalizeTerm(term)];

            affectedTermIds.Add(termId);
            frequencies.Written(termId, chunk.Id);

            await using var postingCmd = connection.CreateCommand();
            postingCmd.CommandText = UpsertPostingSql;
            AddParameter(postingCmd, "@termId", termId);
            AddParameter(postingCmd, "@chunkId", chunk.Id);
            AddParameter(postingCmd, "@tf", frequency);
            AddParameter(postingCmd, "@docLen", terms.Count);
            await postingCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (field, fieldTermList) in fieldTerms)
        {
            var fieldFrequencies = fieldTermList
                .GroupBy(t => t, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            if (fieldFrequencies.Count > 0)
                statistics.AddField(field, fieldTermList.Count);

            foreach (var (term, frequency) in fieldFrequencies)
            {
                var termId = termIds[NormalizeTerm(term)];
                affectedTermIds.Add(termId);
                frequencies.Written(termId, chunk.Id);

                await using var fieldCmd = connection.CreateCommand();
                fieldCmd.CommandText = UpsertFieldPostingSql;
                AddParameter(fieldCmd, "@termId", termId);
                AddParameter(fieldCmd, "@chunkId", chunk.Id);
                AddParameter(fieldCmd, "@field", field);
                AddParameter(fieldCmd, "@tf", frequency);
                AddParameter(fieldCmd, "@fieldLen", fieldTermList.Count);
                AddParameter(fieldCmd, "@docLen", terms.Count);
                await fieldCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        LogChunkIndexed(Logger, chunk.Id, termFrequencies.Count);
        return true;
    }

    /// <inheritdoc />
    public async Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            return;

        await DeleteChunkSetAsync([chunkId], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteChunksAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);

        var ids = chunkIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
            return;

        await DeleteChunkSetAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the chunk's filterable metadata rows. Delete-then-insert rather than upsert: a key
    /// dropped from the metadata has to disappear from the filter dimension too, and an upsert keyed
    /// on the rows present would leave the removed one behind — the chunk would keep matching a
    /// filter it no longer satisfies.
    /// </summary>
    private async Task WriteFilterableMetadataAsync(
        DbConnection connection,
        DocumentChunk chunk,
        CancellationToken cancellationToken)
    {
        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.CommandText = "DELETE FROM bm25_chunk_metadata WHERE chunk_id = @chunkId";
            AddParameter(deleteCmd, "@chunkId", chunk.Id);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Distinct because a collection may repeat a value, and the row set is a membership fact -
        // "this chunk has this value for this key" is true once however many times it was written.
        var rows = KeywordMetadataFilter.Project(chunk.Metadata)
            .Select(row => (row.Key, Value: StoredMetadataValue(row.Value)))
            .Distinct()
            .ToList();
        if (rows.Count == 0)
            return;

        foreach (var (key, value) in rows)
        {
            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
                "INSERT INTO bm25_chunk_metadata (chunk_id, meta_key, meta_value) " +
                "VALUES (@chunkId, @key, @value)";
            AddParameter(insertCmd, "@chunkId", chunk.Id);
            AddParameter(insertCmd, "@key", key);
            AddParameter(insertCmd, "@value", value);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds an EXISTS predicate per filter entry against <c>bm25_chunk_metadata</c>, adding the
    /// parameters to <paramref name="command"/>. Entries are ANDed; the values within one entry are
    /// ORed, which is the match-any semantics the vector store's payload filter uses.
    /// </summary>
    /// <remarks>
    /// EXISTS rather than a join: a chunk can hold several values for one key, and a join would
    /// return it once per matching value, multiplying postings rows and with them the BM25 score.
    /// Scoring must not depend on how many metadata values a chunk happens to carry.
    /// </remarks>
    private string BuildMetadataPredicate(
        DbCommand command,
        string chunkIdColumnRef,
        IReadOnlyList<(string Key, IReadOnlyList<string> Accepted)> expanded)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < expanded.Count; i++)
        {
            var (key, accepted) = expanded[i];
            var documentScope = string.Equals(key, FilterKeys.DocumentId, StringComparison.Ordinal);
            var valueParams = new string[accepted.Count];
            for (var v = 0; v < accepted.Count; v++)
            {
                valueParams[v] = $"@mfVal{i}_{v}";
                AddParameter(command, valueParams[v], documentScope ? accepted[v] : StoredMetadataValue(accepted[v]));
            }

            // The document scope reads the chunk's own document_id column, never the metadata table:
            // a chunk indexed without a metadata copy of its document id must stay inside its
            // document's scope, exactly as the vector stores resolve the same key.
            if (documentScope)
            {
                builder.Append(" AND ")
                       .Append(chunkIdColumnRef)
                       .Append(" IN (SELECT dc")
                       .Append(i)
                       .Append(".chunk_id FROM bm25_chunks dc")
                       .Append(i)
                       .Append(" WHERE dc")
                       .Append(i)
                       .Append(".document_id IN (")
                       .Append(string.Join(", ", valueParams))
                       .Append("))");
                continue;
            }

            var keyParam = $"@mfKey{i}";
            AddParameter(command, keyParam, key);

            // An uncorrelated membership test, not EXISTS correlated on the chunk: the set of chunks the
            // filter accepts is the same for every posting row, so it is resolved once per statement
            // through the (meta_key, meta_value) index. The correlated form re-evaluated the value list
            // for every posting row — with a whole vault's document ids as the accepted values, a
            // search that takes 30 ms unfiltered took 9 s.
            builder.Append(" AND ")
                   .Append(chunkIdColumnRef)
                   .Append(" IN (SELECT mf")
                   .Append(i)
                   .Append(".chunk_id FROM bm25_chunk_metadata mf")
                   .Append(i)
                   .Append(" WHERE mf")
                   .Append(i)
                   .Append(".meta_key = ")
                   .Append(keyParam)
                   .Append(" AND mf")
                   .Append(i)
                   .Append(".meta_value IN (")
                   .Append(string.Join(", ", valueParams))
                   .Append("))");
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public async Task<int> DeleteByFilterAsync(
        IReadOnlyDictionary<string, object> filter,
        CancellationToken cancellationToken = default)
    {
        var expanded = KeywordMetadataFilter.Expand(filter, nameof(filter));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var chunkIds = new List<string>();
        await using (var selectCmd = connection.CreateCommand())
        {
            var predicate = BuildMetadataPredicate(selectCmd, "c.chunk_id", expanded);
            selectCmd.CommandText = $"SELECT c.chunk_id FROM bm25_chunks c WHERE 1 = 1{predicate}";

            await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                chunkIds.Add(reader.GetString(0));
            }
        }

        if (chunkIds.Count == 0)
            return 0;

        await DeleteChunkSetAsync(chunkIds, cancellationToken).ConfigureAwait(false);
        LogChunksDeletedByFilter(Logger, chunkIds.Count, expanded.Count);
        return chunkIds.Count;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One transaction: the document's rows are read, checked, and written back under the new ids — chunk row, filterable
    /// metadata rows, body postings and field postings — with the old rows removed, exactly as a delete followed by an
    /// index of the same text would leave them. The stored text is re-analyzed rather than the posting rows re-keyed,
    /// because a metadata update can change a scored field (the default fields are <c>title</c> and <c>file_name</c>),
    /// and field postings copied under a new id would keep matching the old value. Nothing is embedded: this index has
    /// no vectors.
    /// </remarks>
    public async Task<int> ReassignDocumentAsync(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates = null,
        CancellationToken cancellationToken = default)
    {
        DocumentReassignment.ValidateArguments(oldDocumentId, newDocumentId, chunkIdMap);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var moved = 0;
        await RunWithConcurrencyRetryAsync(
            async () => moved = await ReassignDocumentOnceAsync(
                oldDocumentId, newDocumentId, chunkIdMap, metadataUpdates, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        return moved;
    }

    /// <summary>Attempts one reassignment transaction. Retried as a whole on a serialization failure.</summary>
    private async Task<int> ReassignDocumentOnceAsync(
        string oldDocumentId,
        string newDocumentId,
        IReadOnlyDictionary<string, string> chunkIdMap,
        IReadOnlyDictionary<string, object?>? metadataUpdates,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var oldIds = await ReadChunkIdsForDocumentAsync(connection, oldDocumentId, cancellationToken).ConfigureAwait(false);
            if (oldIds.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }

            DocumentReassignment.EnsureCovered(oldIds, chunkIdMap);

            var targetIds = await ReadChunkIdsForDocumentAsync(connection, newDocumentId, cancellationToken).ConfigureAwait(false);
            if (targetIds.Count > 0)
                throw DocumentReassignment.TargetDocumentNotEmpty(newDocumentId);

            var taken = await LoadChunksAsync(connection, oldIds.Select(id => chunkIdMap[id]).ToList(), cancellationToken).ConfigureAwait(false);
            if (taken.Count > 0)
                throw DocumentReassignment.TargetChunkIdsTaken(taken.Keys.ToList());

            var stored = await LoadChunksAsync(connection, oldIds, cancellationToken).ConfigureAwait(false);
            var tokenized = stored.Values
                .Select(chunk =>
                {
                    var newId = chunkIdMap[chunk.Id];
                    var moved = new DocumentChunk
                    {
                        Id = newId,
                        DocumentId = newDocumentId,
                        ChunkIndex = chunk.ChunkIndex,
                        Content = chunk.Content,
                        TokenCount = chunk.TokenCount,
                        Metadata = DocumentReassignment.RewriteMetadata(
                            chunk.Metadata, oldDocumentId, newDocumentId, newId, chunkIdMap, metadataUpdates)
                    };
                    return (Chunk: moved, Terms: Tokenize(moved.Content).ToList(), FieldTerms: TokenizeFields(moved));
                })
                .ToList();

            await LockChunksAsync(connection, [.. oldIds, .. tokenized.Select(t => t.Chunk.Id)], cancellationToken).ConfigureAwait(false);

            // Same discipline as indexing: ids for the new chunks' terms without locking, the old chunks' terms from
            // the postings their deletion removes, every term row locked only at the end.
            var termIds = await AcquireTermIdsAsync(
                connection,
                TermAcquisitionOrder(tokenized
                    .Select(t => (IEnumerable<string>)t.Terms)
                    .Concat(tokenized.SelectMany(t => t.FieldTerms.Select(f => (IEnumerable<string>)f.Terms)))),
                cancellationToken).ConfigureAwait(false);
            var affectedTermIds = new HashSet<long>(termIds.Values);
            var statistics = new StatisticsDelta();
            var frequencies = new DocumentFrequencyDelta();

            await DeleteChunkRowsAsync(connection, oldIds, affectedTermIds, statistics, frequencies, cancellationToken).ConfigureAwait(false);

            var reindexed = 0;
            foreach (var (chunk, terms, fieldTerms) in tokenized)
            {
                if (await IndexChunkCoreAsync(connection, chunk, terms, fieldTerms, termIds, affectedTermIds, statistics, frequencies, cancellationToken).ConfigureAwait(false))
                    reindexed++;
            }

            await ApplyDocumentFrequencyDeltaAsync(connection, frequencies, affectedTermIds, cancellationToken).ConfigureAwait(false);
            await ApplyStatisticsDeltaAsync(connection, statistics, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            LogDocumentReassigned(Logger, reindexed, oldDocumentId, newDocumentId);
            return reindexed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetChunkIdsByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return [];

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadChunkIdsForDocumentAsync(connection, documentId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // The chunk ids come from this index's own table. Reading them from the vector store instead
        // made deletion depend on the vector rows still being there, which they are not once the
        // caller has already dropped them.
        var chunkIds = await ReadChunkIdsForDocumentAsync(connection, documentId, cancellationToken).ConfigureAwait(false);

        if (chunkIds.Count == 0)
            return;

        await DeleteChunkSetAsync(chunkIds, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<string>> ReadChunkIdsForDocumentAsync(
        DbConnection connection,
        string documentId,
        CancellationToken cancellationToken)
    {
        var chunkIds = new List<string>();
        await using var chunkIdsCmd = connection.CreateCommand();
        chunkIdsCmd.CommandText = "SELECT chunk_id FROM bm25_chunks WHERE document_id = @documentId";
        AddParameter(chunkIdsCmd, "@documentId", documentId);

        await using var reader = await chunkIdsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            chunkIds.Add(reader.GetString(0));
        }

        return chunkIds;
    }

    private async Task DeleteChunkSetAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await RunWithConcurrencyRetryAsync(
            () => DeleteChunksOnceAsync(chunkIds, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Attempts one deletion transaction. Retried as a whole on a serialization failure.</summary>
    private async Task DeleteChunksOnceAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // The term rows whose document frequency drops are the ones the deleted postings pointed at; the
            // deletion reports them, and they are locked with every other writer's at the end, in id order.
            await LockChunksAsync(connection, chunkIds, cancellationToken).ConfigureAwait(false);
            var affectedTermIds = new HashSet<long>();
            var statistics = new StatisticsDelta();
            var frequencies = new DocumentFrequencyDelta();

            await DeleteChunkRowsAsync(connection, chunkIds, affectedTermIds, statistics, frequencies, cancellationToken).ConfigureAwait(false);

            await ApplyDocumentFrequencyDeltaAsync(connection, frequencies, affectedTermIds, cancellationToken).ConfigureAwait(false);
            await ApplyStatisticsDeltaAsync(connection, statistics, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            foreach (var chunkId in chunkIds)
            {
                LogChunkDeleted(Logger, chunkId);
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Deletes the rows of <paramref name="chunkIds"/> on the open transaction, recording the postings removed and
    /// the lengths the statistics lose. The caller has already acquired the term rows those chunks held.
    /// </summary>
    private async Task DeleteChunkRowsAsync(
        DbConnection connection,
        IEnumerable<string> chunkIds,
        HashSet<long> affectedTermIds,
        StatisticsDelta statistics,
        DocumentFrequencyDelta frequencies,
        CancellationToken cancellationToken)
    {
        foreach (var chunkId in chunkIds)
        {
            await DeletePostingsAsync(connection, chunkId, affectedTermIds, statistics, frequencies, cancellationToken).ConfigureAwait(false);
            await DeleteChunkPayloadAsync(connection, chunkId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes a chunk's body and field postings, recording what the deletion actually removed: each
    /// (term, chunk) pair for document frequency, the term ids for the zero-frequency cleanup, and the
    /// stored lengths for the statistics.
    /// </summary>
    /// <remarks>
    /// The removed rows come from the deletion itself (<c>DELETE … RETURNING</c>), not from a read before it. Under
    /// concurrent writers of the same chunk a prior read can see rows another transaction is about to delete, or miss
    /// rows it has just committed; the rows a <c>DELETE</c> reports are the ones it removed once it held their locks,
    /// so the delta built from them is exact.
    /// </remarks>
    private async Task DeletePostingsAsync(
        DbConnection connection,
        string chunkId,
        HashSet<long> affectedTermIds,
        StatisticsDelta statistics,
        DocumentFrequencyDelta frequencies,
        CancellationToken cancellationToken)
    {
        long? documentLength = null;
        await using (var bodyCmd = connection.CreateCommand())
        {
            bodyCmd.CommandText = "DELETE FROM bm25_postings WHERE chunk_id = @chunkId RETURNING term_id, document_length";
            AddParameter(bodyCmd, "@chunkId", chunkId);
            await using var reader = await bodyCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var termId = reader.GetInt64(0);
                affectedTermIds.Add(termId);
                frequencies.Removed(termId, chunkId);
                var length = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                documentLength = documentLength is null ? length : Math.Max(documentLength.Value, length);
            }
        }

        if (documentLength is not null)
            statistics.RemoveDocument(documentLength.Value);

        // Field rows count toward document frequency only for the fields configured now, the same rows the full
        // recount reads. Rows left behind by a field dropped from the configuration are deleted all the same, and
        // still take their lengths out of that field's statistics.
        var fieldLengths = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await using (var fieldCmd = connection.CreateCommand())
        {
            fieldCmd.CommandText = "DELETE FROM bm25_field_postings WHERE chunk_id = @chunkId RETURNING term_id, field, field_length";
            AddParameter(fieldCmd, "@chunkId", chunkId);
            await using var reader = await fieldCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var termId = reader.GetInt64(0);
                var field = reader.GetString(1);
                affectedTermIds.Add(termId);
                if (_configuredFields.Contains(field))
                    frequencies.Removed(termId, chunkId);
                var length = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture);
                fieldLengths[field] = fieldLengths.TryGetValue(field, out var current) ? Math.Max(current, length) : length;
            }
        }

        foreach (var (field, length) in fieldLengths)
            statistics.RemoveField(field, length);
    }

    /// <summary>Deletes a chunk's payload row and its filterable metadata rows.</summary>
    private static async Task DeleteChunkPayloadAsync(DbConnection connection, string chunkId, CancellationToken cancellationToken)
    {
        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = """
            DELETE FROM bm25_chunk_metadata WHERE chunk_id = @chunkId;
            DELETE FROM bm25_chunks WHERE chunk_id = @chunkId;
            """;
        AddParameter(deleteCmd, "@chunkId", chunkId);
        await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What one write transaction changes about document frequency: for every (term, chunk) pair it deleted or
    /// wrote, whether the chunk held the term before the transaction and whether it holds it now.
    /// </summary>
    /// <remarks>
    /// Document frequency counts distinct chunks that hold a term in the body or in a configured field, so the
    /// unit is the pair, not the posting row: a chunk with the term in its body and two fields is one document.
    /// A pair first seen as deleted existed before the transaction (every write deletes a chunk's rows before it
    /// writes them); a pair first seen as written did not. A chunk replaced with a term it already held nets to
    /// zero, one that lost the term to minus one, one that gained it to plus one - also when the same chunk is
    /// written more than once in a transaction.
    /// </remarks>
    private sealed class DocumentFrequencyDelta
    {
        private readonly Dictionary<(long TermId, string ChunkId), (bool Before, bool After)> _pairs = [];

        public void Removed(long termId, string chunkId)
        {
            var key = (termId, chunkId);
            _pairs[key] = _pairs.TryGetValue(key, out var state) ? (state.Before, false) : (true, false);
        }

        public void Written(long termId, string chunkId)
        {
            var key = (termId, chunkId);
            _pairs[key] = _pairs.TryGetValue(key, out var state) ? (state.Before, true) : (false, true);
        }

        /// <summary>The terms the transaction wrote a posting for.</summary>
        public IEnumerable<long> WrittenTermIds()
            => _pairs.Where(pair => pair.Value.After).Select(pair => pair.Key.TermId).Distinct();

        /// <summary>The terms whose document frequency moves, grouped by how much it moves.</summary>
        public SortedDictionary<int, HashSet<long>> TermsByChange()
        {
            var perTerm = new Dictionary<long, int>();
            foreach (var ((termId, _), (before, after)) in _pairs)
            {
                var change = (after ? 1 : 0) - (before ? 1 : 0);
                if (change != 0)
                    perTerm[termId] = perTerm.GetValueOrDefault(termId) + change;
            }

            var grouped = new SortedDictionary<int, HashSet<long>>();
            foreach (var (termId, change) in perTerm)
            {
                if (change == 0)
                    continue;
                if (!grouped.TryGetValue(change, out var termIds))
                    grouped[change] = termIds = [];
                termIds.Add(termId);
            }

            return grouped;
        }
    }

    /// <summary>
    /// Moves document frequency by what the transaction changed, then removes the term rows it touched that no
    /// chunk holds any more.
    /// </summary>
    /// <remarks>
    /// Document frequency used to be rederived on every write by counting each touched term's postings. A common
    /// term has postings in a share of every chunk, so a write cost the size of the index and indexing a corpus one
    /// document at a time cost its square. The delta costs what the transaction wrote. The full recount
    /// (<see cref="BuildDocumentFrequencyUpdateSql"/>) remains the repair path: <see cref="OptimizeIndexAsync"/>
    /// runs it, and so does the first open under a different field set.
    /// </remarks>
    private async Task ApplyDocumentFrequencyDeltaAsync(
        DbConnection connection,
        DocumentFrequencyDelta frequencies,
        HashSet<long> affectedTermIds,
        CancellationToken cancellationToken)
    {
        await LockTermRowsAsync(connection, affectedTermIds, frequencies, cancellationToken).ConfigureAwait(false);

        // One statement per distinct change rather than per term: a write moves most of its terms by the
        // same amount (+1 for a new chunk, -1 for a deleted one).
        foreach (var (change, termIds) in frequencies.TermsByChange())
        {
            foreach (var batch in Batch(termIds, TermIdBatchSize))
            {
                await using var updateCmd = connection.CreateCommand();
                var predicate = BuildTermIdPredicate(updateCmd, "bm25_terms.id", batch);
                updateCmd.CommandText = $"UPDATE bm25_terms SET document_frequency = document_frequency + @change WHERE {predicate}";
                AddParameter(updateCmd, "@change", change);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (affectedTermIds.Count == 0)
            return;

        // Scoped to the rows this transaction touched, including terms it acquired but wrote no posting for.
        // The unscoped form scanned every term on every write and could wait on rows other transactions hold
        // for reasons unrelated to this one.
        foreach (var batch in Batch(affectedTermIds, TermIdBatchSize))
        {
            await using var cleanupCmd = connection.CreateCommand();
            var predicate = BuildTermIdPredicate(cleanupCmd, "bm25_terms.id", batch);
            cleanupCmd.CommandText = TermCleanupLockClause is { } clause
                ? $"DELETE FROM bm25_terms WHERE id IN (SELECT id FROM bm25_terms WHERE {predicate} AND document_frequency <= 0 {clause})"
                : $"DELETE FROM bm25_terms WHERE {predicate} AND document_frequency <= 0";
            await cleanupCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rederives every term's document frequency from the posting rows, removes the terms no chunk holds, and
    /// records the field set the counts were taken under. Costs the size of the index.
    /// </summary>
    /// <remarks>
    /// The work is split into consecutive term-id ranges of at most <see cref="TermIdBatchSize"/> rows, walked by
    /// key, so no single statement grows with the table. One statement over every term ran past a command timeout
    /// on a production-sized index, and since the first open under a different field set runs this recount, that
    /// failed the host's startup. The ranges stay inside the caller's transaction: the counts and the recorded
    /// field set still commit together.
    /// </remarks>
    private async Task RecountDocumentFrequenciesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var lowerExclusive = long.MinValue;
        while (await ReadTermRangeUpperBoundAsync(connection, lowerExclusive, cancellationToken).ConfigureAwait(false) is { } upperInclusive)
        {
            await using (var updateCmd = connection.CreateCommand())
            {
                // A chunk that holds the term in its body and in a field is one document for IDF: the count is over
                // distinct chunks across both posting relations, never a sum of rows. Only the fields currently
                // configured count - the same rows the query reads.
                updateCmd.CommandText = BuildDocumentFrequencyUpdateSql(
                    Fields.Fields.Count == 0 ? null : BuildFieldPredicate(updateCmd, "f.field"),
                    TermRangePredicate);
                AddTermRangeParameters(updateCmd, lowerExclusive, upperInclusive);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var cleanupCmd = connection.CreateCommand())
            {
                cleanupCmd.CommandText = $"DELETE FROM bm25_terms WHERE {TermRangePredicate} AND document_frequency <= 0";
                AddTermRangeParameters(cleanupCmd, lowerExclusive, upperInclusive);
                await cleanupCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            lowerExclusive = upperInclusive;
        }

        await WriteDocumentFrequencyFieldsKeyAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private const string TermRangePredicate = "bm25_terms.id > @rangeLower AND bm25_terms.id <= @rangeUpper";

    private static void AddTermRangeParameters(DbCommand command, long lowerExclusive, long upperInclusive)
    {
        AddParameter(command, "@rangeLower", lowerExclusive);
        AddParameter(command, "@rangeUpper", upperInclusive);
    }

    /// <summary>
    /// The largest term id among the next <see cref="TermIdBatchSize"/> ids above <paramref name="lowerExclusive"/>,
    /// or <see langword="null"/> when no term lies above it. Reads the primary key only.
    /// </summary>
    private async Task<long?> ReadTermRangeUpperBoundAsync(DbConnection connection, long lowerExclusive, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT MAX(id) FROM (SELECT id FROM bm25_terms WHERE id > @rangeLower ORDER BY id LIMIT @rangeSize) AS term_range";
        AddParameter(command, "@rangeLower", lowerExclusive);
        AddParameter(command, "@rangeSize", TermIdBatchSize);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Recounts document frequency once when the stored counts were taken under a different field set than the
    /// one configured now - or the store does not say, as an index written by an earlier release does not.
    /// </summary>
    /// <remarks>
    /// A delta counts a field row only if its field is configured, so it is exact only against counts taken under
    /// the same configuration. A store indexed with the title field and opened without it would otherwise keep
    /// counting title-only chunks for as long as the terms live.
    /// </remarks>
    private async Task ReconcileDocumentFrequencyFieldsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var stored = await ReadDocumentFrequencyFieldsKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        if (stored.Count == 1 && string.Equals(stored[0], _documentFrequencyFieldsKey, StringComparison.Ordinal))
            return;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RecountDocumentFrequenciesAsync(connection, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            LogDocumentFrequenciesRecounted(Logger, BackendName);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<List<string>> ReadDocumentFrequencyFieldsKeysAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key FROM bm25_statistics WHERE key LIKE 'df%'";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            if (key.StartsWith(DocumentFrequencyFieldsKeyPrefix, StringComparison.Ordinal))
                keys.Add(key);
        }

        return keys;
    }

    /// <summary>Replaces the recorded field set with the one configured now.</summary>
    private async Task WriteDocumentFrequencyFieldsKeyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        foreach (var key in await ReadDocumentFrequencyFieldsKeysAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(key, _documentFrequencyFieldsKey, StringComparison.Ordinal))
                continue;

            await using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM bm25_statistics WHERE key = @key";
            AddParameter(deleteCmd, "@key", key);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpsertStatisticAsync(connection, _documentFrequencyFieldsKey, 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The statement that rederives document frequency from the posting rows for the terms
    /// <paramref name="termPredicate"/> selects: the distinct chunks holding the term in the body or in a
    /// configured field. Writes move the stored value by a delta of the same quantity; this is the recount
    /// that repairs it.
    /// </summary>
    /// <remarks>
    /// Every subquery is correlated on <c>term_id</c> directly, the leading column of both posting
    /// tables' primary keys, so each term costs two index range reads. The earlier form counted over a
    /// derived table — the body postings <c>UNION ALL</c> the field postings, filtered by
    /// <c>term_id</c> outside it — and a planner that does not push that filter into the union reads
    /// both posting tables in full for every statement: each write then costs the size of the index,
    /// and writing a vault entry by entry costs its square. The body count needs no <c>DISTINCT</c>
    /// because <c>(term_id, chunk_id)</c> is that table's key; a field row counts only when the chunk
    /// has no body row for the term.
    /// </remarks>
    internal static string BuildDocumentFrequencyUpdateSql(string? fieldPredicate, string termPredicate)
    {
        var fieldOnlyChunks = fieldPredicate is null
            ? string.Empty
            : $"""

                  + (SELECT COUNT(DISTINCT f.chunk_id) FROM bm25_field_postings f
                     WHERE f.term_id = bm25_terms.id AND {fieldPredicate}
                       AND NOT EXISTS (SELECT 1 FROM bm25_postings b
                                       WHERE b.term_id = f.term_id AND b.chunk_id = f.chunk_id))
              """;
        return $"""
            UPDATE bm25_terms
            SET document_frequency =
                (SELECT COUNT(*) FROM bm25_postings p WHERE p.term_id = bm25_terms.id){fieldOnlyChunks}
            WHERE {termPredicate};
            """;
    }

    private static IEnumerable<IReadOnlyCollection<long>> Batch(HashSet<long> ids, int batchSize)
    {
        if (ids.Count <= batchSize)
        {
            yield return ids;
            yield break;
        }

        var buffer = new List<long>(batchSize);
        foreach (var id in ids)
        {
            buffer.Add(id);
            if (buffer.Count == batchSize)
            {
                yield return buffer;
                buffer = new List<long>(batchSize);
            }
        }

        if (buffer.Count > 0)
            yield return buffer;
    }

    /// <inheritdoc />
    public async Task ClearIndexAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM bm25_postings;
            DELETE FROM bm25_field_postings;
            DELETE FROM bm25_terms;
            DELETE FROM bm25_chunk_metadata;
            DELETE FROM bm25_chunks;
            DELETE FROM bm25_statistics;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // An empty index counts nothing under any field set; recording the current one keeps the next open from
        // recounting it.
        await WriteDocumentFrequencyFieldsKeyAsync(connection, cancellationToken).ConfigureAwait(false);

        LogIndexCleared(Logger);
    }

    #endregion

    #region Statistics and maintenance

    /// <inheritdoc />
    public async Task<KeywordIndexStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var totalDocs = (long)await GetStatValueAsync(connection, "total_documents", cancellationToken).ConfigureAwait(false);
        var avgDocLength = await GetStatValueAsync(connection, "avg_doc_length", cancellationToken).ConfigureAwait(false);

        int termCount;
        await using (var termCountCmd = connection.CreateCommand())
        {
            termCountCmd.CommandText = "SELECT COUNT(*) FROM bm25_terms";
            termCount = Convert.ToInt32(
                await termCountCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        long totalOccurrences;
        await using (var totalOccCmd = connection.CreateCommand())
        {
            totalOccCmd.CommandText = "SELECT COALESCE(SUM(term_frequency), 0) FROM bm25_postings";
            totalOccurrences = Convert.ToInt64(
                await totalOccCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        var topTerms = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var topTermsCmd = connection.CreateCommand())
        {
            topTermsCmd.CommandText = """
                SELECT term, document_frequency
                FROM bm25_terms
                ORDER BY document_frequency DESC
                LIMIT 20;
                """;

            await using var reader = await topTermsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                topTerms[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        return new KeywordIndexStatistics
        {
            TotalDocuments = totalDocs,
            TotalTerms = termCount,
            TotalTermOccurrences = totalOccurrences,
            AverageDocumentLength = avgDocLength,
            IndexSizeBytes = 0,
            LastOptimizedAt = null,
            TopFrequentTerms = topTerms
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// The repair path for the counts every write maintains by deltas: document frequency and the corpus statistics
    /// are rederived from the posting rows in one transaction, terms no chunk holds are removed, and the store is
    /// then compacted where the backend supports it. Costs the size of the index.
    /// </remarks>
    public async Task OptimizeIndexAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await RunWithConcurrencyRetryAsync(
            () => RecountOnceAsync(cancellationToken), cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (CompactSql is { Length: > 0 } compactSql)
        {
            await using var compactCmd = connection.CreateCommand();
            compactCmd.CommandText = compactSql;
            await compactCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        LogIndexOptimized(Logger);
    }

    /// <summary>Attempts one recount transaction. Retried as a whole on a serialization failure.</summary>
    private async Task RecountOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RecountDocumentFrequenciesAsync(connection, cancellationToken).ConfigureAwait(false);
            await RecountStatisticsAsync(connection, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public Task RefreshIDFCacheAsync(CancellationToken cancellationToken = default)
    {
        // IDF is derived from document_frequency on every query, so there is no cache to rebuild.
        return Task.CompletedTask;
    }

    #endregion

    #region Term operations

    /// <inheritdoc />
    public double GetIDF(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return 0;

        using var connection = CreateConnection();
        connection.Open();

        var totalDocs = GetStatValue(connection, "total_documents");
        if (totalDocs == 0)
            return 0;

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_frequency FROM bm25_terms WHERE term = @term";
        AddParameter(command, "@term", NormalizeTerm(term));

        var result = command.ExecuteScalar();
        if (result is null || result == DBNull.Value)
            return 0;

        return ComputeIdf(totalDocs, Convert.ToInt32(result, CultureInfo.InvariantCulture));
    }

    /// <summary>Terms per lookup statement — well under every backend's bound-parameter limit (SQLite: 32 766).</summary>
    private const int DocumentFrequencyLookupBatchSize = 500;

    /// <inheritdoc />
    /// <remarks>
    /// Reads <c>bm25_terms.document_frequency</c>, the count scoring uses, with one <c>IN (…)</c> statement per
    /// <see cref="DocumentFrequencyLookupBatchSize"/> distinct terms on a single connection.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, int>> GetDocumentFrequenciesAsync(
        IEnumerable<string> terms,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);

        // The caller's spellings, grouped by the form the index stores.
        var spellingsByTerm = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (term is null || result.ContainsKey(term))
                continue;

            result[term] = 0;
            var normalized = NormalizeTerm(term);
            if (!spellingsByTerm.TryGetValue(normalized, out var spellings))
                spellingsByTerm[normalized] = spellings = [];
            spellings.Add(term);
        }

        if (spellingsByTerm.Count == 0)
            return result;

        // Like every other operation: the schema exists and the counts were taken under the configured field set
        // before they are read. Without it the first read of a process reopened under different fields returned
        // the counts of the old ones.
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var batch in spellingsByTerm.Keys.Chunk(DocumentFrequencyLookupBatchSize))
        {
            await using var command = connection.CreateCommand();
            var names = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                names[i] = "@t" + i.ToString(CultureInfo.InvariantCulture);
                AddParameter(command, names[i], batch[i]);
            }

            command.CommandText =
                $"SELECT term, document_frequency FROM bm25_terms WHERE term IN ({string.Join(", ", names)})";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var frequency = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                foreach (var spelling in spellingsByTerm[reader.GetString(0)])
                    result[spelling] = frequency;
            }
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>Delegates to <see cref="Analyzer"/> — the same instance the index path uses.</remarks>
    public IEnumerable<string> Tokenize(string text) => Analyzer.Tokenize(text);

    /// <summary>
    /// Brings a term into the casing the index stores. <see cref="Tokenize"/> already lower-cases, so
    /// this only matters for a term handed in directly; doing it here rather than relying on a
    /// case-insensitive collation keeps lookups identical on every backend.
    /// </summary>
    private static string NormalizeTerm(string term) => term.ToLowerInvariant();

    #endregion

    #region Helpers

    /// <summary>
    /// Adds a parameter, mapping null to <see cref="DBNull"/>.
    /// <para>
    /// A null is given an explicit string type rather than left for the provider to infer: a
    /// <see cref="DBNull"/> carries no type information, and providers that require one fail at
    /// execution time. Every nullable column in this schema is text (<c>bm25_chunks.metadata</c>),
    /// so declaring it here is both correct and the common path — most chunks have no metadata.
    /// </para>
    /// </summary>
    protected static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;

        if (value is null)
        {
            parameter.DbType = DbType.String;
            parameter.Value = DBNull.Value;
        }
        else
        {
            parameter.Value = value;
        }

        command.Parameters.Add(parameter);
    }

    private static async Task<double> GetStatValueAsync(DbConnection connection, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM bm25_statistics WHERE key = @key";
        AddParameter(command, "@key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result != DBNull.Value
            ? Convert.ToDouble(result, CultureInfo.InvariantCulture)
            : 0;
    }

    private static double GetStatValue(DbConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM bm25_statistics WHERE key = @key";
        AddParameter(command, "@key", key);

        var result = command.ExecuteScalar();
        return result is not null && result != DBNull.Value
            ? Convert.ToDouble(result, CultureInfo.InvariantCulture)
            : 0;
    }

    private const string TotalDocumentsKey = "total_documents";
    private const string TotalDocumentLengthKey = "total_doc_length";
    private const string AverageDocumentLengthKey = "avg_doc_length";

    private static string FieldDocumentCountKey(string field) => "field_doc_count:" + field;

    private static string FieldTotalLengthKey(string field) => "field_total_length:" + field;

    /// <summary>
    /// What one write transaction changes about the corpus statistics: documents and analyzed length
    /// added and removed, for the body and per field.
    /// </summary>
    private sealed class StatisticsDelta
    {
        public long Documents { get; private set; }

        public long DocumentLength { get; private set; }

        public SortedDictionary<string, (long Documents, long Length)> Fields { get; } = new(StringComparer.Ordinal);

        public void AddDocument(long length)
        {
            Documents++;
            DocumentLength += length;
        }

        public void RemoveDocument(long length)
        {
            Documents--;
            DocumentLength -= length;
        }

        public void AddField(string field, long length) => Adjust(field, 1, length);

        public void RemoveField(string field, long length) => Adjust(field, -1, -length);

        private void Adjust(string field, long documents, long length)
        {
            Fields.TryGetValue(field, out var current);
            Fields[field] = (current.Documents + documents, current.Length + length);
        }
    }

    /// <summary>
    /// Moves the stored statistics by what the transaction changed.
    /// </summary>
    /// <remarks>
    /// The statistics used to be recounted from the posting tables on every write — a distinct count
    /// and an average over every posting row, plus one more per field — so a write cost the size of
    /// the index. The totals are kept as sums now and moved by the delta; the averages the scorer
    /// reads are rewritten from them. A store whose sums are missing (written by an earlier release,
    /// or a field that had no total yet) is recounted once, from the rows as the transaction leaves
    /// them, which also makes the recount the repair path: <see cref="OptimizeIndexAsync"/> runs it.
    /// Keys are written in ordinal order so two transactions never take the rows in opposite orders.
    /// </remarks>
    private async Task ApplyStatisticsDeltaAsync(DbConnection connection, StatisticsDelta delta, CancellationToken cancellationToken)
    {
        var stored = await ReadStatisticsAsync(connection, cancellationToken).ConfigureAwait(false);
        var missing = !stored.ContainsKey(TotalDocumentLengthKey)
            || !stored.ContainsKey(TotalDocumentsKey)
            || delta.Fields.Keys.Any(f => !stored.ContainsKey(FieldDocumentCountKey(f)) || !stored.ContainsKey(FieldTotalLengthKey(f)));
        if (missing)
        {
            await RecountStatisticsAsync(connection, cancellationToken).ConfigureAwait(false);
            return;
        }

        var increments = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            [TotalDocumentsKey] = delta.Documents,
            [TotalDocumentLengthKey] = delta.DocumentLength,
        };
        foreach (var (field, change) in delta.Fields)
        {
            increments[FieldDocumentCountKey(field)] = change.Documents;
            increments[FieldTotalLengthKey(field)] = change.Length;
        }

        foreach (var (key, change) in increments)
        {
            if (change == 0)
                continue;

            await using var incrementCmd = connection.CreateCommand();
            incrementCmd.CommandText = "UPDATE bm25_statistics SET value = value + @delta WHERE key = @key";
            AddParameter(incrementCmd, "@delta", (double)change);
            AddParameter(incrementCmd, "@key", key);
            await incrementCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Re-read after the increments: under a concurrent writer the totals this transaction now
        // holds are the ones the averages must be derived from.
        var totals = await ReadStatisticsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (delta.Documents != 0 || delta.DocumentLength != 0)
        {
            await UpsertStatisticAsync(
                connection,
                AverageDocumentLengthKey,
                Average(totals.GetValueOrDefault(TotalDocumentLengthKey), totals.GetValueOrDefault(TotalDocumentsKey)),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var field in delta.Fields.Keys)
        {
            await UpsertStatisticAsync(
                connection,
                FieldAverageLengthKey(field),
                Average(totals.GetValueOrDefault(FieldTotalLengthKey(field)), totals.GetValueOrDefault(FieldDocumentCountKey(field))),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static double Average(double total, double count) => count > 0 ? total / count : 0;

    private static async Task<Dictionary<string, double>> ReadStatisticsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM bm25_statistics";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values[reader.GetString(0)] = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <summary>Rederives every statistic from the posting rows. Costs the size of the index.</summary>
    private async Task RecountStatisticsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var written = new SortedDictionary<string, double>(StringComparer.Ordinal);

        await using (var bodyCmd = connection.CreateCommand())
        {
            bodyCmd.CommandText =
                "SELECT COUNT(*), COALESCE(SUM(document_length), 0) FROM (SELECT DISTINCT chunk_id, document_length FROM bm25_postings) lengths";
            await using var reader = await bodyCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var documents = Convert.ToDouble(reader.GetValue(0), CultureInfo.InvariantCulture);
            var length = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
            written[TotalDocumentsKey] = documents;
            written[TotalDocumentLengthKey] = length;
            written[AverageDocumentLengthKey] = Average(length, documents);
        }

        // Every configured field gets its keys even with no rows yet, so the next write to it is an
        // increment rather than another recount.
        foreach (var field in Fields.Fields)
        {
            written[FieldDocumentCountKey(field.MetadataKey)] = 0;
            written[FieldTotalLengthKey(field.MetadataKey)] = 0;
            written[FieldAverageLengthKey(field.MetadataKey)] = 0;
        }

        await using (var fieldCmd = connection.CreateCommand())
        {
            fieldCmd.CommandText =
                "SELECT field, COUNT(*), COALESCE(SUM(field_length), 0) FROM (SELECT DISTINCT chunk_id, field, field_length FROM bm25_field_postings) lengths GROUP BY field";
            await using var reader = await fieldCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var field = reader.GetString(0);
                var documents = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
                var length = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
                written[FieldDocumentCountKey(field)] = documents;
                written[FieldTotalLengthKey(field)] = length;
                written[FieldAverageLengthKey(field)] = Average(length, documents);
            }
        }

        foreach (var (key, value) in written)
        {
            await UpsertStatisticAsync(connection, key, value, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpsertStatisticAsync(DbConnection connection, string key, double value, CancellationToken cancellationToken)
    {
        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = UpsertStatisticSql;
        AddParameter(updateCmd, "@key", key);
        AddParameter(updateCmd, "@value", value);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Disposal

    /// <summary>
    /// Releases resources held by the service. Subclasses that hold a connection open should override
    /// <see cref="Dispose(bool)"/> rather than this method.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources; override to release backend-specific state.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _initLock.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region LoggerMessage definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword index initialized ({Backend})")]
    private static partial void LogServiceInitialized(ILogger logger, string backend);

    [LoggerMessage(Level = LogLevel.Debug, Message = "BM25 search started: {Query} ({TermCount} terms)")]
    private static partial void LogSearchStarted(ILogger logger, string query, int termCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "BM25 search completed: {ResultCount} results")]
    private static partial void LogSearchCompleted(ILogger logger, int resultCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Indexed chunk {ChunkId} with {TermCount} terms")]
    private static partial void LogChunkIndexed(ILogger logger, string chunkId, int termCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Indexed {ChunkCount} chunks into the keyword index")]
    private static partial void LogChunksIndexed(ILogger logger, int chunkCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Keyword index transaction hit a concurrency conflict (SQLSTATE {SqlState}); retry {Attempt}")]
    private static partial void LogConcurrencyRetry(ILogger logger, int attempt, string sqlState);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Keyword index transaction lost a term row to a concurrent writer's cleanup; retry {Attempt}")]
    private static partial void LogTermRowRemovedRetry(ILogger logger, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reassigned {ChunkCount} keyword-index chunks from document {OldDocumentId} to {NewDocumentId}")]
    private static partial void LogDocumentReassigned(ILogger logger, int chunkCount, string oldDocumentId, string newDocumentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted chunk {ChunkId} from keyword index")]
    private static partial void LogChunkDeleted(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {ChunkCount} chunks from keyword index matching {ConditionCount} metadata conditions")]
    private static partial void LogChunksDeletedByFilter(ILogger logger, int chunkCount, int conditionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword index cleared")]
    private static partial void LogIndexCleared(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword index document frequencies recounted for the configured field set ({Backend})")]
    private static partial void LogDocumentFrequenciesRecounted(ILogger logger, string backend);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword index optimized")]
    private static partial void LogIndexOptimized(ILogger logger);

    #endregion
}
