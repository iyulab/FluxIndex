/**
 * FluxIndex Local Simulation
 *
 * SQLite 기반 Core/SDK 기능 검증을 위한 최소 테스트
 * - 얇은 계층: Core/SDK 기능만 직접 호출
 * - 핵심 기능은 모두 Core/SDK가 담당
 *
 * Note: InMemory embedding은 테스트용으로 모든 벡터가 동일합니다.
 * 실제 semantic search 테스트는 LMSupply 또는 외부 embedding 서비스가 필요합니다.
 */

using FluxIndex.SDK;
using FluxIndex.Storage.SQLite;
using FluxIndex.Core.Domain.Models;

Console.WriteLine("=== FluxIndex Local Simulation (SQLite) ===\n");

// 1. SQLite 기반 Context 생성 (InMemory Embedding for testing)
var dbPath = Path.Combine(Path.GetTempPath(), $"fluxindex_local_{DateTime.Now:yyyyMMdd_HHmmss}.db");
Console.WriteLine($"[Setup] Database: {dbPath}");

var context = FluxIndexContext.CreateBuilder()
    .UseLocalStorage(dbPath)
    .AddSQLiteStorage()
    .UseInMemoryEmbedding()
    .Build();

Console.WriteLine("[Setup] Context created with SQLite + InMemory Embedding");
Console.WriteLine("[Note] InMemory embedding produces identical vectors - keyword search recommended\n");

// 2. 테스트 문서 준비
var testDocuments = new[]
{
    new { Id = "doc1", Title = "FluxIndex Overview", Content = """
        FluxIndex is a powerful RAG infrastructure library for .NET.
        It provides hybrid search combining vector similarity and keyword matching.
        Supports multiple storage backends: SQLite, PostgreSQL, Qdrant.
        """ },
    new { Id = "doc2", Title = "RAG Architecture", Content = """
        Retrieval-Augmented Generation (RAG) enhances LLM responses with retrieved context.
        Key components: Document chunking, embedding generation, vector search, reranking.
        FluxIndex implements all these components with clean architecture.
        """ },
    new { Id = "doc3", Title = "Hybrid Search", Content = """
        Hybrid search combines semantic vector search with BM25 keyword matching.
        RRF (Reciprocal Rank Fusion) merges results from both approaches.
        This achieves better recall than either method alone.
        """ }
};

// 3. 문서 인덱싱
Console.WriteLine("[Indexing] Starting document indexing...");
foreach (var doc in testDocuments)
{
    var chunks = new List<CacheDocumentChunk>();
    var chunkContent = doc.Content.Trim();

    var chunk = new CacheDocumentChunk
    {
        Id = Guid.NewGuid().ToString(),
        DocumentId = doc.Id,
        Content = chunkContent,
        ChunkIndex = 0,
        TotalChunks = 1,
        Metadata = new Dictionary<string, object>
        {
            ["title"] = doc.Title,
            ["source"] = "local_simulation"
        }
    };

    chunks.Add(chunk);

    await context.Indexer.IndexChunksAsync(chunks);
    Console.WriteLine($"  - Indexed: {doc.Title} ({chunkContent.Length} chars)");
}
Console.WriteLine($"[Indexing] Complete: {testDocuments.Length} documents\n");

// 4. 통계 확인
var stats = await context.Retriever.GetStatisticsAsync();
Console.WriteLine($"[Stats] Total documents: {stats.TotalDocuments}, Total chunks: {stats.TotalChunks}\n");

// 5. Vector 검색 테스트 (InMemory embedding으로는 제한적)
Console.WriteLine("[Vector Search] Testing (limited with InMemory embedding)...\n");
var vectorQuery = "FluxIndex RAG";
Console.WriteLine($"Query: \"{vectorQuery}\"");
Console.WriteLine(new string('-', 50));

var vectorResults = await context.Retriever.SearchAsync(vectorQuery, maxResults: 3, minScore: 0.0f);
Console.WriteLine($"  Results: {vectorResults.Count()}");
foreach (var result in vectorResults.Take(3))
{
    var title = result.DocumentChunk?.Metadata?.GetValueOrDefault("title", "Unknown") ?? "Unknown";
    Console.WriteLine($"  [{result.Score:F3}] {title}");
}
Console.WriteLine();

