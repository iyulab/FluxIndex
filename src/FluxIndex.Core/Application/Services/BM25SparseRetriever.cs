using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// BM25 기반 희소 검색 구현체
/// </summary>
public class BM25SparseRetriever : ISparseRetriever
{
    private readonly ILogger<BM25SparseRetriever> _logger;
    private readonly ConcurrentDictionary<string, BM25Index> _indexes;
    private readonly object _lockObject = new();

    // BM25 기본 매개변수
    private const double DefaultK1 = 1.2;
    private const double DefaultB = 0.75;

    public BM25SparseRetriever(ILogger<BM25SparseRetriever> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _indexes = new ConcurrentDictionary<string, BM25Index>();
    }

    /// <summary>
    /// BM25 키워드 검색 실행
    /// </summary>
    public async Task<IReadOnlyList<SparseSearchResult>> SearchAsync(
        string query,
        SparseSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SparseSearchResult>();

        options ??= new SparseSearchOptions();

        _logger.LogInformation("BM25 검색 시작: {Query}", query);

        var searchTerms = TokenizeQuery(query, options);
        if (!searchTerms.Any())
            return Array.Empty<SparseSearchResult>();

        var results = new List<SparseSearchResult>();

        // 모든 인덱스에서 검색
        foreach (var indexKvp in _indexes)
        {
            var index = indexKvp.Value;
            var indexResults = await SearchInIndexAsync(searchTerms, index, options, cancellationToken);
            results.AddRange(indexResults);
        }

        // 점수 기준 정렬 및 상위 결과 반환
        var sortedResults = results
            .Where(r => r.Score >= options.MinScore)
            .OrderByDescending(r => r.Score)
            .Take(options.MaxResults)
            .ToList();

        _logger.LogInformation("BM25 검색 완료: {ResultCount}개 결과", sortedResults.Count);

        return sortedResults.AsReadOnly();
    }

    /// <summary>
    /// 문서 청크 인덱싱
    /// </summary>
    public async Task IndexDocumentAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null)
            return;

        _logger.LogInformation("청크 인덱싱 시작: {ChunkId}", chunk.Id);

        var index = _indexes.GetOrAdd("default", _ => new BM25Index());

        await IndexChunkAsync(chunk, index, cancellationToken);

        // 인덱스 통계 업데이트
        await UpdateIndexStatisticsAsync(index, cancellationToken);

        _logger.LogInformation("청크 인덱싱 완료: {ChunkId}", chunk.Id);
    }

    /// <summary>
    /// 인덱스 통계 조회
    /// </summary>
    public async Task<SparseIndexStatistics> GetIndexStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // 비동기 인터페이스 준수

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
    /// 인덱스 최적화
    /// </summary>
    public async Task OptimizeIndexAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation("인덱스 최적화 시작");

            var defaultIndex = _indexes.GetOrAdd("default", _ => new BM25Index());

            lock (_lockObject)
            {
                // 빈도가 1인 용어 제거 (선택적)
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

                _logger.LogInformation("인덱스 최적화 완료: {RemovedTerms}개 저빈도 용어 제거",
                    lowFrequencyTerms.Count);
            }
        }, cancellationToken);
    }

    #region Private Methods

    private async Task<IReadOnlyList<SparseSearchResult>> SearchInIndexAsync(
        IReadOnlyList<string> searchTerms,
        BM25Index index,
        SparseSearchOptions options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // 비동기 인터페이스 준수

        var results = new Dictionary<string, SparseSearchResult>();
        var avgDocLength = index.DocumentCount > 0
            ? index.TotalDocumentLength / (double)index.DocumentCount
            : 0;

        foreach (var term in searchTerms)
        {
            if (!index.InvertedIndex.TryGetValue(term, out var postings))
                continue;

            var df = postings.Count; // 문서 빈도
            var idf = Math.Log((index.DocumentCount - df + 0.5) / (df + 0.5)); // BM25 IDF

            foreach (var posting in postings)
            {
                var chunkId = posting.ChunkId;
                var tf = posting.TermFrequency;
                var docLength = posting.DocumentLength;

                // BM25 점수 계산
                var bm25Score = CalculateBM25Score(tf, df, index.DocumentCount, docLength, avgDocLength, options);

                if (results.TryGetValue(chunkId, out var existingResult))
                {
                    // 기존 결과에 점수 누적
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
                    // 새 결과 생성
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

        // IDF 계산
        var idf = Math.Log((totalDocs - df + 0.5) / (df + 0.5));

        // TF 정규화
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
        await Task.CompletedTask; // 비동기 인터페이스 준수

        var tokens = TokenizeContent(chunk.Content);
        var termFrequencies = CountTermFrequencies(tokens);

        lock (_lockObject)
        {
            // 문서 인덱스에 청크 추가
            index.DocumentIndex[chunk.Id] = chunk;

            // 각 용어에 대해 역인덱스 업데이트
            foreach (var termFreq in termFrequencies)
            {
                var term = termFreq.Key;
                var frequency = termFreq.Value;

                // 전체 용어 빈도 업데이트
                index.TermFrequencies.AddOrUpdate(term, frequency, (_, existing) => existing + frequency);

                // 역인덱스 업데이트
                index.InvertedIndex.AddOrUpdate(term,
                    new List<Posting> { new(chunk.Id, frequency, tokens.Count) },
                    (_, existing) =>
                    {
                        var updatedList = new List<Posting>(existing) { new(chunk.Id, frequency, tokens.Count) };
                        return updatedList;
                    });
            }

            // 인덱스 통계 업데이트
            index.DocumentCount++;
            index.TotalDocumentLength += tokens.Count;
        }
    }

    private async Task UpdateIndexStatisticsAsync(BM25Index index, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // 비동기 인터페이스 준수
        // 추가 통계 업데이트 로직이 필요하면 여기에 구현
    }

    private IReadOnlyList<string> TokenizeQuery(string query, SparseSearchOptions options)
    {
        var tokens = TokenizeContent(query);

        if (options.EnableTermExpansion)
        {
            // 스테밍, 동의어 확장 등 (기본 구현)
            tokens = ExpandTerms(tokens);
        }

        return tokens;
    }

    private IReadOnlyList<string> TokenizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<string>();

        // 기본 토큰화: 단어 분리, 소문자 변환, 특수문자 제거
        var tokens = Regex.Split(content.ToLowerInvariant(), @"\W+")
            .Where(token => !string.IsNullOrWhiteSpace(token) && token.Length > 1)
            .ToList();

        return tokens.AsReadOnly();
    }

    private IReadOnlyList<string> ExpandTerms(IReadOnlyList<string> terms)
    {
        // 기본 구현: 스테밍이나 동의어 확장 없이 원본 반환
        // 실제 구현에서는 Porter Stemmer나 동의어 사전 사용
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
        // 대략적인 인덱스 크기 계산
        var termCount = index.TermFrequencies.Count;
        var postingCount = index.InvertedIndex.Values.Sum(postings => postings.Count);

        // 용어당 평균 8바이트 + 포스팅당 평균 16바이트
        return (termCount * 8) + (postingCount * 16);
    }

    #endregion
}

#region Data Structures

/// <summary>
/// BM25 인덱스 데이터 구조
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
/// 포스팅 정보 (용어가 출현하는 문서 정보)
/// </summary>
internal record Posting(string ChunkId, int TermFrequency, int DocumentLength);

#endregion