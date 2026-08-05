using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// Interface for sparse retrievers that support index persistence
/// </summary>
public interface IPersistableSparseRetriever
{
    /// <summary>
    /// Saves the index to a file
    /// </summary>
    Task SaveIndexAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the index from a file
    /// </summary>
    Task LoadIndexAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the persistence file path if configured
    /// </summary>
    string? PersistencePath { get; }

    /// <summary>
    /// Gets whether auto-save is enabled
    /// </summary>
    bool AutoSaveEnabled { get; }
}

/// <summary>
/// BM25 based sparse retrieval implementation with optional file persistence.
/// Implements the unified IKeywordSearchService contract.
/// </summary>
public partial class BM25SparseRetriever : IKeywordSearchService, IPersistableSparseRetriever, IDisposable
{
    private readonly ILogger<BM25SparseRetriever> _logger;
    private readonly ConcurrentDictionary<string, BM25Index> _indexes;
    private readonly object _lockObject = new();
    private readonly string? _persistencePath;
    private readonly bool _autoSave;
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);

    private static readonly JsonSerializerOptions s_persistenceJsonOptions = new()
    {
        WriteIndented = false
    };

    // BM25 default parameters
    private const double DefaultK1 = 1.2;
    private const double DefaultB = 0.75;

    /// <summary>
    /// Creates a new BM25 sparse retriever without persistence
    /// </summary>
    public BM25SparseRetriever(ILogger<BM25SparseRetriever> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _indexes = new ConcurrentDictionary<string, BM25Index>();
        _persistencePath = null;
        _autoSave = false;
    }

    /// <summary>
    /// Creates a new BM25 sparse retriever with optional file persistence
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="persistencePath">Path to the persistence file (null for no persistence)</param>
    /// <param name="autoSave">If true, automatically saves after each indexing operation</param>
    /// <param name="loadExisting">If true and file exists, loads index on construction</param>
    public BM25SparseRetriever(
        ILogger<BM25SparseRetriever> logger,
        string? persistencePath,
        bool autoSave = false,
        bool loadExisting = true)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _indexes = new ConcurrentDictionary<string, BM25Index>();
        _persistencePath = persistencePath;
        _autoSave = autoSave;

        if (loadExisting && !string.IsNullOrEmpty(persistencePath) && File.Exists(persistencePath))
        {
            LoadIndexAsync(persistencePath).GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public string? PersistencePath => _persistencePath;

    /// <inheritdoc />
    public bool AutoSaveEnabled => _autoSave;

    /// <summary>
    /// Executes BM25 keyword search over the in-memory inverted index.
    /// </summary>
    private async Task<IReadOnlyList<SparseSearchResult>> SearchCoreAsync(
        string query,
        SparseSearchOptions? options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SparseSearchResult>();

        options ??= new SparseSearchOptions();

        LogBM25SearchStarted(_logger, query);

        var searchTerms = TokenizeQuery(query, options);
        if (!searchTerms.Any())
            return Array.Empty<SparseSearchResult>();

        var results = new List<SparseSearchResult>();

        // Search across all indexes
        foreach (var indexKvp in _indexes)
        {
            var index = indexKvp.Value;
            var indexResults = await SearchInIndexAsync(searchTerms, index, options, cancellationToken);
            results.AddRange(indexResults);
        }

        // Sort by score and return top results
        var sortedResults = results
            .Where(r => r.Score >= options.MinScore)
            .OrderByDescending(r => r.Score)
            .Take(options.MaxResults)
            .ToList();

        LogBM25SearchCompleted(_logger, sortedResults.Count);

        return sortedResults.AsReadOnly();
    }

    /// <summary>
    /// Indexes a document chunk into the in-memory inverted index.
    /// </summary>
    private async Task IndexChunkCoreAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        if (chunk == null)
            return;

        LogIndexingChunkStarted(_logger, chunk.Id);

        var index = _indexes.GetOrAdd("default", _ => new BM25Index());

        await IndexChunkAsync(chunk, index, cancellationToken);

        // Update index statistics
        await UpdateIndexStatisticsAsync(index, cancellationToken);

        await AutoSaveIfEnabledAsync(cancellationToken);

        LogIndexingChunkCompleted(_logger, chunk.Id);
    }

    /// <summary>
    /// Gets index statistics in the internal sparse shape.
    /// </summary>
    private async Task<SparseIndexStatistics> GetIndexStatisticsCoreAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var defaultIndex = _indexes.GetOrAdd("default", _ => new BM25Index());

        lock (_lockObject)
        {
            var topTerms = defaultIndex.TermFrequencies
                .OrderByDescending(tf => tf.Value)
                .Take(100)
                .ToDictionary(tf => tf.Key, tf => (long)tf.Value);

            return new SparseIndexStatistics
            {
                DocumentCount = defaultIndex.DocumentCount,
                UniqueTermCount = defaultIndex.TermFrequencies.Count,
                TotalTermOccurrences = defaultIndex.TermFrequencies.Values.Sum(),
                AverageDocumentLength = defaultIndex.DocumentCount > 0
                    ? defaultIndex.TotalDocumentLength / (double)defaultIndex.DocumentCount
                    : 0,
                IndexSizeBytes = EstimateIndexSize(defaultIndex),
                LastOptimizedAt = defaultIndex.LastOptimizedAt,
                TopFrequentTerms = topTerms
            };
        }
    }

    /// <summary>
    /// Optimize the index
    /// </summary>
    public async Task OptimizeIndexAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            LogIndexOptimizationStarted(_logger);

            var defaultIndex = _indexes.GetOrAdd("default", _ => new BM25Index());

            lock (_lockObject)
            {
                // Remove terms with frequency of 1 (optional)
                var lowFrequencyTerms = defaultIndex.TermFrequencies
                    .Where(tf => tf.Value <= 1)
                    .Select(tf => tf.Key)
                    .ToList();

                foreach (var term in lowFrequencyTerms)
                {
                    defaultIndex.TermFrequencies.TryRemove(term, out _);
                    defaultIndex.InvertedIndex.TryRemove(term, out _);
                }

                defaultIndex.LastOptimizedAt = DateTime.UtcNow;

                LogIndexOptimizationCompleted(_logger, lowFrequencyTerms.Count);
            }
        }, cancellationToken);

        await AutoSaveIfEnabledAsync(cancellationToken);
    }

    #region Persistence Methods

    /// <inheritdoc />
    public async Task SaveIndexAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _persistenceLock.WaitAsync(cancellationToken);
        try
        {
            LogSavingIndex(_logger, filePath);

            var data = new BM25IndexData
            {
                Version = 1,
                SavedAt = DateTime.UtcNow,
                Indexes = _indexes.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new BM25IndexSerializable
                    {
                        DocumentCount = kvp.Value.DocumentCount,
                        TotalDocumentLength = kvp.Value.TotalDocumentLength,
                        LastOptimizedAt = kvp.Value.LastOptimizedAt,
                        TermFrequencies = kvp.Value.TermFrequencies.ToDictionary(tf => tf.Key, tf => tf.Value),
                        InvertedIndex = kvp.Value.InvertedIndex.ToDictionary(
                            ii => ii.Key,
                            ii => ii.Value.Select(p => new PostingSerializable
                            {
                                ChunkId = p.ChunkId,
                                TermFrequency = p.TermFrequency,
                                DocumentLength = p.DocumentLength
                            }).ToList()
                        ),
                        Documents = kvp.Value.DocumentIndex.Select(d => new ChunkSerializable
                        {
                            Id = d.Key,
                            DocumentId = d.Value.DocumentId,
                            Content = d.Value.Content,
                            ChunkIndex = d.Value.ChunkIndex
                        }).ToList()
                    }
                )
            };

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, data, s_persistenceJsonOptions, cancellationToken);

            var totalDocs = _indexes.Values.Sum(i => i.DocumentCount);
            LogIndexSaved(_logger, totalDocs);
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task LoadIndexAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"BM25 index file not found: {filePath}");
        }

        await _persistenceLock.WaitAsync(cancellationToken);
        try
        {
            LogLoadingIndex(_logger, filePath);

            await using var stream = File.OpenRead(filePath);
            var data = await JsonSerializer.DeserializeAsync<BM25IndexData>(stream, cancellationToken: cancellationToken);

            if (data?.Indexes == null)
            {
                throw new InvalidDataException("Invalid BM25 index data format");
            }

            _indexes.Clear();

            foreach (var indexKvp in data.Indexes)
            {
                var index = new BM25Index
                {
                    DocumentCount = indexKvp.Value.DocumentCount,
                    TotalDocumentLength = indexKvp.Value.TotalDocumentLength,
                    LastOptimizedAt = indexKvp.Value.LastOptimizedAt
                };

                // Restore term frequencies
                foreach (var tf in indexKvp.Value.TermFrequencies)
                {
                    index.TermFrequencies[tf.Key] = tf.Value;
                }

                // Restore inverted index
                foreach (var ii in indexKvp.Value.InvertedIndex)
                {
                    index.InvertedIndex[ii.Key] = ii.Value
                        .Select(p => new Posting(p.ChunkId, p.TermFrequency, p.DocumentLength))
                        .ToList();
                }

                // Restore documents
                foreach (var doc in indexKvp.Value.Documents)
                {
                    var chunk = DocumentChunk.Create(
                        doc.DocumentId ?? string.Empty,
                        doc.Content ?? string.Empty,
                        doc.ChunkIndex,
                        1
                    );
                    index.DocumentIndex[doc.Id] = chunk;
                }

                _indexes[indexKvp.Key] = index;
            }

            var totalDocs = _indexes.Values.Sum(i => i.DocumentCount);
            LogIndexLoaded(_logger, totalDocs);
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    private async Task AutoSaveIfEnabledAsync(CancellationToken cancellationToken)
    {
        if (_autoSave && !string.IsNullOrEmpty(_persistencePath))
        {
            await SaveIndexAsync(_persistencePath, cancellationToken);
        }
    }

    #endregion

    #region Private Methods

    private static async Task<IReadOnlyList<SparseSearchResult>> SearchInIndexAsync(
        IReadOnlyList<string> searchTerms,
        BM25Index index,
        SparseSearchOptions options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var results = new Dictionary<string, SparseSearchResult>();
        var avgDocLength = index.DocumentCount > 0
            ? index.TotalDocumentLength / (double)index.DocumentCount
            : 0;

        foreach (var term in searchTerms)
        {
            if (!index.InvertedIndex.TryGetValue(term, out var postings))
                continue;

            var df = postings.Count;
            var idf = Math.Log(1 + (index.DocumentCount - df + 0.5) / (df + 0.5));

            foreach (var posting in postings)
            {
                var chunkId = posting.ChunkId;
                var tf = posting.TermFrequency;
                var docLength = posting.DocumentLength;

                // Calculate BM25 score
                var bm25Score = CalculateBM25Score(tf, df, index.DocumentCount, docLength, avgDocLength, options);

                if (results.TryGetValue(chunkId, out var existingResult))
                {
                    // Accumulate score for existing result
                    var newScore = existingResult.Score + bm25Score;
                    var newMatchedTerms = existingResult.MatchedTerms.Concat(new[] { term }).Distinct().ToList();
                    var newTermFreqs = new Dictionary<string, int>(existingResult.TermFrequencies) { [term] = tf };

                    results[chunkId] = existingResult with
                    {
                        Score = newScore,
                        MatchedTerms = newMatchedTerms.AsReadOnly(),
                        TermFrequencies = newTermFreqs
                    };
                }
                else
                {
                    // Create new result
                    if (index.DocumentIndex.TryGetValue(chunkId, out var chunk))
                    {
                        results[chunkId] = new SparseSearchResult
                        {
                            Chunk = chunk,
                            Score = bm25Score,
                            MatchedTerms = new[] { term },
                            TermFrequencies = new Dictionary<string, int> { [term] = tf },
                            DocumentLength = docLength,
                            ScoreComponents = CreateBM25Components(tf, idf, docLength, avgDocLength, bm25Score, term)
                        };
                    }
                }
            }
        }

        return results.Values.ToList().AsReadOnly();
    }

    private static double CalculateBM25Score(int tf, int df, long totalDocs, int docLength, double avgDocLength, SparseSearchOptions options)
    {
        var k1 = options.K1;
        var b = options.B;

        // IDF calculation
        var idf = Math.Log(1 + (totalDocs - df + 0.5) / (df + 0.5));

        // TF normalization
        var normalizedTf = (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * (docLength / avgDocLength)));

        return idf * normalizedTf;
    }

    private static BM25Components CreateBM25Components(int tf, double idf, int docLength, double avgDocLength, double finalScore, string term)
    {
        var tfScore = tf / (double)(tf + DefaultK1 * (1 - DefaultB + DefaultB * (docLength / avgDocLength)));
        var docLengthNorm = docLength / avgDocLength;

        return new BM25Components
        {
            TermFrequencyScore = tfScore,
            InverseDocumentFrequencyScore = idf,
            DocumentLengthNormalization = docLengthNorm,
            FinalScore = finalScore,
            TermScores = new Dictionary<string, double> { [term] = finalScore }
        };
    }

    private async Task IndexChunkAsync(DocumentChunk chunk, BM25Index index, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var tokens = TokenizeContent(chunk.Content);
        var termFrequencies = CountTermFrequencies(tokens);

        lock (_lockObject)
        {
            // Add chunk to document index
            index.DocumentIndex[chunk.Id] = chunk;

            // Update inverted index for each term
            foreach (var termFreq in termFrequencies)
            {
                var term = termFreq.Key;
                var frequency = termFreq.Value;

                // Update global term frequency
                index.TermFrequencies.AddOrUpdate(term, frequency, (_, existing) => existing + frequency);

                // Update inverted index
                index.InvertedIndex.AddOrUpdate(term,
                    new List<Posting> { new(chunk.Id, frequency, tokens.Count) },
                    (_, existing) =>
                    {
                        var updatedList = new List<Posting>(existing) { new(chunk.Id, frequency, tokens.Count) };
                        return updatedList;
                    });
            }

            // Update index statistics
            index.DocumentCount++;
            index.TotalDocumentLength += tokens.Count;
        }
    }

    private static async Task UpdateIndexStatisticsAsync(BM25Index index, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        // Additional statistics update logic can be implemented here
    }

    private static IReadOnlyList<string> TokenizeQuery(string query, SparseSearchOptions options)
    {
        var tokens = TokenizeContent(query);

        if (options.EnableTermExpansion)
        {
            // Stemming, synonym expansion, etc. (basic implementation)
            tokens = ExpandTerms(tokens);
        }

        return tokens;
    }

    private static IReadOnlyList<string> TokenizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<string>();

        // Basic tokenization: word splitting, lowercase, remove special characters
        var tokens = Regex.Split(content.ToLowerInvariant(), @"\W+")
            .Where(token => !string.IsNullOrWhiteSpace(token) && token.Length > 1)
            .ToList();

        return tokens.AsReadOnly();
    }

    private static IReadOnlyList<string> ExpandTerms(IReadOnlyList<string> terms)
    {
        // Basic implementation: return original without stemming or synonym expansion
        // In real implementation, use Porter Stemmer or synonym dictionary
        return terms;
    }

    private static Dictionary<string, int> CountTermFrequencies(IReadOnlyList<string> tokens)
    {
        var frequencies = new Dictionary<string, int>();

        foreach (var token in tokens)
        {
            frequencies[token] = frequencies.GetValueOrDefault(token, 0) + 1;
        }

        return frequencies;
    }

    private static long EstimateIndexSize(BM25Index index)
    {
        // Approximate index size calculation
        var termCount = index.TermFrequencies.Count;
        var postingCount = index.InvertedIndex.Values.Sum(postings => postings.Count);

        // Average 8 bytes per term + 16 bytes per posting
        return (termCount * 8) + (postingCount * 16);
    }

    #endregion

    #region IKeywordSearchService Implementation

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeywordSearchResult>> SearchAsync(
        string query,
        KeywordSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var metadataFilter = options?.MetadataFilter is { Count: > 0 }
            ? KeywordMetadataFilter.Expand(options.MetadataFilter, nameof(options))
            : null;

        // SparseSearchOptions has no document scope, so this option used to be accepted and dropped
        // on this backend while the relational ones honoured it - the same silent-ignore class that
        // was closed on the SQL side. An option the contract declares must either work or not exist.
        var documentScope = string.IsNullOrWhiteSpace(options?.DocumentIdFilter)
            ? null
            : options!.DocumentIdFilter;

        var restricted = metadataFilter is not null || documentScope is not null;

        var sparseOptions = options != null
            ? new SparseSearchOptions
            {
                // A restricted search ranks the whole candidate set and truncates afterwards. Taking
                // MaxResults first and filtering the survivors is the false-negative structure this
                // filter exists to avoid: a scope whose documents lose the global ranking race would
                // come back empty for a query its documents match. Ranking every candidate is
                // affordable here because this backend holds the index in memory anyway.
                MaxResults = restricted ? int.MaxValue : options.MaxResults,
                MinScore = options.MinScore,
                K1 = options.K1,
                B = options.B,
                EnableTermExpansion = options.EnableTermExpansion,
                EnablePhraseSearch = options.EnablePhraseSearch
            }
            : null;

        var results = await SearchCoreAsync(query, sparseOptions, cancellationToken);

        IEnumerable<SparseSearchResult> projected = results;
        if (restricted)
        {
            if (documentScope is not null)
                projected = projected.Where(r => r.Chunk?.DocumentId == documentScope);

            if (metadataFilter is not null)
                projected = projected.Where(r => KeywordMetadataFilter.Matches(r.Chunk?.Metadata, metadataFilter));

            projected = projected.Take(options!.MaxResults);
        }

        return projected.Select(r => new KeywordSearchResult
        {
            Chunk = r.Chunk,
            Score = r.Score,
            MatchedTerms = r.MatchedTerms,
            TermFrequencies = r.TermFrequencies,
            DocumentLength = r.DocumentLength
        }).ToList();
    }

    /// <inheritdoc />
    public Task<int> DeleteByFilterAsync(
        IReadOnlyDictionary<string, object> filter,
        CancellationToken cancellationToken = default)
    {
        var expanded = KeywordMetadataFilter.Expand(filter, nameof(filter));
        var defaultIndex = _indexes.GetOrAdd("default", _ => new BM25Index());

        lock (_lockObject)
        {
            var chunkIds = defaultIndex.DocumentIndex
                .Where(kvp => KeywordMetadataFilter.Matches(kvp.Value?.Metadata, expanded))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var chunkId in chunkIds)
            {
                defaultIndex.DocumentIndex.TryRemove(chunkId, out _);

                foreach (var kvp in defaultIndex.InvertedIndex)
                {
                    kvp.Value.RemoveAll(p => p.ChunkId == chunkId);
                }
            }

            defaultIndex.DocumentCount = defaultIndex.DocumentIndex.Count;
            return Task.FromResult(chunkIds.Count);
        }
    }

    /// <inheritdoc />
    public Task IndexChunkAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        return IndexChunkCoreAsync(chunk, cancellationToken);
    }

    /// <inheritdoc />
    public async Task IndexChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            await IndexChunkCoreAsync(chunk, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task DeleteChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chunkId))
            return Task.CompletedTask;

        var defaultIndex = _indexes.GetOrAdd("default", _ => new BM25Index());

        lock (_lockObject)
        {
            // Remove from document index
            if (defaultIndex.DocumentIndex.TryRemove(chunkId, out _))
            {
                // Remove from inverted index
                foreach (var kvp in defaultIndex.InvertedIndex)
                {
                    kvp.Value.RemoveAll(p => p.ChunkId == chunkId);
                }

                // Recalculate document count
                defaultIndex.DocumentCount = defaultIndex.DocumentIndex.Count;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return Task.CompletedTask;

        var defaultIndex = _indexes.GetOrAdd("default", _ => new BM25Index());

        lock (_lockObject)
        {
            // Find all chunks for this document
            var chunkIds = defaultIndex.DocumentIndex
                .Where(kvp => kvp.Value.DocumentId == documentId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var chunkId in chunkIds)
            {
                defaultIndex.DocumentIndex.TryRemove(chunkId, out _);

                foreach (var kvp in defaultIndex.InvertedIndex)
                {
                    kvp.Value.RemoveAll(p => p.ChunkId == chunkId);
                }
            }

            defaultIndex.DocumentCount = defaultIndex.DocumentIndex.Count;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearIndexAsync(CancellationToken cancellationToken = default)
    {
        lock (_lockObject)
        {
            _indexes.Clear();
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<KeywordIndexStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetIndexStatisticsCoreAsync(cancellationToken);

        return new KeywordIndexStatistics
        {
            TotalDocuments = stats.DocumentCount,
            TotalTerms = stats.UniqueTermCount,
            TotalTermOccurrences = stats.TotalTermOccurrences,
            AverageDocumentLength = stats.AverageDocumentLength,
            IndexSizeBytes = stats.IndexSizeBytes,
            LastOptimizedAt = stats.LastOptimizedAt,
            TopFrequentTerms = stats.TopFrequentTerms
        };
    }

    /// <inheritdoc />
    public Task RefreshIDFCacheAsync(CancellationToken cancellationToken = default)
    {
        // IDF is computed dynamically in this implementation, no cache to refresh
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public double GetIDF(string term)
    {
        var defaultIndex = _indexes.GetOrAdd("default", _ => new BM25Index());

        if (!defaultIndex.InvertedIndex.TryGetValue(term.ToLowerInvariant(), out var postings))
            return 0;

        var df = postings.Count;
        var totalDocs = defaultIndex.DocumentCount;

        if (totalDocs == 0 || df == 0)
            return 0;

        return Math.Log(1 + (totalDocs - df + 0.5) / (df + 0.5));
    }

    /// <inheritdoc />
    public IEnumerable<string> Tokenize(string text)
    {
        return TokenizeContent(text);
    }

    #endregion

    #region LoggerMessage Definitions

    [LoggerMessage(Level = LogLevel.Information, Message = "BM25 search started: {Query}")]
    private static partial void LogBM25SearchStarted(ILogger logger, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "BM25 search completed: {ResultCount} results")]
    private static partial void LogBM25SearchCompleted(ILogger logger, int resultCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing chunk started: {ChunkId}")]
    private static partial void LogIndexingChunkStarted(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexing chunk completed: {ChunkId}")]
    private static partial void LogIndexingChunkCompleted(ILogger logger, string chunkId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Index optimization started")]
    private static partial void LogIndexOptimizationStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Index optimization completed: {RemovedTerms} low-frequency terms removed")]
    private static partial void LogIndexOptimizationCompleted(ILogger logger, int removedTerms);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saving BM25 index to {FilePath}")]
    private static partial void LogSavingIndex(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "BM25 index saved successfully: {DocumentCount} documents")]
    private static partial void LogIndexSaved(ILogger logger, long documentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading BM25 index from {FilePath}")]
    private static partial void LogLoadingIndex(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "BM25 index loaded successfully: {DocumentCount} documents")]
    private static partial void LogIndexLoaded(ILogger logger, long documentCount);

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        _persistenceLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

#region Data Structures

/// <summary>
/// BM25 index data structure
/// </summary>
internal sealed class BM25Index
{
    public ConcurrentDictionary<string, DocumentChunk> DocumentIndex { get; } = new();
    public ConcurrentDictionary<string, int> TermFrequencies { get; } = new();
    public ConcurrentDictionary<string, List<Posting>> InvertedIndex { get; } = new();
    public long DocumentCount { get; set; }
    public long TotalDocumentLength { get; set; }
    public DateTime LastOptimizedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Posting information (document info where term appears)
/// </summary>
internal sealed record Posting(string ChunkId, int TermFrequency, int DocumentLength);

#endregion

#region Persistence Data Classes

internal sealed class BM25IndexData
{
    public int Version { get; set; }
    public DateTime SavedAt { get; set; }
    public Dictionary<string, BM25IndexSerializable> Indexes { get; set; } = new();
}

internal sealed class BM25IndexSerializable
{
    public long DocumentCount { get; set; }
    public long TotalDocumentLength { get; set; }
    public DateTime LastOptimizedAt { get; set; }
    public Dictionary<string, int> TermFrequencies { get; set; } = new();
    public Dictionary<string, List<PostingSerializable>> InvertedIndex { get; set; } = new();
    public List<ChunkSerializable> Documents { get; set; } = new();
}

internal sealed class PostingSerializable
{
    public string ChunkId { get; set; } = string.Empty;
    public int TermFrequency { get; set; }
    public int DocumentLength { get; set; }
}

internal sealed class ChunkSerializable
{
    public string Id { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string? Content { get; set; }
    public int ChunkIndex { get; set; }
}

#endregion