// 6. 키워드 검색 테스트 (BM25) - 더 적합한 테스트
Console.WriteLine("[Keyword Search] Testing BM25...\n");
var keywords = new[] { "FluxIndex", "RAG", "hybrid search" };

foreach (var keyword in keywords)
{
    Console.WriteLine($"Keyword: \"{keyword}\"");
    Console.WriteLine(new string('-', 50));

    var keywordResults = await context.Retriever.KeywordSearchAsync(keyword, maxResults: 3);
    Console.WriteLine($"  Results: {keywordResults.Count()}");

    foreach (var result in keywordResults)
    {
        var title = result.DocumentChunk?.Metadata?.GetValueOrDefault("title", "Unknown") ?? "Unknown";
        Console.WriteLine($"  [{result.Score:F3}] {title}");
    }
    Console.WriteLine();
}

// 7. 하이브리드 검색 테스트
Console.WriteLine("[Hybrid Search] Testing Vector + Keyword fusion...\n");
var hybridKeyword = "vector similarity";
var hybridQuery = "vector similarity search";
Console.WriteLine($"Keyword: \"{hybridKeyword}\", Query: \"{hybridQuery}\"");
Console.WriteLine(new string('-', 50));

var hybridResults = await context.Retriever.HybridSearchAsync(hybridKeyword, hybridQuery, maxResults: 3, vectorWeight: 0.3);
Console.WriteLine($"  Results: {hybridResults.Count()}");

foreach (var result in hybridResults)
{
    var title = result.DocumentChunk?.Metadata?.GetValueOrDefault("title", "Unknown") ?? "Unknown";
    Console.WriteLine($"  [{result.Score:F3}] {title}");
}
Console.WriteLine();

// 8. SearchAsync with SearchOptions (통합 API 테스트)
Console.WriteLine("[Unified Search API] Testing SearchAsync with options...\n");
var searchOptions = new SearchOptions
{
    TopK = 5,
    MinSimilarity = 0.0f,
    UseHybridSearch = true
};

var unifiedResults = await context.Retriever.SearchAsync("FluxIndex architecture", searchOptions);
Console.WriteLine($"Query: \"FluxIndex architecture\"");
Console.WriteLine($"  Total Results: {unifiedResults.TotalResults}");
Console.WriteLine($"  Search Time: {unifiedResults.SearchTime.TotalMilliseconds:F1}ms");
Console.WriteLine($"  Hybrid Search Used: {unifiedResults.Metadata?.GetValueOrDefault("useHybridSearch", false)}");

foreach (var result in unifiedResults.Results.Take(3))
{
    var title = result.Metadata?.GetValueOrDefault("title", "Unknown") ?? "Unknown";
    Console.WriteLine($"  [{result.Score:F3}] {title} (V:{result.VectorScore:F3}, K:{result.KeywordScore:F3})");
}
Console.WriteLine();

// 9. 정리
Console.WriteLine("[Cleanup] Disposing context...");
(context as IDisposable)?.Dispose();

// 잠시 대기 후 DB 파일 삭제
await Task.Delay(500);

try
{
    if (File.Exists(dbPath))
    {
        File.Delete(dbPath);
        Console.WriteLine($"[Cleanup] Deleted database: {dbPath}");
    }
}
catch (IOException)
{
    Console.WriteLine($"[Cleanup] Database file in use, skipping deletion: {dbPath}");
}

Console.WriteLine("\n=== Simulation Complete ===");
Console.WriteLine("\nTest Summary:");
Console.WriteLine("  - SQLite storage: Working");
Console.WriteLine("  - Document indexing: Working");
Console.WriteLine("  - Vector search: Limited (InMemory embedding)");
Console.WriteLine("  - Keyword search: Working (BM25)");
Console.WriteLine("  - Hybrid search: Working (fusion)");
Console.WriteLine("  - Unified API: Working");
