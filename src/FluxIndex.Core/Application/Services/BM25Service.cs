using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Services;

/// <summary>
/// Implementation of BM25 (Best Matching 25) ranking algorithm
/// </summary>
public class BM25Service : IBM25Service
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IVectorStore _vectorStore;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BM25Service> _logger;
    
    // BM25 parameters
    private readonly float _k1; // Controls term frequency saturation (typically 1.2)
    private readonly float _b;  // Controls length normalization (typically 0.75)
    
    // IDF cache
    private readonly ConcurrentDictionary<string, float> _idfCache;
    private float _averageDocumentLength;
    private int _totalDocuments;
    
    // Regex for tokenization
    private static readonly Regex TokenizerRegex = new Regex(@"\b\w+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KoreanTokenizerRegex = new Regex(@"[\uAC00-\uD7AF]+|\w+", RegexOptions.Compiled);

    public BM25Service(
        IDocumentRepository documentRepository,
        IVectorStore vectorStore,
        IMemoryCache? cache = null,
        ILogger<BM25Service>? logger = null,
        float k1 = 1.2f,
        float b = 0.75f)
    {
        _documentRepository = documentRepository;
        _vectorStore = vectorStore;
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        _logger = logger ?? new NullLogger<BM25Service>();
        _k1 = k1;
        _b = b;
        _idfCache = new ConcurrentDictionary<string, float>();
        _averageDocumentLength = 0;
        _totalDocuments = 0;
    }

    /// <summary>
    /// Calculates BM25 score for a query-document pair
    /// BM25 formula: Σ(IDF(qi) * (f(qi, D) * (k1 + 1)) / (f(qi, D) + k1 * (1 - b + b * |D| / avgdl)))
    /// </summary>
    public float CalculateScore(string query, string document, float averageDocumentLength)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(document))
            return 0f;

        var queryTerms = Tokenize(query).ToList();
        var documentTerms = Tokenize(document).ToList();
        var documentLength = documentTerms.Count;
        
        if (documentLength == 0 || averageDocumentLength <= 0)
            return 0f;

        // Calculate term frequencies in document
        var termFrequencies = CalculateTermFrequencies(documentTerms);
        
        float score = 0f;
        foreach (var queryTerm in queryTerms.Distinct())
        {
            if (!termFrequencies.TryGetValue(queryTerm, out int termFrequency))
                continue;

            // Get IDF for the term
            float idf = GetIDF(queryTerm);
            
            // Calculate BM25 component for this term
            float numerator = termFrequency * (_k1 + 1);
            float denominator = termFrequency + _k1 * (1 - _b + _b * documentLength / averageDocumentLength);
            
            score += idf * (numerator / denominator);
        }

        return score;
    }

    /// <summary>
    /// Performs BM25 search across all documents
    /// </summary>
    public async Task<IEnumerable<BM25Result>> SearchAsync(
        string query,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("BM25 search for: {Query}", query);

        // Ensure IDF cache is populated
        if (_totalDocuments == 0)
        {
            await UpdateIDFCacheAsync(cancellationToken);
        }

        var queryTerms = Tokenize(query).ToList();
        if (!queryTerms.Any())
        {
            _logger.LogWarning("No valid tokens in query: {Query}", query);
            return Enumerable.Empty<BM25Result>();
        }

        // Search for documents containing any of the query terms
        var candidateDocuments = new List<BM25Result>();
        
        // Get all documents (in production, this should be optimized with inverted index)
        var allDocuments = await _documentRepository.GetAllAsync(cancellationToken);
        
        foreach (var document in allDocuments)
        {
            // Get chunks for the document
            var chunks = await _vectorStore.GetByDocumentIdAsync(document.Id, cancellationToken);
            
            foreach (var chunk in chunks)
            {
                var score = CalculateScore(query, chunk.Content, _averageDocumentLength);
                
                if (score > 0)
                {
                    candidateDocuments.Add(new BM25Result
                    {
                        DocumentId = document.Id,
                        ChunkId = chunk.Id,
                        Content = chunk.Content,
                        Score = score,
                        DocumentLength = Tokenize(chunk.Content).Count(),
                        TermFrequencies = CalculateTermFrequencies(Tokenize(chunk.Content)),
                        Metadata = chunk.Properties
                    });
                }
            }
        }

        // Sort by score and return top K
        var results = candidateDocuments
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        _logger.LogInformation("BM25 search found {Count} results", results.Count);
        return results;
    }

    /// <summary>
    /// Updates the IDF cache by analyzing the entire document corpus
    /// </summary>
    public async Task UpdateIDFCacheAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating IDF cache");

        var allDocuments = await _documentRepository.GetAllAsync(cancellationToken);
        _totalDocuments = allDocuments.Count();
        
        if (_totalDocuments == 0)
        {
            _logger.LogWarning("No documents found for IDF calculation");
            return;
        }

        // Document frequency map
        var documentFrequency = new ConcurrentDictionary<string, int>();
        var totalLength = 0;
        var documentCount = 0;

        foreach (var document in allDocuments)
        {
            var chunks = await _vectorStore.GetByDocumentIdAsync(document.Id, cancellationToken);
            
            foreach (var chunk in chunks)
            {
                var terms = Tokenize(chunk.Content).Distinct().ToList();
                totalLength += terms.Count;
                documentCount++;
                
                foreach (var term in terms)
                {
                    documentFrequency.AddOrUpdate(term, 1, (_, count) => count + 1);
                }
            }
        }

        // Calculate average document length
        _averageDocumentLength = documentCount > 0 ? (float)totalLength / documentCount : 0;

        // Calculate IDF for each term
        // IDF = log((N - df + 0.5) / (df + 0.5))
        foreach (var kvp in documentFrequency)
        {
            float idf = (float)Math.Log((_totalDocuments - kvp.Value + 0.5) / (kvp.Value + 0.5));
            _idfCache[kvp.Key] = Math.Max(0, idf); // Ensure non-negative
        }

        _logger.LogInformation("IDF cache updated with {Count} terms", _idfCache.Count);
    }

    /// <summary>
    /// Gets the IDF value for a term
    /// </summary>
    public float GetIDF(string term)
    {
        term = term.ToLowerInvariant();
        
        if (_idfCache.TryGetValue(term, out float idf))
        {
            return idf;
        }

        // Default IDF for unknown terms (rare terms)
        // Using a higher value to give some weight to unknown terms
        return (float)Math.Log(_totalDocuments + 1);
    }

    /// <summary>
    /// Tokenizes text into terms for BM25 processing
    /// Supports both English and Korean text
    /// </summary>
    public IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Enumerable.Empty<string>();

        // Check if text contains Korean characters
        bool hasKorean = text.Any(c => c >= '\uAC00' && c <= '\uD7AF');
        
        var regex = hasKorean ? KoreanTokenizerRegex : TokenizerRegex;
        var matches = regex.Matches(text);
        
        return matches
            .Cast<Match>()
            .Select(m => m.Value.ToLowerInvariant())
            .Where(token => token.Length > 1); // Filter out single characters
    }

    /// <summary>
    /// Calculates term frequencies in a document
    /// </summary>
    private Dictionary<string, int> CalculateTermFrequencies(IEnumerable<string> terms)
    {
        var frequencies = new Dictionary<string, int>();
        
        foreach (var term in terms)
        {
            var lowerTerm = term.ToLowerInvariant();
            if (frequencies.ContainsKey(lowerTerm))
            {
                frequencies[lowerTerm]++;
            }
            else
            {
                frequencies[lowerTerm] = 1;
            }
        }
        
        return frequencies;
    }
}