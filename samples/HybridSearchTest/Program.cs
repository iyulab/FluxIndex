using FluxIndex.SDK;
using FluxIndex.Domain.Entities;
using FluxIndex.Samples.Shared;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using DomainHybridSearchOptions = FluxIndex.Domain.Models.HybridSearchOptions;

namespace HybridSearchTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🔍 FluxIndex Hybrid Search Test (Phase 8.2)");
        Console.WriteLine("=============================================\n");

        // 환경 변수에서 OpenAI 설정 로드
        var (apiKey, embeddingModel, completionModel) = ConfigurationHelper.LoadOpenAIConfiguration();

        if (!ConfigurationHelper.ValidateApiKey(apiKey))
        {
            return;
        }

        ConfigurationHelper.DisplayConfiguration(apiKey!, embeddingModel, completionModel);

        try
        {
            // 1. FluxIndex 컨텍스트 생성 (하이브리드 검색 활성화)
            Console.WriteLine("🔧 Phase 1: Hybrid Search System Setup");
            Console.WriteLine("======================================");

            var context = FluxIndexContext.CreateBuilder()
                .UseOpenAI(apiKey!, embeddingModel)
                .UseSQLiteInMemory()
                .UseMemoryCache(maxCacheSize: 2000)
                .WithQualityMonitoring(enableRealTimeAlerts: true)
                .Build();

            Console.WriteLine("✅ FluxIndex context with hybrid search initialized");

            // 2. 테스트 데이터 인덱싱
            Console.WriteLine("\n📚 Phase 2: Test Data Indexing");
            Console.WriteLine("===============================");

            await IndexTestDocuments(context);
            Console.WriteLine("✅ 테스트 문서 인덱싱 완료");

            // 3. 벡터 검색 vs 하이브리드 검색 비교
            Console.WriteLine("\n🔍 Phase 3: Vector vs Hybrid Search Comparison");
            Console.WriteLine("===============================================");

            var testQueries = new[]
            {
                "machine learning algorithms",
                "neural networks deep learning",
                "Python programming",
                "data science techniques",
                "artificial intelligence applications",
                "vector similarity search",
                "natural language processing",
                "computer vision models"
            };

            foreach (var query in testQueries)
            {
                await CompareSearchMethods(context, query);
                await Task.Delay(100); // Rate limiting
            }

            // 4. 하이브리드 검색 가중치 테스트
            Console.WriteLine("\n⚖️ Phase 4: Hybrid Search Weight Testing");
            Console.WriteLine("========================================");

            await TestHybridWeights(context, "machine learning Python");

            // 5. 성능 평가
            Console.WriteLine("\n📊 Phase 5: Performance Analysis");
            Console.WriteLine("================================");

            await PerformanceAnalysis(context, testQueries);

            Console.WriteLine("\n🎉 Phase 8.2 하이브리드 검색 테스트 완료!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 오류 발생: {ex.Message}");
            Console.WriteLine($"상세: {ex}");
        }

        Console.WriteLine("\n🎯 Press any key to exit...");
        Console.ReadKey();
    }

    static async Task IndexTestDocuments(IFluxIndexContext context)
    {
        var documents = new[]
        {
            new { Id = "ml_basics", Title = "Machine Learning Basics", Content = "Machine learning is a subset of artificial intelligence that focuses on algorithms and statistical models. Python is widely used for implementing machine learning algorithms due to its extensive libraries like scikit-learn, TensorFlow, and PyTorch. Common algorithms include linear regression, decision trees, and neural networks." },
            new { Id = "deep_learning", Title = "Deep Learning Guide", Content = "Deep learning uses neural networks with multiple layers to model and understand complex patterns. Popular frameworks include TensorFlow, PyTorch, and Keras. Applications include computer vision, natural language processing, and speech recognition. GPU acceleration is often used for training deep neural networks." },
            new { Id = "python_data", Title = "Python for Data Science", Content = "Python has become the go-to language for data science and machine learning. Key libraries include NumPy for numerical computing, pandas for data manipulation, matplotlib for visualization, and scikit-learn for machine learning. Jupyter notebooks provide an interactive environment for data analysis." },
            new { Id = "ai_applications", Title = "AI Applications", Content = "Artificial intelligence has numerous real-world applications including computer vision for image recognition, natural language processing for text analysis, robotics for automation, and recommendation systems for personalized content. These applications leverage various machine learning techniques." },
            new { Id = "vector_search", Title = "Vector Similarity Search", Content = "Vector similarity search is a technique used in modern search engines and recommendation systems. It involves converting text or other data into high-dimensional vectors using embedding models, then finding similar vectors using cosine similarity or other distance metrics. This enables semantic search capabilities." },
            new { Id = "nlp_guide", Title = "Natural Language Processing", Content = "Natural language processing (NLP) is a field of AI that deals with the interaction between computers and human language. Techniques include tokenization, part-of-speech tagging, named entity recognition, and sentiment analysis. Transformer models like BERT and GPT have revolutionized NLP." }
        };

        foreach (var doc in documents)
        {
            var document = Document.Create(doc.Id);

            // 문서에 청크 추가
            var chunks = new[]
            {
                new FluxIndex.Domain.Models.DocumentChunk
                {
                    Id = $"{doc.Id}_chunk_1",
                    DocumentId = doc.Id,
                    Content = doc.Content,
                    ChunkIndex = 0,
                    TotalChunks = 1,
                    Metadata = new Dictionary<string, object>
                    {
                        ["title"] = doc.Title,
                        ["category"] = "technology",
                        ["indexed_at"] = DateTime.UtcNow
                    }
                }
            };

            await context.IndexChunksAsync(chunks, doc.Id, new Dictionary<string, object>
            {
                ["title"] = doc.Title,
                ["category"] = "technology"
            });

            await context.IndexAsync(document);
            Console.WriteLine($"   📄 Indexed: {doc.Title}");
        }
    }

    static async Task CompareSearchMethods(IFluxIndexContext context, string query)
    {
        Console.WriteLine($"\n🔍 Query: \"{query}\"");
        Console.WriteLine("───────────────────────────────────────");

        // 벡터 검색
        var vectorStopwatch = Stopwatch.StartNew();
        var vectorResults = await context.SearchAsync(query, maxResults: 5, minScore: 0.1f);
        vectorStopwatch.Stop();

        Console.WriteLine($"📊 Vector Search ({vectorStopwatch.ElapsedMilliseconds}ms):");
        var vectorList = vectorResults.ToList();
        for (int i = 0; i < Math.Min(3, vectorList.Count); i++)
        {
            var result = vectorList[i];
            Console.WriteLine($"   {i + 1}. {result.Metadata.GetValueOrDefault("title", "Unknown")} (Score: {result.Score:F3})");
        }
        Console.WriteLine($"   Total results: {vectorList.Count}");

        // 하이브리드 검색 (기존 방식)
        var hybridStopwatch = Stopwatch.StartNew();
        var hybridResults = await context.HybridSearchAsync(query, query, maxResults: 5, vectorWeight: 0.7f);
        hybridStopwatch.Stop();

        Console.WriteLine($"🔄 Hybrid Search Legacy ({hybridStopwatch.ElapsedMilliseconds}ms):");
        var hybridList = hybridResults.ToList();
        for (int i = 0; i < Math.Min(3, hybridList.Count); i++)
        {
            var result = hybridList[i];
            Console.WriteLine($"   {i + 1}. {result.Metadata.GetValueOrDefault("title", "Unknown")} (Score: {result.Score:F3})");
        }
        Console.WriteLine($"   Total results: {hybridList.Count}");

        // 하이브리드 검색 V2 시도
        try
        {
            var hybridV2Stopwatch = Stopwatch.StartNew();
            var hybridV2Options = new DomainHybridSearchOptions
            {
                MaxResults = 5,
                VectorWeight = 0.7f,
                SparseWeight = 0.3f
            };
            var hybridV2Results = await context.HybridSearchV2Async(query, hybridV2Options);
            hybridV2Stopwatch.Stop();

            Console.WriteLine($"🚀 Hybrid Search V2 ({hybridV2Stopwatch.ElapsedMilliseconds}ms):");
            for (int i = 0; i < Math.Min(3, hybridV2Results.Count); i++)
            {
                var result = hybridV2Results[i];
                Console.WriteLine($"   {i + 1}. Document {result.Chunk.DocumentId} (Score: {result.FusedScore:F3}, Vector: {result.VectorScore:F3}, Sparse: {result.SparseScore:F3})");
            }
            Console.WriteLine($"   Total results: {hybridV2Results.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Hybrid Search V2 failed: {ex.Message}");
        }
    }

    static async Task TestHybridWeights(IFluxIndexContext context, string query)
    {
        Console.WriteLine($"\n🔍 Weight Testing Query: \"{query}\"");
        Console.WriteLine("──────────────────────────────────────────");

        var weights = new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

        foreach (var vectorWeight in weights)
        {
            try
            {
                var options = new DomainHybridSearchOptions
                {
                    MaxResults = 3,
                    VectorWeight = vectorWeight,
                    SparseWeight = 1.0f - vectorWeight
                };

                var results = await context.HybridSearchV2Async(query, options);
                Console.WriteLine($"📊 Vector Weight {vectorWeight:F1}, Sparse Weight {1.0f - vectorWeight:F1}:");

                for (int i = 0; i < Math.Min(2, results.Count); i++)
                {
                    var result = results[i];
                    Console.WriteLine($"   {i + 1}. Doc {result.Chunk.DocumentId} (Fused: {result.FusedScore:F3})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Weight {vectorWeight:F1} failed: {ex.Message}");
            }
        }
    }

    static async Task PerformanceAnalysis(IFluxIndexContext context, string[] queries)
    {
        var vectorTimes = new List<long>();
        var hybridTimes = new List<long>();
        var vectorResultCounts = new List<int>();
        var hybridResultCounts = new List<int>();

        foreach (var query in queries)
        {
            // Vector search performance
            var sw = Stopwatch.StartNew();
            var vectorResults = await context.SearchAsync(query, maxResults: 10, minScore: 0.1f);
            sw.Stop();

            vectorTimes.Add(sw.ElapsedMilliseconds);
            vectorResultCounts.Add(vectorResults.Count());

            // Hybrid search performance (legacy)
            sw.Restart();
            var hybridResults = await context.HybridSearchAsync(query, query, maxResults: 10, vectorWeight: 0.7f);
            sw.Stop();

            hybridTimes.Add(sw.ElapsedMilliseconds);
            hybridResultCounts.Add(hybridResults.Count());

            await Task.Delay(50); // Rate limiting
        }

        Console.WriteLine("📊 Performance Comparison:");
        Console.WriteLine($"   Vector Search:");
        Console.WriteLine($"     - Average time: {vectorTimes.Average():F1}ms");
        Console.WriteLine($"     - Average results: {vectorResultCounts.Average():F1}");
        Console.WriteLine($"   Hybrid Search:");
        Console.WriteLine($"     - Average time: {hybridTimes.Average():F1}ms");
        Console.WriteLine($"     - Average results: {hybridResultCounts.Average():F1}");

        var speedupPercent = ((vectorTimes.Average() - hybridTimes.Average()) / vectorTimes.Average()) * 100;
        var resultImprovementPercent = ((hybridResultCounts.Average() - vectorResultCounts.Average()) / vectorResultCounts.Average()) * 100;

        Console.WriteLine($"📈 Performance Improvement:");
        Console.WriteLine($"   - Speed change: {speedupPercent:F1}% {'i'}");
        Console.WriteLine($"   - Result count change: {resultImprovementPercent:F1}%");
    }
}