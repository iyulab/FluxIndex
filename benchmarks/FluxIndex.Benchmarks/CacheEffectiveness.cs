using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotNetEnv;
using FluxIndex.Benchmarks.TestData;
using FluxIndex.Domain.Entities;
using FluxIndex.SDK;

namespace FluxIndex.Benchmarks;

/// <summary>
/// 임베딩 캐시 효과 측정 프로그램
/// Phase 7.3: 동일 쿼리 반복 시 성능 개선 확인
/// </summary>
public class CacheEffectiveness
{
    public static async Task RunCacheTest()
    {
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("    Embedding Cache Effectiveness Test (Phase 7.3)");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        string? tempDbPath = null;
        try
        {
            // 1. Setup
            var projectRoot = FindProjectRoot();
            var envPath = Path.Combine(projectRoot, ".env.local");

            if (!File.Exists(envPath))
            {
                Console.WriteLine("❌ .env.local 파일을 찾을 수 없습니다.");
                return;
            }

            Env.Load(envPath);
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
            var embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";

            Console.WriteLine($"✓ Using embedding model: {embeddingModel}\n");

            // 2. Create context
            tempDbPath = Path.Combine(Path.GetTempPath(), $"fluxindex-cache-test-{Guid.NewGuid()}.db");
            var context = FluxIndexContext.CreateBuilder()
                .UseSQLite(tempDbPath)
                .UseOpenAI(apiKey, embeddingModel)
                .Build();

            // 3. Index test data
            Console.WriteLine("[Setup] Indexing 100 chunks...");
            var chunks = SampleDocuments.GenerateChunks(100, seed: 12345);
            var documents = ConvertChunksToDocuments(chunks);
            await context.Indexer.IndexBatchAsync(documents, parallelism: 4);
            Console.WriteLine("✓ Indexing completed\n");

            // 4. Test 1: First query (cold cache)
            Console.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.WriteLine("║          TEST 1: COLD CACHE (First Query)         ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════╝\n");

            var testQuery = "Explain the architectural differences between transformer models and recurrent neural networks";

            var sw = Stopwatch.StartNew();
            var results1 = await context.Retriever.SearchAsync(testQuery, maxResults: 10);
            sw.Stop();

            var coldCacheTime = sw.ElapsedMilliseconds;
            Console.WriteLine($"Query: {testQuery}");
            Console.WriteLine($"⏱️  Cold Cache Time: {coldCacheTime}ms");
            Console.WriteLine($"📊 Results: {results1.Count()} chunks\n");

            // 5. Test 2: Same query (warm cache - embedding cached)
            Console.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.WriteLine("║     TEST 2: WARM CACHE (Same Query Repeated)      ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════╝\n");

            var warmCacheTimes = new List<long>();
            for (int i = 0; i < 5; i++)
            {
                sw = Stopwatch.StartNew();
                var results2 = await context.Retriever.SearchAsync(testQuery, maxResults: 10);
                sw.Stop();

                warmCacheTimes.Add(sw.ElapsedMilliseconds);
                Console.WriteLine($"[Iteration {i + 1}/5] Warm Cache Time: {sw.ElapsedMilliseconds}ms");
            }

            var avgWarmCacheTime = warmCacheTimes.Average();
            Console.WriteLine($"\n⏱️  Average Warm Cache Time: {avgWarmCacheTime:F2}ms\n");

            // 6. Analysis
            Console.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.WriteLine("║              CACHE EFFECTIVENESS ANALYSIS          ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════╝\n");

            var improvement = coldCacheTime - avgWarmCacheTime;
            var improvementPercent = (improvement / coldCacheTime) * 100;

            Console.WriteLine($"Cold Cache (1st query):     {coldCacheTime}ms");
            Console.WriteLine($"Warm Cache (avg 5 queries): {avgWarmCacheTime:F2}ms");
            Console.WriteLine();

            if (improvement > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Improvement: {improvement:F2}ms ({improvementPercent:F1}%)");
                Console.ResetColor();

                if (improvementPercent >= 50)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Cache is HIGHLY effective (>{improvementPercent:F0}% improvement)");
                }
                else if (improvementPercent >= 20)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"⚠️  Cache is moderately effective ({improvementPercent:F0}% improvement)");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ Cache has minimal effect ({improvementPercent:F0}% improvement)");
                }
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ No improvement detected (cache may not be working)");
                Console.ResetColor();
            }

            Console.WriteLine();

            // 7. Estimate savings
            if (improvement > 0)
            {
                Console.WriteLine("═══════════════════════════════════════════════════");
                Console.WriteLine();
                Console.WriteLine("Projected Savings for Production:");
                Console.WriteLine($"  If 30% of queries are repeated:");
                Console.WriteLine($"    - Cold: 1000 queries × {coldCacheTime}ms = {coldCacheTime * 1000}ms total");
                Console.WriteLine($"    - Warm: 700 cold + 300 warm = {700 * coldCacheTime + 300 * avgWarmCacheTime:F0}ms total");
                Console.WriteLine($"    - Savings: {coldCacheTime * 1000 - (700 * coldCacheTime + 300 * avgWarmCacheTime):F0}ms ({(coldCacheTime * 1000 - (700 * coldCacheTime + 300 * avgWarmCacheTime)) / (coldCacheTime * 1000) * 100:F1}%)");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ ERROR: {ex.GetType().Name}");
            Console.WriteLine($"   Message: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            // Cleanup
            if (tempDbPath != null && File.Exists(tempDbPath))
            {
                try
                {
                    File.Delete(tempDbPath);
                    var walPath = tempDbPath + "-wal";
                    var shmPath = tempDbPath + "-shm";
                    if (File.Exists(walPath)) File.Delete(walPath);
                    if (File.Exists(shmPath)) File.Delete(shmPath);
                }
                catch { }
            }
        }
    }

    private static List<Document> ConvertChunksToDocuments(List<FluxIndex.Domain.Models.DocumentChunk> chunks)
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

    private static string FindProjectRoot()
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

        throw new DirectoryNotFoundException("프로젝트 루트를 찾을 수 없습니다.");
    }
}
