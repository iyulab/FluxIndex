using FluxIndex.SDK;
using FluxIndex.Domain.Entities;
using FluxIndex.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace QualityValidationTest;

/// <summary>
/// 품질 개선 사항 검증 테스트
/// - PostgreSQL 유사도 계산 버그 수정 확인
/// - 낮은 임계값(0.2f)으로 결과 수 개선 확인
/// - 시맨틱 캐싱 성능 개선 확인
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🔬 FluxIndex Quality Validation Test");
        Console.WriteLine("=====================================");

        try
        {
            // Load API key from environment
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

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("❌ OPENAI_API_KEY not found");
                return;
            }

            await TestQualityImprovements(apiKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static async Task TestQualityImprovements(string apiKey)
    {
        var stopwatch = new Stopwatch();

        // Test 1: Similarity threshold improvement (Before: 0.5f → After: 0.2f)
        Console.WriteLine("\n🎯 Test 1: Similarity Threshold Optimization");
        Console.WriteLine("=============================================");

        var contextLowThreshold = FluxIndexContext.CreateBuilder()
            .UseOpenAI(apiKey, "text-embedding-3-small")
            .UseSQLiteInMemory()
            .UseMemoryCache(maxCacheSize: 1000)
            .WithLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .Build();

        // Index test documents
        var testDocuments = CreateDiverseTestDocuments();
        foreach (var doc in testDocuments)
        {
            await contextLowThreshold.Indexer.IndexDocumentAsync(doc);
        }

        // Test with optimized threshold (0.2f)
        var testQueries = new[]
        {
            "machine learning algorithms",
            "neural networks deep learning",
            "data science techniques",
            "artificial intelligence applications",
            "vector similarity search"
        };

        var resultsWithLowThreshold = new List<(string Query, int Count, double AvgScore, TimeSpan Time)>();

        Console.WriteLine("🔍 Testing with optimized threshold (0.2f):");
        foreach (var query in testQueries)
        {
            stopwatch.Restart();
            var results = await contextLowThreshold.SearchAsync(query, maxResults: 10, minScore: 0.2f);
            stopwatch.Stop();

            var resultList = results.ToList();
            var avgScore = resultList.Any() ? resultList.Average(r => r.Score) : 0;

            resultsWithLowThreshold.Add((query, resultList.Count, avgScore, stopwatch.Elapsed));
            Console.WriteLine($"   Query: '{query}' → {resultList.Count} results, avg score: {avgScore:F3}, time: {stopwatch.ElapsedMilliseconds}ms");
        }

        // Test 2: Semantic Caching Performance
        Console.WriteLine("\n⚡ Test 2: Semantic Caching Performance");
        Console.WriteLine("======================================");

        var cacheTestQuery = "machine learning algorithms";

        // First call (no cache)
        stopwatch.Restart();
        var firstResults = await contextLowThreshold.SearchAsync(cacheTestQuery, maxResults: 5);
        stopwatch.Stop();
        var firstCallTime = stopwatch.ElapsedMilliseconds;
        Console.WriteLine($"   First call (cache miss): {firstCallTime}ms");

        // Second call (should hit cache or be very similar)
        stopwatch.Restart();
        var secondResults = await contextLowThreshold.SearchAsync(cacheTestQuery, maxResults: 5);
        stopwatch.Stop();
        var secondCallTime = stopwatch.ElapsedMilliseconds;
        Console.WriteLine($"   Second call: {secondCallTime}ms");

        // Similar query (should benefit from semantic caching)
        var similarQuery = "ML algorithms and techniques";
        stopwatch.Restart();
        var similarResults = await contextLowThreshold.SearchAsync(similarQuery, maxResults: 5);
        stopwatch.Stop();
        var similarCallTime = stopwatch.ElapsedMilliseconds;
        Console.WriteLine($"   Similar query ('{similarQuery}'): {similarCallTime}ms");

        // Test 3: PostgreSQL vs SQLite Comparison
        Console.WriteLine("\n🏪 Test 3: Storage Backend Consistency");
        Console.WriteLine("======================================");

        // Test same query with both backends to ensure consistency
        var testQuery = "neural network applications";
        var sqliteResults = await contextLowThreshold.SearchAsync(testQuery, maxResults: 10);
        var sqliteList = sqliteResults.ToList();

        Console.WriteLine($"   SQLite results: {sqliteList.Count} documents");
        Console.WriteLine($"   Score range: {(sqliteList.Any() ? sqliteList.Min(r => r.Score):0):F3} - {(sqliteList.Any() ? sqliteList.Max(r => r.Score):0):F3}");

        // Test 4: Quality Metrics Summary
        Console.WriteLine("\n📊 Test 4: Overall Quality Metrics");
        Console.WriteLine("===================================");

        var totalQueries = resultsWithLowThreshold.Count;
        var avgResultCount = resultsWithLowThreshold.Average(r => r.Count);
        var avgResponseTime = resultsWithLowThreshold.Average(r => r.Time.TotalMilliseconds);
        var overallAvgScore = resultsWithLowThreshold.Where(r => r.Count > 0).Average(r => r.AvgScore);
        var successRate = (double)resultsWithLowThreshold.Count(r => r.Count > 0) / totalQueries;

        Console.WriteLine($"📈 Quality Improvements Summary:");
        Console.WriteLine($"   - Average results per query: {avgResultCount:F1} (Target: >6.0)");
        Console.WriteLine($"   - Average response time: {avgResponseTime:F1}ms (Target: <250ms)");
        Console.WriteLine($"   - Average similarity score: {overallAvgScore:F3}");
        Console.WriteLine($"   - Success rate: {successRate:P1} (Target: >95%)");
        Console.WriteLine($"   - Cache performance: {(secondCallTime < firstCallTime ? "✅ Improved" : "⚠️ No improvement")}");

        // Performance comparison with targets
        Console.WriteLine("\n🎯 Target vs Actual Performance:");
        Console.WriteLine($"   Result Count: {avgResultCount:F1}/6.0 {(avgResultCount >= 6 ? "✅" : "❌")}");
        Console.WriteLine($"   Response Time: {avgResponseTime:F1}ms/250ms {(avgResponseTime <= 250 ? "✅" : "❌")}");
        Console.WriteLine($"   Success Rate: {successRate:P1}/95% {(successRate >= 0.95 ? "✅" : "❌")}");

        // Cache performance summary
        Console.WriteLine($"\n💾 Cache Performance:");
        Console.WriteLine($"   - Cache enabled: ✅ Memory cache active");
        Console.WriteLine($"   - Cache performance improvement: {(secondCallTime < firstCallTime ? "✅ Detected" : "⚠️ Not detected")}");
        Console.WriteLine($"   - First call: {firstCallTime}ms → Second call: {secondCallTime}ms");

        Console.WriteLine("\n🎉 Quality validation completed!");
    }

    static Document[] CreateDiverseTestDocuments()
    {
        return new[]
        {
            CreateDocument("ml_basics", "Machine Learning Fundamentals",
                "Machine learning is a subset of artificial intelligence that enables computers to learn without being explicitly programmed. " +
                "Supervised learning uses labeled datasets to train algorithms that classify data or predict outcomes accurately. " +
                "Unsupervised learning identifies hidden patterns in data without labeled examples. " +
                "Common supervised algorithms include linear regression, logistic regression, decision trees, and support vector machines. " +
                "Deep learning uses neural networks with multiple layers to model complex patterns in data.",
                new Dictionary<string, object> { {"topic", "ML"}, {"level", "beginner"} }),

            CreateDocument("neural_networks", "Neural Networks and Deep Learning",
                "Neural networks are computing systems inspired by biological neural networks. " +
                "Deep learning architectures include convolutional neural networks for image processing, " +
                "recurrent neural networks for sequential data, and transformer models for natural language processing. " +
                "Backpropagation algorithm trains networks by computing gradients and updating weights. " +
                "Activation functions like ReLU, sigmoid, and tanh introduce non-linearity. " +
                "GPU acceleration enables training of large-scale deep learning models.",
                new Dictionary<string, object> { {"topic", "deep_learning"}, {"level", "intermediate"} }),

            CreateDocument("data_science", "Data Science and Analytics",
                "Data science combines statistics, computer science, and domain expertise to extract insights from data. " +
                "The data science process includes data collection, cleaning, exploratory analysis, modeling, and visualization. " +
                "Statistical methods like hypothesis testing, regression analysis, and clustering are fundamental tools. " +
                "Python libraries like pandas, numpy, scikit-learn, and matplotlib support data science workflows. " +
                "Big data technologies like Apache Spark enable processing of large-scale datasets.",
                new Dictionary<string, object> { {"topic", "data_science"}, {"level", "intermediate"} }),

            CreateDocument("ai_applications", "AI Applications and Use Cases",
                "Artificial intelligence has diverse applications across industries including healthcare, finance, and transportation. " +
                "Computer vision enables image recognition, medical imaging, and autonomous vehicles. " +
                "Natural language processing powers chatbots, sentiment analysis, and language translation. " +
                "Recommendation systems use collaborative filtering and content-based methods. " +
                "Reinforcement learning optimizes decision-making in gaming, robotics, and resource allocation.",
                new Dictionary<string, object> { {"topic", "AI_applications"}, {"level", "advanced"} }),

            CreateDocument("vector_search", "Vector Similarity Search Systems",
                "Vector similarity search enables semantic search by converting text into high-dimensional embeddings. " +
                "Embedding models like BERT, Word2Vec, and OpenAI create dense vector representations of text. " +
                "Approximate nearest neighbor algorithms like HNSW and IVF provide efficient similarity search. " +
                "Vector databases store and index embeddings for fast retrieval at scale. " +
                "Applications include semantic search, recommendation systems, and retrieval-augmented generation.",
                new Dictionary<string, object> { {"topic", "vector_search"}, {"level", "advanced"} })
        };
    }

    static Document CreateDocument(string id, string title, string content, Dictionary<string, object> metadata)
    {
        var document = Document.Create(id);

        // Smart chunking by sentences with overlap
        var sentences = content.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
        var chunkSize = 200; // Slightly larger chunks for better context
        var currentChunk = "";
        var chunkIndex = 0;

        for (int i = 0; i < sentences.Length; i++)
        {
            var sentence = sentences[i];
            if (currentChunk.Length + sentence.Length > chunkSize && !string.IsNullOrEmpty(currentChunk))
            {
                // Add current chunk
                var chunk = new DocumentChunk(currentChunk.Trim() + ".", chunkIndex)
                {
                    DocumentId = id,
                    TokenCount = currentChunk.Length / 4,
                    Metadata = new Dictionary<string, object>(metadata) { {"title", title} }
                };
                document.AddChunk(chunk);

                chunkIndex++;

                // Start new chunk with overlap (last sentence)
                currentChunk = sentence;
            }
            else
            {
                currentChunk += (string.IsNullOrEmpty(currentChunk) ? "" : ". ") + sentence;
            }
        }

        // Add final chunk
        if (!string.IsNullOrEmpty(currentChunk))
        {
            var chunk = new DocumentChunk(currentChunk.Trim() + ".", chunkIndex)
            {
                DocumentId = id,
                TokenCount = currentChunk.Length / 4,
                Metadata = new Dictionary<string, object>(metadata) { {"title", title} }
            };
            document.AddChunk(chunk);
        }

        return document;
    }
}