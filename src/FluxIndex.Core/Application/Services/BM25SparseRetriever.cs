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
/// Implements both legacy ISparseRetriever and new unified IKeywordSearchService interfaces.
/// </summary>
public class BM25SparseRetriever : ISparseRetriever, IKeywordSearchService, IPersistableSparseRetriever
{
    private readonly ILogger<BM25SparseRetriever> _logger;
    private readonly ConcurrentDictionary<string, BM25Index> _indexes;
    private readonly object _lockObject = new();
    private readonly string? _persistencePath;
    private readonly bool _autoSave;
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);

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
    /// Execute BM25 keyword search
    /// </summary>
    public async Task<IReadOnlyList<SparseSearchResult>> SearchAsync(
        string query,
        SparseSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SparseSearchResult>();

        options ??= new SparseSearchOptions();

        _logger.LogInformation("BM25 search started: {Query}", query);

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

        _logger.LogInformation("BM25 search completed: {ResultCount} results", sortedResults.Count);

        return sortedResults.AsReadOnly();
    }

    /// <summary>
    /// Index a document chunk
    /// </summary>
    public async Task IndexDocumentAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            return;

        _logger.LogInformation("Indexing chunk started: {ChunkId}", chunk.Id);

        var index = _indexes.GetOrAdd("default", _ => new BM25Index());

        await IndexChunkAsync(chunk, index, cancellationToken);

        // Update index statistics
        await UpdateIndexStatisticsAsync(index, cancellationToken);

        await AutoSaveIfEnabledAsync(cancellationToken);

        _logger.LogInformation("Indexing chunk completed: {ChunkId}", chunk.Id);
    }

    /// <summary>
    /// Get index statistics
    /// </summary>
    public async Task<SparseIndexStatistics> GetIndexStatisticsAsync(CancellationToken cancellationToken = default)
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
            _logger.LogInformation("Index optimization started");

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

                _logger.LogInformation("Index optimization completed: {RemovedTerms} low-frequency terms removed",
                    lowFrequencyTerms.Count);
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
            _logger.LogInformation("Saving BM25 index to {FilePath}", filePath);

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

            var options = new JsonSerializerOptions
            {
                WriteIndented = false
            };

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken);

            _logger.LogInformation("BM25 index saved successfully: {DocumentCount} documents",
                _indexes.Values.Sum(i => i.DocumentCount));
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
            _logger.LogInformation("Loading BM25 index from {FilePath}", filePath);

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

            _logger.LogInformation("BM25 index loaded successfully: {DocumentCount} documents",
                _indexes.Values.Sum(i => i.DocumentCount));
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

    private async Task<IReadOnlyList<SparseSearchResult>> SearchInIndexAsync(
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
            var idf = Math.Log((index.DocumentCount - df + 0.5) / (df + 0.5));

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

    private double CalculateBM25Score(int tf, int df, long totalDocs, int docLength, double avgDocLength, SparseSearchOptions options)
    {
        var k1 = options.K1;
        var b = options.B;

        // IDF calculation
        var idf = Math.Log((totalDocs - df + 0.5) / (df + 0.5));

        // TF normalization
        var normalizedTf = (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * (docLength / avgDocLength)));

        return idf * normalizedTf;
    }

    private BM25Components CreateBM25Components(int tf, double idf, int docLength, double avgDocLength, double finalScore, string term)
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

    private async Task UpdateIndexStatisticsAsync(BM25Index index, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        // Additional statistics update logic can be implemented here
    }

    private IReadOnlyList<string> TokenizeQuery(string query, SparseSearchOptions options)
    {
        var tokens = TokenizeContent(query);

        if (options.EnableTermExpansion)
        {
            // Stemming, synonym expansion, etc. (basic implementation)
            tokens = ExpandTerms(tokens);
        }

        return tokens;
    }

    private IReadOnlyList<string> TokenizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<string>();

        // Basic tokenization: word splitting, lowercase, remove special characters
        var tokens = Regex.Split(content.ToLowerInvariant(), @"\W+")
            .Where(token => !string.IsNullOrWhiteSpace(token) && token.Length > 1)
            .ToList();

        return tokens.AsReadOnly();
    }

    private IReadOnlyList<string> ExpandTerms(IReadOnlyList<string> terms)
    {
        // Basic implementation: return original without stemming or synonym expansion
        // In real implementation, use Porter Stemmer or synonym dictionary
        return terms;
    }

    private Dictionary<string, int> CountTermFrequencies(IReadOnlyList<string> tokens)
    {
        var frequencies = new Dictionary<string, int>();

        foreach (var token in tokens)
        {
            frequencies[token] = frequencies.GetValueOrDefault(token, 0) + 1;
        }

        return frequencies;
    }

    private long EstimateIndexSize(BM25Index index)
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
    async Task<IReadOnlyList<KeywordSearchResult>> IKeywordSearchService.SearchAsync(
        string query,
        KeywordSearchOptions? options,
        CancellationToken cancellationToken)
    {
        var sparseOptions = options != null
            ? new SparseSearchOptions
            {
                MaxResults = options.MaxResults,
                MinScore = options.MinScore,
                K1 = options.K1,
                B = options.B,
                EnableTermExpansion = options.EnableTermExpansion,
                EnablePhraseSearch = options.EnablePhraseSearch
            }
            : null;

        var results = await SearchAsync(query, sparseOptions, cancellationToken);

        return results.Select(r => new KeywordSearchResult
        {
            Chunk = r.Chunk,
            Score = r.Score,
            MatchedTerms = r.MatchedTerms,
            TermFrequencies = r.TermFrequencies,
            DocumentLength = r.DocumentLength
        }).ToList();
    }

    /// <inheritdoc />
    public Task IndexChunkAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        return IndexDocumentAsync(chunk, cancellationToken);
    }

    /// <inheritdoc />
    public async Task IndexChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            await IndexDocumentAsync(chunk, cancellationToken);
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
    async Task<KeywordIndexStatistics> IKeywordSearchService.GetStatisticsAsync(CancellationToken cancellationToken)
    {
        var stats = await GetIndexStatisticsAsync(cancellationToken);

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

        return Math.Log((totalDocs - df + 0.5) / (df + 0.5));
    }

    /// <inheritdoc />
    public IEnumerable<string> Tokenize(string text)
    {
        return TokenizeContent(text);
    }

    #endregion
}

#region Data Structures

/// <summary>
/// BM25 index data structure
/// </summary>
internal class BM25Index
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
internal record Posting(string ChunkId, int TermFrequency, int DocumentLength);

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
