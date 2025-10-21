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
/// 빠른 baseline 성능 측정 프로그램
/// BenchmarkDotNet보다 빠르게 초기 성능 데이터 수집
/// </summary>
public class QuickBaseline
{
    public static async Task RunQuickBaseline()
    {
        Console.WriteLine("=== FluxIndex Quick Baseline Performance Test ===");
        Console.WriteLine("목표: Phase 7.3 최적화 전 현재 성능 측정\n");

        string? tempDbPath = null;
        try
        {
            // 1. .env.local 로드
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

            Console.WriteLine($"✓ API Configuration Loaded");
            Console.WriteLine($"  Embedding Model: {embeddingModel}\n");

            // 2. FluxIndex 컨텍스트 생성
            tempDbPath = Path.Combine(Path.GetTempPath(), $"fluxindex-baseline-{Guid.NewGuid()}.db");
            Console.WriteLine($"[Setup] Creating FluxIndex context with ChunkCount=100");

            var context = FluxIndexContext.CreateBuilder()
                .UseSQLite(tempDbPath)
                .UseOpenAI(apiKey, embeddingModel)
                .Build();

            // 3. 테스트 데이터 인덱싱
            Console.WriteLine($"[Setup] Indexing 100 chunks (10 documents)...");
            var setupStopwatch = Stopwatch.StartNew();

            var chunks = SampleDocuments.GenerateChunks(100, seed: 12345);
            var documents = ConvertChunksToDocuments(chunks);

            await context.Indexer.IndexBatchAsync(documents, parallelism: 4);
            setupStopwatch.Stop();

            Console.WriteLine($"✓ Indexing completed in {setupStopwatch.ElapsedMilliseconds}ms\n");

            // 4. 쿼리 준비
            var complexQueries = SampleQueries.GetComplexSemanticQueries();

            // 5. Warm-up (첫 번째 실행은 캐시 초기화 등으로 느릴 수 있음)
            Console.WriteLine("[Warmup] Running 1 warmup query...");
            await context.Retriever.SearchAsync(complexQueries[0], maxResults: 10);
            Console.WriteLine("✓ Warmup completed\n");

            // 6. Baseline 측정 (5회 반복)
            Console.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.WriteLine("║           BASELINE PERFORMANCE MEASUREMENT        ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════╝\n");

            var iterations = 5;
            var results = new List<(string query, long latency, int resultsCount)>();

            for (int i = 0; i < iterations; i++)
            {
                var query = complexQueries[i % complexQueries.Count];

                var sw = Stopwatch.StartNew();
                var searchResults = await context.Retriever.SearchAsync(query, maxResults: 10);
                sw.Stop();

                var resultCount = searchResults.Count();
                results.Add((query, sw.ElapsedMilliseconds, resultCount));

                Console.WriteLine($"[Iteration {i + 1}/{iterations}]");
                Console.WriteLine($"  Query: {query.Substring(0, Math.Min(60, query.Length))}...");
                Console.WriteLine($"  Latency: {sw.ElapsedMilliseconds}ms");
                Console.WriteLine($"  Results: {resultCount} chunks");
                Console.WriteLine($"  Avg Score: {(searchResults.Any() ? searchResults.Average(r => r.Score) : 0):F4}\n");
            }

            // 7. 통계 분석
            Console.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.WriteLine("║              BASELINE STATISTICS                  ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════╝\n");

            var latencies = results.Select(r => r.latency).ToList();
            var avgLatency = latencies.Average();
            var minLatency = latencies.Min();
            var maxLatency = latencies.Max();
            var p50 = Percentile(latencies, 50);
            var p95 = Percentile(latencies, 95);
            var avgResults = results.Average(r => r.resultsCount);

            Console.WriteLine($"Response Time Statistics:");
            Console.WriteLine($"  Average:    {avgLatency:F2}ms");
            Console.WriteLine($"  Min:        {minLatency}ms");
            Console.WriteLine($"  Max:        {maxLatency}ms");
            Console.WriteLine($"  P50:        {p50:F2}ms");
            Console.WriteLine($"  P95:        {p95:F2}ms");
            Console.WriteLine();
            Console.WriteLine($"Search Quality:");
            Console.WriteLine($"  Avg Results: {avgResults:F2} chunks/query");
            Console.WriteLine();

            // 8. Phase 7.3 목표 대비 평가
            Console.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.WriteLine("║         PHASE 7.3 TARGET COMPARISON               ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════╝\n");

            var targetLatency = 250.0; // 목표: 250ms
            var targetResults = 6.0;    // 목표: 6-8/10 results
            var currentLatency = avgLatency;
            var currentResults = avgResults;

            Console.WriteLine($"Latency:");
            Console.WriteLine($"  Current: {currentLatency:F2}ms");
            Console.WriteLine($"  Target:  {targetLatency}ms");
            if (currentLatency > targetLatency)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Gap:     -{(currentLatency - targetLatency):F2}ms (needs improvement)");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Status:  ✓ Target met!");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine($"Search Quality:");
            Console.WriteLine($"  Current: {currentResults:F2} results/query");
            Console.WriteLine($"  Target:  {targetResults} results/query");
            if (currentResults < targetResults)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Gap:     -{(targetResults - currentResults):F2} results (needs improvement)");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  Status:  ✓ Target met!");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("Next Steps:");
            Console.WriteLine("1. Implement query optimization (filtering, pre-processing)");
            Console.WriteLine("2. Re-run baseline to measure improvement");
            Console.WriteLine("3. Iterate until targets are met");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ ERROR: {ex.GetType().Name}");
            Console.WriteLine($"   Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
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

    private static double Percentile(List<long> sequence, int percentile)
    {
        var sorted = sequence.OrderBy(x => x).ToList();
        int N = sorted.Count;
        double n = (N - 1) * percentile / 100.0 + 1;

        if (n == 1d) return sorted[0];
        if (n == N) return sorted[N - 1];

        int k = (int)n;
        double d = n - k;
        return sorted[k - 1] + d * (sorted[k] - sorted[k - 1]);
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
