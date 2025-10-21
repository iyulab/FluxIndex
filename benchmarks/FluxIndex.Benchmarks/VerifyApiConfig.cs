using System;
using System.IO;
using System.Threading.Tasks;
using DotNetEnv;
using FluxIndex.SDK;
using FluxIndex.Domain.Entities;

namespace FluxIndex.Benchmarks;

/// <summary>
/// .env.local 구성 및 실제 OpenAI API 연결 확인 프로그램
/// 벤치마크 실행 전에 API 키와 설정이 올바른지 검증
/// </summary>
public class VerifyApiConfig
{
    public static async Task RunVerification()
    {
        Console.WriteLine("=== FluxIndex Real API Configuration Verification ===\n");

        string? tempDbPath = null;
        try
        {
            // 1. .env.local 파일 찾기 및 로드
            var projectRoot = FindProjectRoot();
            var envPath = Path.Combine(projectRoot, ".env.local");

            Console.WriteLine($"[1/5] Loading .env.local from: {projectRoot}");

            if (!File.Exists(envPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ ERROR: .env.local 파일을 찾을 수 없습니다.");
                Console.WriteLine($"   Expected path: {envPath}");
                Console.WriteLine($"   프로젝트 루트에 .env.local 파일을 생성하세요.");
                Console.ResetColor();
                return;
            }

            Env.Load(envPath);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ .env.local 로드 완료");
            Console.ResetColor();

            // 2. 환경 변수 확인
            Console.WriteLine("\n[2/5] Checking environment variables");

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4";
            var embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";

            if (string.IsNullOrEmpty(apiKey))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ ERROR: OPENAI_API_KEY가 설정되지 않았습니다.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ API Key: {apiKey.Substring(0, 20)}... ({apiKey.Length} chars)");
            Console.WriteLine($"✓ LLM Model: {model}");
            Console.WriteLine($"✓ Embedding Model: {embeddingModel}");
            Console.ResetColor();

            // 3. FluxIndex 컨텍스트 생성
            Console.WriteLine("\n[3/5] Creating FluxIndex context with real OpenAI API");

            // Use temp file-based SQLite instead of in-memory for reliability
            tempDbPath = Path.Combine(Path.GetTempPath(), $"fluxindex-verify-{Guid.NewGuid()}.db");
            Console.WriteLine($"   Using temporary database: {tempDbPath}");

            var context = FluxIndexContext.CreateBuilder()
                .UseSQLite(tempDbPath)
                .UseOpenAI(apiKey, embeddingModel)
                .Build();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ FluxIndex context created successfully");
            Console.ResetColor();

            // 4. 테스트 문서 인덱싱 (실제 API 호출)
            Console.WriteLine("\n[4/5] Testing indexing with real API (1 document)");
            Console.WriteLine("⏳ This will make a real API call and may take a few seconds...");

            var testDocument = new Document
            {
                Id = "test-doc-1",
                CreatedAt = DateTime.UtcNow
            };

            var testChunk = new FluxIndex.Domain.Entities.DocumentChunk
            {
                Id = "chunk-1",
                DocumentId = "test-doc-1",
                Content = "FluxIndex is a RAG infrastructure library for .NET 9.0 that provides vector search and hybrid search capabilities.",
                ChunkIndex = 0,
                TotalChunks = 1,
                TokenCount = 20,
                Metadata = new Dictionary<string, object>
                {
                    { "test", true },
                    { "verification", "api-config" }
                }
            };

            testDocument.AddChunk(testChunk);

            var startTime = DateTime.UtcNow;
            var documentId = await context.Indexer.IndexDocumentAsync(testDocument);
            var elapsed = DateTime.UtcNow - startTime;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Indexing completed in {elapsed.TotalMilliseconds:F0}ms");
            Console.WriteLine($"  Document ID: {documentId}");
            Console.ResetColor();

            // 5. 테스트 검색 (실제 API 호출)
            Console.WriteLine("\n[5/5] Testing search with real API");
            Console.WriteLine("⏳ This will make a real API call and may take a few seconds...");

            startTime = DateTime.UtcNow;
            var results = await context.Retriever.SearchAsync("RAG infrastructure for .NET", maxResults: 5);
            elapsed = DateTime.UtcNow - startTime;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Search completed in {elapsed.TotalMilliseconds:F0}ms");
            Console.WriteLine($"  Results found: {results.Count()}");
            Console.ResetColor();

            if (results.Any())
            {
                var topResult = results.First();
                Console.WriteLine($"\n  Top result:");
                Console.WriteLine($"  - Score: {topResult.Score:F4}");
                Console.WriteLine($"  - Content: {topResult.DocumentChunk.Content.Substring(0, Math.Min(100, topResult.DocumentChunk.Content.Length))}...");
            }

            // 성공 요약
            Console.WriteLine("\n╔═══════════════════════════════════════════════════╗");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("║  ✓ ALL CHECKS PASSED - API Configuration Valid  ║");
            Console.ResetColor();
            Console.WriteLine("╚═══════════════════════════════════════════════════╝");
            Console.WriteLine("\nYou can now run benchmarks with:");
            Console.WriteLine("  dotnet run --project benchmarks/FluxIndex.Benchmarks -c Release -- --filter *RealApi*");
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
            Console.WriteLine($"\n   Stack trace:");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
        finally
        {
            // Cleanup temporary database
            if (tempDbPath != null && File.Exists(tempDbPath))
            {
                try
                {
                    File.Delete(tempDbPath);
                    Console.WriteLine($"\n✓ Cleaned up temporary database: {tempDbPath}");
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
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

        throw new DirectoryNotFoundException(
            "프로젝트 루트를 찾을 수 없습니다. .env.local 파일이 프로젝트 루트에 있는지 확인하세요.");
    }
}
