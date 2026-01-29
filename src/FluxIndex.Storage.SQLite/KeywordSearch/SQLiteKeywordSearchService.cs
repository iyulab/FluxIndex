using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluxIndex.Storage.SQLite.KeywordSearch;

/// <summary>
/// SQLite RDB-backed implementation of IKeywordSearchService.
/// Uses persistent inverted index tables for efficient BM25 keyword search.
/// </summary>
public class SQLiteKeywordSearchService : IKeywordSearchService
{
    private readonly string _connectionString;
    private readonly ILogger<SQLiteKeywordSearchService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    // BM25 default parameters
    private const double DefaultK1 = 1.2;
    private const double DefaultB = 0.75;

    // Stop words for tokenization
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
        "in", "with", "to", "for", "of", "as", "by", "this", "that", "these", "those",
        "it", "its", "be", "are", "was", "were", "been", "being", "have", "has", "had"
    };

    public SQLiteKeywordSearchService(
        IOptions<SQLiteOptions> options,
        ILogger<SQLiteKeywordSearchService> logger)
    {
        var opts = options.Value;
        _connectionString = opts.UseInMemory
            ? "Data Source=:memory:;Mode=Memory;Cache=Shared"
            : $"Data Source={opts.DatabasePath}";
        _logger = logger;
    }

    public SQLiteKeywordSearchService(
        string connectionString,
        ILogger<SQLiteKeywordSearchService> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    #region Initialization

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Create BM25 tables
            var createTablesSql = """
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

                CREATE TABLE IF NOT EXISTS bm25_statistics (
                    key TEXT PRIMARY KEY,
                    value REAL NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_bm25_terms_term ON bm25_terms(term);
                CREATE INDEX IF NOT EXISTS idx_bm25_postings_chunk ON bm25_postings(chunk_id);
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = createTablesSql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _initialized = true;
            _logger.LogInformation("SQLiteKeywordSearchService initialized");
        }
        finally
        {
            _initLock.Release();
        }
    }

    #endregion

    #region Search Operations

    public async Task<IReadOnlyList<KeywordSearchResult>> SearchAsync(
        string query,
        KeywordSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        await EnsureInitializedAsync(cancellationToken);
        options ??= new KeywordSearchOptions();

        var terms = Tokenize(query).ToList();
        if (terms.Count == 0)
            return [];

        _logger.LogDebug("BM25 search started: {Query} ({TermCount} terms)", query, terms.Count);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Get index statistics
        var totalDocs = await GetStatValueAsync(connection, "total_documents", cancellationToken);
        var avgDocLength = await GetStatValueAsync(connection, "avg_doc_length", cancellationToken);

        if (totalDocs == 0 || avgDocLength == 0)
            return [];

        // Calculate BM25 scores
        var scores = new Dictionary<string, (double Score, List<string> MatchedTerms, Dictionary<string, int> TermFreqs, int DocLength)>();

        foreach (var term in terms)
        {
            // Get term ID and document frequency
            var termSql = "SELECT id, document_frequency FROM bm25_terms WHERE term = @term COLLATE NOCASE";
            await using var termCmd = connection.CreateCommand();
            termCmd.CommandText = termSql;
            termCmd.Parameters.AddWithValue("@term", term);

            await using var termReader = await termCmd.ExecuteReaderAsync(cancellationToken);
            if (!await termReader.ReadAsync(cancellationToken))
                continue;

            var termId = termReader.GetInt32(0);
            var df = termReader.GetInt32(1);
            await termReader.CloseAsync();

            // Calculate IDF
            var idf = Math.Log((totalDocs - df + 0.5) / (df + 0.5));

            // Get postings for this term
            var postingSql = "SELECT chunk_id, term_frequency, document_length FROM bm25_postings WHERE term_id = @termId";
            await using var postingCmd = connection.CreateCommand();
            postingCmd.CommandText = postingSql;
            postingCmd.Parameters.AddWithValue("@termId", termId);

            await using var postingReader = await postingCmd.ExecuteReaderAsync(cancellationToken);
            while (await postingReader.ReadAsync(cancellationToken))
            {
                var chunkId = postingReader.GetString(0);
                var tf = postingReader.GetInt32(1);
                var docLength = postingReader.GetInt32(2);

                // Calculate BM25 score for this term-document pair
                var normalizedTf = (tf * (options.K1 + 1)) /
                    (tf + options.K1 * (1 - options.B + options.B * (docLength / avgDocLength)));
                var bm25Score = idf * normalizedTf;

                if (scores.TryGetValue(chunkId, out var existing))
                {
                    existing.MatchedTerms.Add(term);
                    existing.TermFreqs[term] = tf;
                    scores[chunkId] = (existing.Score + bm25Score, existing.MatchedTerms, existing.TermFreqs, docLength);
                }
                else
                {
                    scores[chunkId] = (bm25Score, new List<string> { term }, new Dictionary<string, int> { { term, tf } }, docLength);
                }
            }
        }

        // Filter and sort results
        var results = new List<KeywordSearchResult>();

        // Get chunk details
        var chunkIds = scores.Keys
            .Where(id => scores[id].Score >= options.MinScore)
            .OrderByDescending(id => scores[id].Score)
            .Take(options.MaxResults)
            .ToList();

        if (chunkIds.Count == 0)
            return [];

        // Load chunk data from vectors table
        var chunkSql = $"SELECT Id, DocumentId, ChunkIndex, Content, TokenCount, Metadata FROM vectors WHERE Id IN ({string.Join(",", chunkIds.Select((_, i) => $"@id{i}"))})";
        await using var chunkCmd = connection.CreateCommand();
        chunkCmd.CommandText = chunkSql;
        for (int i = 0; i < chunkIds.Count; i++)
        {
            chunkCmd.Parameters.AddWithValue($"@id{i}", chunkIds[i]);
        }

        var chunkDict = new Dictionary<string, DocumentChunk>();
        await using var chunkReader = await chunkCmd.ExecuteReaderAsync(cancellationToken);
        while (await chunkReader.ReadAsync(cancellationToken))
        {
            var chunk = new DocumentChunk
            {
                Id = chunkReader.GetString(0),
                DocumentId = chunkReader.GetString(1),
                ChunkIndex = chunkReader.GetInt32(2),
                Content = chunkReader.GetString(3),
                TokenCount = chunkReader.GetInt32(4)
            };

            var metadataJson = chunkReader.IsDBNull(5) ? null : chunkReader.GetString(5);
            if (!string.IsNullOrEmpty(metadataJson))
            {
                chunk.Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
            }

            chunkDict[chunk.Id] = chunk;
        }

        // Build results
        foreach (var chunkId in chunkIds)
        {
            if (chunkDict.TryGetValue(chunkId, out var chunk) && scores.TryGetValue(chunkId, out var scoreData))
            {
                results.Add(new KeywordSearchResult
                {
                    Chunk = chunk,
                    Score = scoreData.Score,
                    MatchedTerms = scoreData.MatchedTerms.Distinct().ToList(),
                    TermFrequencies = scoreData.TermFreqs,
                    DocumentLength = scoreData.DocLength
                });
            }
        }

        _logger.LogDebug("BM25 search completed: {ResultCount} results", results.Count);
        return results;
    }

    #endregion

    #region Index Management

    public async Task IndexChunkAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await EnsureInitializedAsync(cancellationToken);

        var terms = Tokenize(chunk.Content).ToList();
        if (terms.Count == 0)
            return;

        var termFrequencies = terms
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var (term, frequency) in termFrequencies)
            {
                // Insert or get term ID
                var upsertTermSql = """
                    INSERT INTO bm25_terms (term, document_frequency) VALUES (@term, 1)
                    ON CONFLICT(term) DO UPDATE SET document_frequency = document_frequency + 1
                    RETURNING id;
                    """;

                await using var termCmd = connection.CreateCommand();
                termCmd.CommandText = upsertTermSql;
                termCmd.Parameters.AddWithValue("@term", term);
                var termId = Convert.ToInt32(await termCmd.ExecuteScalarAsync(cancellationToken));

                // Insert posting
                var insertPostingSql = """
                    INSERT OR REPLACE INTO bm25_postings (term_id, chunk_id, term_frequency, document_length)
                    VALUES (@termId, @chunkId, @tf, @docLen);
                    """;

                await using var postingCmd = connection.CreateCommand();
                postingCmd.CommandText = insertPostingSql;
                postingCmd.Parameters.AddWithValue("@termId", termId);
                postingCmd.Parameters.AddWithValue("@chunkId", chunk.Id);
                postingCmd.Parameters.AddWithValue("@tf", frequency);
                postingCmd.Parameters.AddWithValue("@docLen", terms.Count);
                await postingCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Update statistics
            await UpdateStatisticsAsync(connection, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Indexed chunk {ChunkId} with {TermCount} terms", chunk.Id, termFrequencies.Count);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task IndexChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            await IndexChunkAsync(chunk, cancellationToken);
        }
    }

    public async Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            return;

        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Get affected terms and decrement their document frequency
            var updateDfSql = """
                UPDATE bm25_terms SET document_frequency = document_frequency - 1
                WHERE id IN (SELECT term_id FROM bm25_postings WHERE chunk_id = @chunkId);
                """;
            await using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = updateDfSql;
            updateCmd.Parameters.AddWithValue("@chunkId", chunkId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);

            // Delete postings
            var deletePostingsSql = "DELETE FROM bm25_postings WHERE chunk_id = @chunkId";
            await using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = deletePostingsSql;
            deleteCmd.Parameters.AddWithValue("@chunkId", chunkId);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);

            // Clean up terms with zero document frequency
            var cleanupSql = "DELETE FROM bm25_terms WHERE document_frequency <= 0";
            await using var cleanupCmd = connection.CreateCommand();
            cleanupCmd.CommandText = cleanupSql;
            await cleanupCmd.ExecuteNonQueryAsync(cancellationToken);

            await UpdateStatisticsAsync(connection, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogDebug("Deleted chunk {ChunkId} from keyword index", chunkId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return;

        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Get chunk IDs for this document from the vectors table
        var chunkIdsSql = "SELECT Id FROM vectors WHERE DocumentId = @documentId";
        await using var chunkIdsCmd = connection.CreateCommand();
        chunkIdsCmd.CommandText = chunkIdsSql;
        chunkIdsCmd.Parameters.AddWithValue("@documentId", documentId);

        var chunkIds = new List<string>();
        await using var reader = await chunkIdsCmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunkIds.Add(reader.GetString(0));
        }

        foreach (var chunkId in chunkIds)
        {
            await DeleteChunkAsync(chunkId, cancellationToken);
        }
    }

    public async Task ClearIndexAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var clearSql = """
            DELETE FROM bm25_postings;
            DELETE FROM bm25_terms;
            DELETE FROM bm25_statistics;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = clearSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Keyword index cleared");
    }

    #endregion

    #region Statistics and Maintenance

    public async Task<KeywordIndexStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var totalDocs = (long)await GetStatValueAsync(connection, "total_documents", cancellationToken);
        var avgDocLength = await GetStatValueAsync(connection, "avg_doc_length", cancellationToken);

        // Get term count
        var termCountSql = "SELECT COUNT(*) FROM bm25_terms";
        await using var termCountCmd = connection.CreateCommand();
        termCountCmd.CommandText = termCountSql;
        var termCount = Convert.ToInt32(await termCountCmd.ExecuteScalarAsync(cancellationToken));

        // Get total occurrences
        var totalOccSql = "SELECT COALESCE(SUM(term_frequency), 0) FROM bm25_postings";
        await using var totalOccCmd = connection.CreateCommand();
        totalOccCmd.CommandText = totalOccSql;
        var totalOccurrences = Convert.ToInt64(await totalOccCmd.ExecuteScalarAsync(cancellationToken));

        // Get top terms
        var topTermsSql = """
            SELECT t.term, t.document_frequency
            FROM bm25_terms t
            ORDER BY t.document_frequency DESC
            LIMIT 20;
            """;
        await using var topTermsCmd = connection.CreateCommand();
        topTermsCmd.CommandText = topTermsSql;

        var topTerms = new Dictionary<string, long>();
        await using var reader = await topTermsCmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            topTerms[reader.GetString(0)] = reader.GetInt32(1);
        }

        return new KeywordIndexStatistics
        {
            TotalDocuments = totalDocs,
            TotalTerms = termCount,
            TotalTermOccurrences = totalOccurrences,
            AverageDocumentLength = avgDocLength,
            IndexSizeBytes = 0, // Would need to query file size
            LastOptimizedAt = null,
            TopFrequentTerms = topTerms
        };
    }

    public async Task OptimizeIndexAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Remove terms with zero document frequency
        var cleanupSql = "DELETE FROM bm25_terms WHERE document_frequency <= 0";
        await using var cleanupCmd = connection.CreateCommand();
        cleanupCmd.CommandText = cleanupSql;
        await cleanupCmd.ExecuteNonQueryAsync(cancellationToken);

        // Vacuum the database
        var vacuumSql = "VACUUM";
        await using var vacuumCmd = connection.CreateCommand();
        vacuumCmd.CommandText = vacuumSql;
        await vacuumCmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Keyword index optimized");
    }

    public Task RefreshIDFCacheAsync(CancellationToken cancellationToken = default)
    {
        // IDF is computed dynamically from document_frequency in this implementation
        return Task.CompletedTask;
    }

    #endregion

    #region Term Operations

    public double GetIDF(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return 0;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var totalDocs = GetStatValueSync(connection, "total_documents");
        if (totalDocs == 0)
            return 0;

        var dfSql = "SELECT document_frequency FROM bm25_terms WHERE term = @term COLLATE NOCASE";
        using var dfCmd = connection.CreateCommand();
        dfCmd.CommandText = dfSql;
        dfCmd.Parameters.AddWithValue("@term", term);

        var result = dfCmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
            return 0;

        var df = Convert.ToInt32(result);
        return Math.Log((totalDocs - df + 0.5) / (df + 0.5));
    }

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

    #endregion

    #region Private Helpers

    private async Task<double> GetStatValueAsync(SqliteConnection connection, string key, CancellationToken cancellationToken)
    {
        var sql = "SELECT value FROM bm25_statistics WHERE key = @key";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value ? Convert.ToDouble(result) : 0;
    }

    private double GetStatValueSync(SqliteConnection connection, string key)
    {
        var sql = "SELECT value FROM bm25_statistics WHERE key = @key";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);

        var result = cmd.ExecuteScalar();
        return result != null && result != DBNull.Value ? Convert.ToDouble(result) : 0;
    }

    private async Task UpdateStatisticsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Calculate total documents
        var totalDocsSql = "SELECT COUNT(DISTINCT chunk_id) FROM bm25_postings";
        await using var totalDocsCmd = connection.CreateCommand();
        totalDocsCmd.CommandText = totalDocsSql;
        var totalDocs = Convert.ToDouble(await totalDocsCmd.ExecuteScalarAsync(cancellationToken));

        // Calculate average document length
        var avgLengthSql = "SELECT AVG(document_length) FROM (SELECT DISTINCT chunk_id, document_length FROM bm25_postings)";
        await using var avgLengthCmd = connection.CreateCommand();
        avgLengthCmd.CommandText = avgLengthSql;
        var avgLengthResult = await avgLengthCmd.ExecuteScalarAsync(cancellationToken);
        var avgLength = avgLengthResult != null && avgLengthResult != DBNull.Value ? Convert.ToDouble(avgLengthResult) : 0;

        // Update statistics
        var updateSql = """
            INSERT OR REPLACE INTO bm25_statistics (key, value) VALUES ('total_documents', @totalDocs);
            INSERT OR REPLACE INTO bm25_statistics (key, value) VALUES ('avg_doc_length', @avgLength);
            """;
        await using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = updateSql;
        updateCmd.Parameters.AddWithValue("@totalDocs", totalDocs);
        updateCmd.Parameters.AddWithValue("@avgLength", avgLength);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    #endregion
}
