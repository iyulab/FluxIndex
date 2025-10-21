using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using DotNetEnv;
using FluxIndex.Benchmarks.TestData;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK;

namespace FluxIndex.Benchmarks.Benchmarks;

/// <summary>
/// 실제 OpenAI API를 사용한 검색 성능 벤치마크 (Phase 7.3: 품질 개선)
///
/// 목표:
/// - 실제 네트워크 레이턴시 측정
/// - API 호출 오버헤드 분석
/// - 현실적인 성능 프로파일 수립
/// - 병목 지점 식별 (API vs 로컬 처리)
///
/// 주의: 실제 API 비용이 발생하므로 작은 데이터셋 사용
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class RealApiSearchBenchmark
{
    private IFluxIndexContext _context = null!;
    private List<string> _simpleQueries = null!;
    private List<string> _complexQueries = null!;
    private List<string> _hybridQueries = null!;

    private string _apiKey = null!;
    private string _embeddingModel = null!;

    /// <summary>
    /// 소규모 데이터셋으로 테스트 (API 비용 고려)
    /// </summary>
    [Params(100, 500)]
    public int ChunkCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        // .env.local 파일 로드
        var projectRoot = FindProjectRoot();
        var envPath = Path.Combine(projectRoot, ".env.local");

        if (!File.Exists(envPath))
        {
            throw new FileNotFoundException(
                $".env.local 파일을 찾을 수 없습니다: {envPath}\n" +
                "실제 API 키를 사용하려면 .env.local 파일을 생성하세요.");
        }

        Env.Load(envPath);

        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY가 .env.local에 설정되지 않았습니다.");

        _embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL")
            ?? "text-embedding-3-small";

        Console.WriteLine($"✓ .env.local 로드 완료");
        Console.WriteLine($"✓ Embedding Model: {_embeddingModel}");
        Console.WriteLine($"✓ API Key: {_apiKey.Substring(0, 20)}...");

        // 실제 OpenAI API 사용하여 FluxIndex 구성
        _context = FluxIndexContext.CreateBuilder()
            .UseSQLiteInMemory()
            .UseOpenAI(_apiKey, _embeddingModel) // 실제 API 사용
            .Build();

        // 테스트 데이터 인덱싱
        Console.WriteLine($"[Real API] Indexing {ChunkCount} chunks...");
        var startTime = DateTime.UtcNow;

        var chunks = SampleDocuments.GenerateChunks(ChunkCount, seed: 12345);
        var documents = ConvertChunksToDocuments(chunks);

        // 실제 API 호출로 임베딩 생성 (시간 소요 예상)
        await _context.Indexer.IndexBatchAsync(documents, parallelism: 4); // 동시성 제한 (API rate limit 고려)

        var elapsed = DateTime.UtcNow - startTime;
        Console.WriteLine($"[Real API] Indexing completed in {elapsed.TotalSeconds:F2}s ({ChunkCount} chunks)");

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
    /// 단순 키워드 검색 - 실제 API 레이턴시 측정
    /// 예상: 네트워크 오버헤드로 InMemory 대비 50-100ms 증가
    /// </summary>
    [Benchmark]
    public async Task SimpleKeywordSearch_RealApi()
    {
        var query = _simpleQueries[0]; // "machine learning basics"
        var results = await _context.Retriever.SearchAsync(query, maxResults: 10);
    }

    /// <summary>
    /// 복잡한 의미론적 검색 - 실제 API 성능 측정
    /// Phase 7.3 목표: 현재 510ms → 250ms 달성 가능 여부 확인
    /// </summary>
    [Benchmark]
    public async Task ComplexSemanticSearch_RealApi()
    {
        var query = _complexQueries[0]; // 장문 의미론적 쿼리
        var results = await _context.Retriever.SearchAsync(query, maxResults: 10);
    }

    /// <summary>
    /// 혼합 검색 - 실제 API 환경에서의 하이브리드 성능
    /// </summary>
    [Benchmark]
    public async Task HybridSearch_RealApi()
    {
        var query = _hybridQueries[0];
        var results = await _context.Retriever.SearchAsync(query, maxResults: 10);
    }

    /// <summary>
    /// TopK 변화에 따른 실제 API 성능
    /// API 호출 횟수는 동일하나, 벡터 검색 비용 증가 측정
    /// </summary>
    [Benchmark]
    [Arguments(5)]
    [Arguments(10)]
    [Arguments(20)]
    [Arguments(50)]
    public async Task SearchWithVariousTopK_RealApi(int maxResults)
    {
        var query = _complexQueries[1];
        var results = await _context.Retriever.SearchAsync(query, maxResults: maxResults);
    }

    /// <summary>
    /// 배치 검색 - 실제 API 동시 호출 성능
    /// API rate limit 및 네트워크 병목 측정
    /// </summary>
    [Benchmark]
    public async Task BatchSearch_RealApi()
    {
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++) // API 비용 고려하여 10개 → 5개로 축소
        {
            var query = _simpleQueries[i % _simpleQueries.Count];
            tasks.Add(_context.Retriever.SearchAsync(query, maxResults: 10));
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 순차 검색 - 실제 API 순차 호출
    /// 배치와 비교하여 동시성 이득 측정
    /// </summary>
    [Benchmark]
    public async Task SequentialSearch_RealApi()
    {
        for (int i = 0; i < 5; i++) // API 비용 고려
        {
            var query = _simpleQueries[i % _simpleQueries.Count];
            await _context.Retriever.SearchAsync(query, maxResults: 10);
        }
    }

    /// <summary>
    /// 캐시 효과 측정 - 동일 쿼리 반복
    /// 첫 호출 vs 캐시된 호출 성능 차이
    /// </summary>
    [Benchmark]
    public async Task CachedSearch_RealApi()
    {
        var query = _simpleQueries[0];

        // 첫 번째 호출 (캐시 미스)
        await _context.Retriever.SearchAsync(query, maxResults: 10);

        // 두 번째 호출 (캐시 히트 예상)
        await _context.Retriever.SearchAsync(query, maxResults: 10);
    }

    /// <summary>
    /// 프로젝트 루트 디렉토리 찾기
    /// </summary>
    private string FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, ".env.local")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "프로젝트 루트를 찾을 수 없습니다. .env.local 파일이 프로젝트 루트에 있는지 확인하세요.");
    }

    /// <summary>
    /// DocumentChunk 리스트를 Document 리스트로 변환
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
                    Embedding = chunk.Embedding, // 초기값, 실제로는 API에서 생성됨
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
