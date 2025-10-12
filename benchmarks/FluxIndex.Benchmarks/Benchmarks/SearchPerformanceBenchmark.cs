using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using FluxIndex.Benchmarks.TestData;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK;

namespace FluxIndex.Benchmarks.Benchmarks;

/// <summary>
/// 검색 성능 벤치마크 (Week 2 최적화 검증)
/// 목표: 510ms → 200-250ms 응답 시간 개선 확인
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class SearchPerformanceBenchmark
{
    private IFluxIndexContext _context = null!;
    private List<string> _simpleQueries = null!;
    private List<string> _complexQueries = null!;
    private List<string> _hybridQueries = null!;

    [Params(1000, 10000)]
    public int ChunkCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // In-Memory SQLite 사용 (빠른 벤치마크)
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding() // 테스트 데이터에 임베딩 포함됨
            .Build();

        // 테스트 데이터 인덱싱
        Console.WriteLine($"Indexing {ChunkCount} chunks for benchmark...");
        var chunks = SampleDocuments.GenerateChunks(ChunkCount, seed: 12345);

        // DocumentChunk를 Document로 변환
        var documents = ConvertChunksToDocuments(chunks);

        await _context.Indexer.IndexBatchAsync(documents, parallelism: 8);
        Console.WriteLine($"Indexing completed: {ChunkCount} chunks");

        // 쿼리 준비
        _simpleQueries = SampleQueries.GetSimpleKeywordQueries();
        _complexQueries = SampleQueries.GetComplexSemanticQueries();
        _hybridQueries = SampleQueries.GetHybridQueries();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        // Cleanup if needed
    }

    /// <summary>
    /// 단순 키워드 검색 (10-30 tokens)
    /// Week 2 최적화 전 평균: ~200-300ms
    /// Week 2 최적화 후 목표: ~100-150ms
    /// </summary>
    [Benchmark]
    public async Task SimpleKeywordSearch()
    {
        var query = _simpleQueries[0]; // "machine learning basics"
        var results = await _context.Retriever.SearchAsync(query, maxResults: 10);
    }

    /// <summary>
    /// 복잡한 의미론적 검색 (50-150 tokens)
    /// Week 2 최적화 전 평균: ~510ms
    /// Week 2 최적화 후 목표: ~200-250ms
    /// </summary>
    [Benchmark]
    public async Task ComplexSemanticSearch()
    {
        var query = _complexQueries[0]; // 장문 의미론적 쿼리
        var results = await _context.Retriever.SearchAsync(query, maxResults: 10);
    }

    /// <summary>
    /// 혼합 검색 (키워드 + 의미론적, 30-80 tokens)
    /// Week 2 최적화 전 평균: ~350-450ms
    /// Week 2 최적화 후 목표: ~150-200ms
    /// </summary>
    [Benchmark]
    public async Task HybridSearch()
    {
        var query = _hybridQueries[0]; // 혼합 쿼리
        var results = await _context.Retriever.SearchAsync(query, maxResults: 10);
    }

    /// <summary>
    /// 다양한 TopK 값으로 검색 (K=5, 10, 20, 50)
    /// TopK 증가에 따른 성능 영향 측정
    /// </summary>
    [Benchmark]
    [Arguments(5)]
    [Arguments(10)]
    [Arguments(20)]
    [Arguments(50)]
    public async Task SearchWithVariousTopK(int maxResults)
    {
        var query = _complexQueries[1];
        var results = await _context.Retriever.SearchAsync(query, maxResults: maxResults);
    }

    /// <summary>
    /// 배치 검색 (10개 쿼리 동시 처리)
    /// 동시 처리 성능 측정
    /// </summary>
    [Benchmark]
    public async Task BatchSearch()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var query = _simpleQueries[i % _simpleQueries.Count];
            tasks.Add(_context.Retriever.SearchAsync(query, maxResults: 10));
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 순차 검색 (10개 쿼리 순차 처리)
    /// 배치 검색과 비교용
    /// </summary>
    [Benchmark]
    public async Task SequentialSearch()
    {
        for (int i = 0; i < 10; i++)
        {
            var query = _simpleQueries[i % _simpleQueries.Count];
            await _context.Retriever.SearchAsync(query, maxResults: 10);
        }
    }

    /// <summary>
    /// DocumentChunk 리스트를 Document 리스트로 변환
    /// 10개 청크마다 1개 문서로 그룹화
    /// </summary>
    private List<Document> ConvertChunksToDocuments(List<FluxIndex.Domain.Models.DocumentChunk> chunks)
    {
        var documents = new List<Document>();
        var groupedChunks = chunks.GroupBy(c => c.DocumentId);

        foreach (var group in groupedChunks)
        {
            var document = new Document
            {
                Id = group.Key,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var chunk in group)
            {
                // DocumentChunk를 DocumentChunkEntity로 변환
                var entityChunk = new FluxIndex.Domain.Entities.DocumentChunk
                {
                    Id = chunk.Id,
                    DocumentId = chunk.DocumentId,
                    Content = chunk.Content,
                    ChunkIndex = chunk.ChunkIndex,
                    TotalChunks = group.Count(),
                    Embedding = chunk.Embedding,
                    TokenCount = chunk.TokenCount,
                    Metadata = chunk.Metadata ?? new Dictionary<string, object>()
                };

                document.AddChunk(entityChunk);
            }

            documents.Add(document);
        }

        return documents;
    }
}
