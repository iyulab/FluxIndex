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
/// 배치 인덱싱 성능 벤치마크 (Week 2 최적화 검증)
/// 목표: 5-10x 인덱싱 속도 개선 확인
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class BatchIndexingBenchmark
{
    private IFluxIndexContext _context = null!;
    private List<Document> _smallBatchDocuments = null!; // 100 chunks
    private List<Document> _mediumBatchDocuments = null!; // 1,000 chunks
    private List<Document> _largeBatchDocuments = null!; // 10,000 chunks

    [GlobalSetup]
    public void GlobalSetup()
    {
        // In-Memory SQLite 사용 (빠른 벤치마크)
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding() // 테스트 데이터에 임베딩 포함됨
            .Build();

        // 테스트 데이터 준비
        Console.WriteLine("Preparing test data for batch indexing benchmarks...");

        var smallChunks = SampleDocuments.GenerateSmallBatch();
        _smallBatchDocuments = ConvertChunksToDocuments(smallChunks);

        var mediumChunks = SampleDocuments.GenerateMediumBatch();
        _mediumBatchDocuments = ConvertChunksToDocuments(mediumChunks);

        var largeChunks = SampleDocuments.GenerateLargeBatch();
        _largeBatchDocuments = ConvertChunksToDocuments(largeChunks);

        Console.WriteLine($"Test data prepared:");
        Console.WriteLine($"  Small: {_smallBatchDocuments.Count} documents ({smallChunks.Count} chunks)");
        Console.WriteLine($"  Medium: {_mediumBatchDocuments.Count} documents ({mediumChunks.Count} chunks)");
        Console.WriteLine($"  Large: {_largeBatchDocuments.Count} documents ({largeChunks.Count} chunks)");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        // Cleanup if needed
    }

    /// <summary>
    /// 소규모 배치 인덱싱 (100 chunks)
    /// Week 2 최적화 전: ~500ms
    /// Week 2 최적화 후 목표: ~50-100ms (5-10x 개선)
    /// </summary>
    [Benchmark]
    [IterationSetup(Target = nameof(IndexSmallBatch_100Chunks))]
    public void SetupSmallBatch()
    {
        // 각 반복마다 깨끗한 상태 시작 (새 컨텍스트)
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding()
            .Build();
    }

    [Benchmark]
    public async Task IndexSmallBatch_100Chunks()
    {
        await _context.Indexer.IndexBatchAsync(_smallBatchDocuments, parallelism: 4);
    }

    /// <summary>
    /// 중규모 배치 인덱싱 (1,000 chunks)
    /// Week 2 최적화 전: ~5초
    /// Week 2 최적화 후 목표: ~500ms-1초 (5-10x 개선)
    /// </summary>
    [Benchmark]
    [IterationSetup(Target = nameof(IndexMediumBatch_1000Chunks))]
    public void SetupMediumBatch()
    {
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding()
            .Build();
    }

    [Benchmark]
    public async Task IndexMediumBatch_1000Chunks()
    {
        await _context.Indexer.IndexBatchAsync(_mediumBatchDocuments, parallelism: 8);
    }

    /// <summary>
    /// 대규모 배치 인덱싱 (10,000 chunks)
    /// Week 2 최적화 전: ~50초
    /// Week 2 최적화 후 목표: ~5-10초 (5-10x 개선)
    /// </summary>
    [Benchmark]
    [IterationSetup(Target = nameof(IndexLargeBatch_10000Chunks))]
    public void SetupLargeBatch()
    {
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding()
            .Build();
    }

    [Benchmark]
    public async Task IndexLargeBatch_10000Chunks()
    {
        await _context.Indexer.IndexBatchAsync(_largeBatchDocuments, parallelism: 16);
    }

    /// <summary>
    /// 병렬 처리 수준별 성능 비교 (1,000 chunks)
    /// </summary>
    [Benchmark]
    [Arguments(1)]
    [Arguments(4)]
    [Arguments(8)]
    [Arguments(16)]
    [IterationSetup(Target = nameof(IndexWithVaryingParallelism))]
    public void SetupParallelismTest()
    {
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseInMemoryEmbedding()
            .Build();
    }

    [Benchmark]
    [Arguments(1)]
    [Arguments(4)]
    [Arguments(8)]
    [Arguments(16)]
    public async Task IndexWithVaryingParallelism(int parallelism)
    {
        await _context.Indexer.IndexBatchAsync(_mediumBatchDocuments, parallelism: parallelism);
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
