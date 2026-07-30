using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Globalization;
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

    /// <summary>Stop words removed during tokenization.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
        "in", "with", "to", "for", "of", "as", "by", "this", "that", "these", "those",
        "it", "its", "be", "are", "was", "were", "been", "being", "have", "has", "had"
    };

    /// <summary>
    /// Initializes the shared index with the logger used for its operations.
    /// </summary>
    protected RelationalKeywordSearchService(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Dialect surface

    /// <summary>Creates a new, unopened connection to the backend.</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>
    /// DDL that creates the four index relations and their indexes if absent:
    /// <c>bm25_terms</c>, <c>bm25_postings</c>, <c>bm25_chunks</c>, <c>bm25_statistics</c>.
    /// </summary>
    protected abstract string SchemaDdl { get; }

    /// <summary>Upsert for a row of <c>bm25_chunks</c>, keyed on <c>chunk_id</c>.</summary>
    protected abstract string UpsertChunkSql { get; }

    /// <summary>Upsert for a row of <c>bm25_terms</c> that returns the term's id.</summary>
    protected abstract string UpsertTermReturningIdSql { get; }

    /// <summary>Upsert for a row of <c>bm25_postings</c>, keyed on (<c>term_id</c>, <c>chunk_id</c>).</summary>
    protected abstract string UpsertPostingSql { get; }

    /// <summary>Upsert for a row of <c>bm25_statistics</c>, keyed on <c>key</c>.</summary>
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
    /// inline the ids override this to keep the statement under their maximum length.
    /// </summary>
    protected virtual int TermIdBatchSize => int.MaxValue;

    /// <summary>
    /// Hook for backend setup that must happen once, inside the initialization lock, before the
    /// schema is created. Does nothing by default.
    /// </summary>
    protected virtual Task OnInitializingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = SchemaDdl;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _initialized = true;
            LogServiceInitialized(Logger, BackendName);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Creates and opens a connection.</summary>
    protected async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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

        LogSearchStarted(Logger, query, terms.Count);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var totalDocs = await GetStatValueAsync(connection, "total_documents", cancellationToken).ConfigureAwait(false);
        var avgDocLength = await GetStatValueAsync(connection, "avg_doc_length", cancellationToken).ConfigureAwait(false);

        if (totalDocs == 0 || avgDocLength == 0)
            return [];

        var scores = new Dictionary<string, ScoreAccumulator>(StringComparer.Ordinal);

        foreach (var term in terms)
        {
            var (termId, documentFrequency) = await TryGetTermAsync(connection, term, cancellationToken).ConfigureAwait(false);
            if (termId is null)
                continue;

            var idf = ComputeIdf(totalDocs, documentFrequency);

            await using var postingCmd = connection.CreateCommand();

            // A document-scoped search restricts the postings themselves rather than the results, so
            // the top-N cut still returns N matches inside that document instead of whatever survives
            // filtering the global top N.
            var scopeJoin = string.Empty;
            if (!string.IsNullOrWhiteSpace(options.DocumentIdFilter))
            {
                scopeJoin = " JOIN bm25_chunks c ON c.chunk_id = p.chunk_id AND c.document_id = @documentIdFilter";
                AddParameter(postingCmd, "@documentIdFilter", options.DocumentIdFilter);
            }

            postingCmd.CommandText =
                "SELECT p.chunk_id, p.term_frequency, p.document_length " +
                $"FROM bm25_postings p{scopeJoin} WHERE p.term_id = @termId";
            AddParameter(postingCmd, "@termId", termId.Value);

            await using var postingReader = await postingCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await postingReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var chunkId = postingReader.GetString(0);
                var tf = postingReader.GetInt32(1);
                var docLength = postingReader.GetInt32(2);

                var normalizedTf = tf * (options.K1 + 1) /
                    (tf + options.K1 * (1 - options.B + options.B * (docLength / avgDocLength)));
                var bm25Score = idf * normalizedTf;

                if (scores.TryGetValue(chunkId, out var existing))
                {
                    existing.MatchedTerms.Add(term);
                    existing.TermFrequencies[term] = tf;
                    scores[chunkId] = existing with { Score = existing.Score + bm25Score, DocumentLength = docLength };
                }
                else
                {
                    scores[chunkId] = new ScoreAccumulator(
                        bm25Score,
                        [term],
                        new Dictionary<string, int>(StringComparer.Ordinal) { [term] = tf },
                        docLength);
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
                chunk.Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
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

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var affectedTermIds = new HashSet<long>();
            var indexedChunks = 0;

            foreach (var chunk in chunkList)
            {
                if (await IndexChunkCoreAsync(connection, chunk, affectedTermIds, cancellationToken).ConfigureAwait(false))
                    indexedChunks++;
            }

            await RecomputeDocumentFrequenciesAsync(connection, affectedTermIds, cancellationToken).ConfigureAwait(false);
            await UpdateStatisticsAsync(connection, cancellationToken).ConfigureAwait(false);

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
    /// Writes one chunk's payload and postings. Re-indexing an existing chunk replaces its postings
    /// wholesale rather than layering new ones on top, so document frequency cannot drift.
    /// </summary>
    private async Task<bool> IndexChunkCoreAsync(
        DbConnection connection,
        DocumentChunk chunk,
        HashSet<long> affectedTermIds,
        CancellationToken cancellationToken)
    {
        var terms = Tokenize(chunk.Content).ToList();
        if (terms.Count == 0)
            return false;

        // Terms that lose a posting when this chunk is replaced still need their df recomputed.
        await CollectTermIdsForChunkAsync(connection, chunk.Id, affectedTermIds, cancellationToken).ConfigureAwait(false);

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.CommandText = "DELETE FROM bm25_postings WHERE chunk_id = @chunkId";
            AddParameter(deleteCmd, "@chunkId", chunk.Id);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

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

        var termFrequencies = terms
            .GroupBy(t => t, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var (term, frequency) in termFrequencies)
        {
            long termId;
            await using (var termCmd = connection.CreateCommand())
            {
                termCmd.CommandText = UpsertTermReturningIdSql;
                AddParameter(termCmd, "@term", term);
                var scalar = await termCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                termId = Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
            }

            affectedTermIds.Add(termId);

            await using var postingCmd = connection.CreateCommand();
            postingCmd.CommandText = UpsertPostingSql;
            AddParameter(postingCmd, "@termId", termId);
            AddParameter(postingCmd, "@chunkId", chunk.Id);
            AddParameter(postingCmd, "@tf", frequency);
            AddParameter(postingCmd, "@docLen", terms.Count);
            await postingCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        LogChunkIndexed(Logger, chunk.Id, termFrequencies.Count);
        return true;
    }

    /// <inheritdoc />
    public async Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            return;

        await DeleteChunksAsync([chunkId], cancellationToken).ConfigureAwait(false);
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
        var chunkIds = new List<string>();
        await using (var chunkIdsCmd = connection.CreateCommand())
        {
            chunkIdsCmd.CommandText = "SELECT chunk_id FROM bm25_chunks WHERE document_id = @documentId";
            AddParameter(chunkIdsCmd, "@documentId", documentId);

            await using var reader = await chunkIdsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                chunkIds.Add(reader.GetString(0));
            }
        }

        if (chunkIds.Count == 0)
            return;

        await DeleteChunksAsync(chunkIds, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteChunksAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var affectedTermIds = new HashSet<long>();

            foreach (var chunkId in chunkIds)
            {
                await CollectTermIdsForChunkAsync(connection, chunkId, affectedTermIds, cancellationToken).ConfigureAwait(false);

                await using var deleteCmd = connection.CreateCommand();
                deleteCmd.CommandText = """
                    DELETE FROM bm25_postings WHERE chunk_id = @chunkId;
                    DELETE FROM bm25_chunks WHERE chunk_id = @chunkId;
                    """;
                AddParameter(deleteCmd, "@chunkId", chunkId);
                await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await RecomputeDocumentFrequenciesAsync(connection, affectedTermIds, cancellationToken).ConfigureAwait(false);
            await UpdateStatisticsAsync(connection, cancellationToken).ConfigureAwait(false);
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

    private static async Task CollectTermIdsForChunkAsync(
        DbConnection connection,
        string chunkId,
        HashSet<long> affectedTermIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT term_id FROM bm25_postings WHERE chunk_id = @chunkId";
        AddParameter(command, "@chunkId", chunkId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            affectedTermIds.Add(reader.GetInt64(0));
        }
    }

    /// <summary>
    /// Derives document frequency from the postings rather than incrementing and decrementing it.
    /// Counter arithmetic drifts the moment a chunk is indexed twice — the value here is always a
    /// function of the postings that actually exist.
    /// </summary>
    private async Task RecomputeDocumentFrequenciesAsync(
        DbConnection connection,
        HashSet<long> termIds,
        CancellationToken cancellationToken)
    {
        if (termIds.Count == 0)
            return;

        foreach (var batch in Batch(termIds, TermIdBatchSize))
        {
            await using var updateCmd = connection.CreateCommand();
            var predicate = BuildTermIdPredicate(updateCmd, "bm25_terms.id", batch);
            updateCmd.CommandText = $"""
                UPDATE bm25_terms
                SET document_frequency =
                    (SELECT COUNT(*) FROM bm25_postings p WHERE p.term_id = bm25_terms.id)
                WHERE {predicate};
                """;
            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var cleanupCmd = connection.CreateCommand();
        cleanupCmd.CommandText = "DELETE FROM bm25_terms WHERE document_frequency <= 0";
        await cleanupCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            DELETE FROM bm25_terms;
            DELETE FROM bm25_chunks;
            DELETE FROM bm25_statistics;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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
    public async Task OptimizeIndexAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (var cleanupCmd = connection.CreateCommand())
        {
            cleanupCmd.CommandText = "DELETE FROM bm25_terms WHERE document_frequency <= 0";
            await cleanupCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (CompactSql is { Length: > 0 } compactSql)
        {
            await using var compactCmd = connection.CreateCommand();
            compactCmd.CommandText = compactSql;
            await compactCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        LogIndexOptimized(Logger);
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

    /// <inheritdoc />
    public IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var tokens = Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(token => !string.IsNullOrWhiteSpace(token) && token.Length > 1)
            .Where(token => !StopWords.Contains(token));

        foreach (var token in tokens)
        {
            yield return token;
        }
    }

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

    private async Task UpdateStatisticsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        double totalDocs;
        await using (var totalDocsCmd = connection.CreateCommand())
        {
            totalDocsCmd.CommandText = "SELECT COUNT(DISTINCT chunk_id) FROM bm25_postings";
            totalDocs = Convert.ToDouble(
                await totalDocsCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        double avgLength;
        await using (var avgLengthCmd = connection.CreateCommand())
        {
            avgLengthCmd.CommandText =
                "SELECT AVG(document_length) FROM (SELECT DISTINCT chunk_id, document_length FROM bm25_postings) lengths";
            var avgLengthResult = await avgLengthCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            avgLength = avgLengthResult is not null && avgLengthResult != DBNull.Value
                ? Convert.ToDouble(avgLengthResult, CultureInfo.InvariantCulture)
                : 0;
        }

        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = UpsertStatisticSql;
        AddParameter(updateCmd, "@totalDocs", totalDocs);
        AddParameter(updateCmd, "@avgLength", avgLength);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted chunk {ChunkId} from keyword index")]
    private static partial void LogChunkDeleted(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword index cleared")]
    private static partial void LogIndexCleared(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Keyword index optimized")]
    private static partial void LogIndexOptimized(ILogger logger);

    #endregion
}
