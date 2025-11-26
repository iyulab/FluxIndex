using FluxIndex.SDK;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Diagnostics;

namespace RealAPITest;

/// <summary>
/// 실제 OpenAI API를 사용한 FluxIndex 기능, 품질, 성능 테스트
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 FluxIndex Real API Quality & Performance Test");
        Console.WriteLine("=================================================");

        try
        {
            // Load .env.local file if it exists
            var envPath = Path.Combine(Directory.GetCurrentDirectory(), "../../.env.local");
            if (File.Exists(envPath))
            {
                var envLines = File.ReadAllLines(envPath);
                foreach (var line in envLines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                    }
                }
            }

            // .env.local에서 API 키 읽기
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";

            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("❌ OPENAI_API_KEY not found in environment variables");
                return;
            }

            Console.WriteLine($"🔑 Using OpenAI API with embedding model: {embeddingModel}");

            // FluxIndex 컨텍스트 설정 - 실제 OpenAI API + 시맨틱 캐싱 사용
            var context = FluxIndexContext.CreateBuilder()
                .UseOpenAI(apiKey, embeddingModel)
                .UseSQLiteInMemory()
                .UseMemoryCache(maxCacheSize: 2000) // Enable semantic caching
                .WithLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information))
                .Build();

            Console.WriteLine("✅ FluxIndex context initialized with real OpenAI API");

            // 성능 측정을 위한 테스트 데이터 준비
            await RunComprehensiveTests(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Error: {ex.InnerException.Message}");
            }
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static async Task RunComprehensiveTests(IFluxIndexContext context)
    {
        var stopwatch = new Stopwatch();

        // 1. 문서 인덱싱 테스트
        Console.WriteLine("\n📄 Phase 1: Document Indexing Test");
        Console.WriteLine("====================================");

        var testDocuments = CreateTestDocuments();

        stopwatch.Start();
        foreach (var doc in testDocuments)
        {
            await context.Indexer.IndexDocumentAsync(doc);
            Console.WriteLine($"   ✅ Indexed: {doc.Id} ({doc.Chunks.Count} chunks)");
        }
        stopwatch.Stop();

        Console.WriteLine($"📊 Indexing completed in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"   - Total documents: {testDocuments.Length}");
        Console.WriteLine($"   - Total chunks: {testDocuments.Sum(d => d.Chunks.Count)}");
        Console.WriteLine($"   - Average per document: {stopwatch.ElapsedMilliseconds / testDocuments.Length}ms");

        // 2. 검색 성능 테스트
        Console.WriteLine("\n🔍 Phase 2: Search Performance Test");
        Console.WriteLine("====================================");

        var testQueries = new[]
        {
            "machine learning algorithms",
            "vector database technology",
            "RAG system architecture",
            "embedding models comparison",
            "search optimization techniques"
        };

        var searchResults = new List<(string Query, TimeSpan Duration, int Results)>();

        foreach (var query in testQueries)
        {
            stopwatch.Restart();
            var results = await context.SearchAsync(query, maxResults: 10);
            stopwatch.Stop();

            var resultList = results.ToList();
            searchResults.Add((query, stopwatch.Elapsed, resultList.Count));

            Console.WriteLine($"   🔍 Query: \"{query}\"");
            Console.WriteLine($"      ⏱️  Duration: {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"      📊 Results: {resultList.Count}");

            // 상위 3개 결과 표시
            foreach (var result in resultList.Take(3))
            {
                Console.WriteLine($"      📄 Score: {result.Score:F3} | {result.Content.Substring(0, Math.Min(60, result.Content.Length))}...");
            }
            Console.WriteLine();
        }

        // 3. 성능 통계 분석
        Console.WriteLine("\n📊 Phase 3: Performance Analysis");
        Console.WriteLine("==================================");

        var avgSearchTime = searchResults.Average(r => r.Duration.TotalMilliseconds);
        var maxSearchTime = searchResults.Max(r => r.Duration.TotalMilliseconds);
        var minSearchTime = searchResults.Min(r => r.Duration.TotalMilliseconds);
        var avgResults = searchResults.Average(r => r.Results);

        Console.WriteLine($"🎯 Search Performance Metrics:");
        Console.WriteLine($"   - Average response time: {avgSearchTime:F1}ms");
        Console.WriteLine($"   - Min response time: {minSearchTime:F1}ms");
        Console.WriteLine($"   - Max response time: {maxSearchTime:F1}ms");
        Console.WriteLine($"   - Average results count: {avgResults:F1}");
        Console.WriteLine($"   - Total API calls: {testQueries.Length}");

        // 4. RAG 품질 평가 (평가 시스템이 활성화된 경우)
        await RunQualityEvaluation(context);

        // 5. 메모리 및 리소스 사용량 확인
        Console.WriteLine("\n💾 Phase 4: Resource Usage Analysis");
        Console.WriteLine("====================================");

        var beforeGC = GC.GetTotalMemory(false);
        GC.Collect();
        var afterGC = GC.GetTotalMemory(true);

        Console.WriteLine($"🧠 Memory Usage:");
        Console.WriteLine($"   - Before GC: {beforeGC / 1024 / 1024:F1} MB");
        Console.WriteLine($"   - After GC: {afterGC / 1024 / 1024:F1} MB");
        Console.WriteLine($"   - Freed: {(beforeGC - afterGC) / 1024 / 1024:F1} MB");

        // 성능 개선 권장사항
        Console.WriteLine("\n💡 Performance Recommendations");
        Console.WriteLine("================================");

        if (avgSearchTime > 1000)
        {
            Console.WriteLine("⚠️  High response time detected:");
            Console.WriteLine("   - Consider enabling semantic caching");
            Console.WriteLine("   - Optimize embedding model selection");
            Console.WriteLine("   - Review chunk size settings");
        }

        if (avgResults < 5)
        {
            Console.WriteLine("⚠️  Low result count detected:");
            Console.WriteLine("   - Review similarity thresholds");
            Console.WriteLine("   - Consider expanding chunk overlap");
            Console.WriteLine("   - Evaluate indexing strategy");
        }

        Console.WriteLine("\n🎉 Comprehensive testing completed!");
    }

    static async Task RunQualityEvaluation(IFluxIndexContext context)
    {
        Console.WriteLine("\n🎯 Phase 3.5: RAG Quality Evaluation");
        Console.WriteLine("=====================================");

        try
        {
            // 간단한 품질 테스트
            var testQuery = "What are machine learning algorithms?";
            var results = await context.SearchAsync(testQuery, maxResults: 5);
            var resultList = results.ToList();

            if (resultList.Any())
            {
                Console.WriteLine($"🔍 Quality test query: \"{testQuery}\"");
                Console.WriteLine($"📊 Retrieved {resultList.Count} results");

                // 다양성 점수 계산 (간단한 버전)
                var uniqueDocuments = resultList.Select(r => r.DocumentId).Distinct().Count();
                var diversityScore = (double)uniqueDocuments / resultList.Count;

                Console.WriteLine($"   📈 Diversity Score: {diversityScore:F3} ({uniqueDocuments}/{resultList.Count} unique docs)");

                // 점수 분포 분석
                var avgScore = resultList.Average(r => r.Score);
                var maxScore = resultList.Max(r => r.Score);
                var minScore = resultList.Min(r => r.Score);

                Console.WriteLine($"   🎯 Score Distribution:");
                Console.WriteLine($"      - Average: {avgScore:F3}");
                Console.WriteLine($"      - Max: {maxScore:F3}");
                Console.WriteLine($"      - Min: {minScore:F3}");
                Console.WriteLine($"      - Range: {maxScore - minScore:F3}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Quality evaluation error: {ex.Message}");
        }
    }

    static Document[] CreateTestDocuments()
    {
        return new[]
        {
            CreateDocument("doc1", "Machine Learning Fundamentals",
                "Machine learning algorithms are computational methods that enable systems to learn patterns from data without explicit programming. " +
                "Supervised learning uses labeled data to train models for prediction tasks. Unsupervised learning discovers hidden patterns in unlabeled data. " +
                "Common algorithms include linear regression, decision trees, neural networks, and support vector machines. " +
                "Deep learning, a subset of machine learning, uses artificial neural networks with multiple layers to model complex patterns.",
                new Dictionary<string, object> { {"category", "education"}, {"topic", "ML"}, {"difficulty", "beginner"} }),

            CreateDocument("doc2", "Vector Databases and Embeddings",
                "Vector databases are specialized storage systems designed for high-dimensional embedding vectors. " +
                "Text embeddings convert words and sentences into numerical vectors that capture semantic meaning. " +
                "Popular embedding models include Word2Vec, GloVe, BERT, and OpenAI's text-embedding models. " +
                "Vector similarity search uses metrics like cosine similarity and Euclidean distance. " +
                "Modern vector databases support approximate nearest neighbor (ANN) algorithms like HNSW and IVF for efficient search.",
                new Dictionary<string, object> { {"category", "technology"}, {"topic", "vector-db"}, {"difficulty", "intermediate"} }),

            CreateDocument("doc3", "RAG System Architecture",
                "Retrieval-Augmented Generation (RAG) combines information retrieval with text generation. " +
                "The architecture consists of three main components: document indexing, retrieval, and generation. " +
                "Document chunking strategies affect retrieval quality and context window utilization. " +
                "Hybrid search combines dense vector search with sparse keyword matching for better results. " +
                "Advanced techniques include query expansion, re-ranking, and context optimization.",
                new Dictionary<string, object> { {"category", "architecture"}, {"topic", "RAG"}, {"difficulty", "advanced"} }),

            CreateDocument("doc4", "Search Optimization Techniques",
                "Search optimization involves multiple strategies to improve retrieval quality and performance. " +
                "Query preprocessing includes stemming, lemmatization, and stop word removal. " +
                "Semantic search uses embedding models to understand query intent and meaning. " +
                "Re-ranking algorithms refine initial search results using cross-encoders or learning-to-rank models. " +
                "Caching strategies reduce latency by storing frequently accessed results and computations.",
                new Dictionary<string, object> { {"category", "optimization"}, {"topic", "search"}, {"difficulty", "intermediate"} }),

            CreateDocument("doc5", "FluxIndex Library Features",
                "FluxIndex is a comprehensive RAG infrastructure library for .NET applications. " +
                "It provides modular architecture with pluggable AI providers and storage backends. " +
                "Key features include hybrid search, small-to-big retrieval, and comprehensive evaluation metrics. " +
                "The library supports PostgreSQL with pgvector, SQLite, and Redis for different deployment scenarios. " +
                "Built-in evaluation system measures nine quality metrics including precision, recall, MRR, and faithfulness.",
                new Dictionary<string, object> { {"category", "software"}, {"topic", "FluxIndex"}, {"difficulty", "intermediate"} })
        };
    }

    static Document CreateDocument(string id, string title, string content, Dictionary<string, object> metadata)
    {
        var document = Document.Create(id);

        // 문서를 의미있는 크기의 청크로 분할
        var sentences = content.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
        var chunkSize = 150; // 적당한 청크 크기
        var currentChunk = "";
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > chunkSize && !string.IsNullOrEmpty(currentChunk))
            {
                // 현재 청크 추가
                var chunk = new DocumentChunk(currentChunk.Trim() + ".", chunkIndex)
                {
                    DocumentId = id,
                    TokenCount = currentChunk.Length / 4, // Rough estimate
                    Metadata = metadata
                };
                document.AddChunk(chunk);

                chunkIndex++;
                currentChunk = sentence;
            }
            else
            {
                currentChunk += (string.IsNullOrEmpty(currentChunk) ? "" : ". ") + sentence;
            }
        }

        // 마지막 청크 추가
        if (!string.IsNullOrEmpty(currentChunk))
        {
            var chunk = new DocumentChunk(currentChunk.Trim() + ".", chunkIndex)
            {
                DocumentId = id,
                TokenCount = currentChunk.Length / 4, // Rough estimate
                Metadata = metadata
            };
            document.AddChunk(chunk);
        }

        return document;
    }
}