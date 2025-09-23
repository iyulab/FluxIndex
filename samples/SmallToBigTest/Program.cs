using FluxIndex.SDK;
using FluxIndex.Domain.Entities;
using FluxIndex.Domain.Models;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Samples.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace SmallToBigTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🔍 FluxIndex Small-to-Big Search Test (Phase 8.2)");
        Console.WriteLine("===============================================\n");

        // 환경 변수에서 OpenAI 설정 로드
        var (apiKey, embeddingModel, completionModel) = ConfigurationHelper.LoadOpenAIConfiguration();

        if (!ConfigurationHelper.ValidateApiKey(apiKey))
        {
            return;
        }

        ConfigurationHelper.DisplayConfiguration(apiKey!, embeddingModel, completionModel);

        try
        {
            // 1. FluxIndex 컨텍스트 생성 (Small-to-Big 검색 활성화)
            Console.WriteLine("🔧 Phase 1: Small-to-Big Search System Setup");
            Console.WriteLine("============================================");

            var context = FluxIndexContext.CreateBuilder()
                .UseOpenAI(apiKey!, embeddingModel)
                .UseSQLiteInMemory()
                .UseMemoryCache(maxCacheSize: 2000)
                .WithQualityMonitoring(enableRealTimeAlerts: true)
                .Build();

            Console.WriteLine("✅ FluxIndex context with Small-to-Big search initialized");

            // SmallToBigRetriever 서비스 확인
            var serviceProvider = ((FluxIndexContext)context).ServiceProvider;
            var smallToBigRetriever = serviceProvider.GetService<ISmallToBigRetriever>();

            if (smallToBigRetriever != null)
            {
                Console.WriteLine("✅ SmallToBigRetriever service registered successfully");
            }
            else
            {
                Console.WriteLine("❌ SmallToBigRetriever service not found");
                return;
            }

            // 2. 테스트 데이터 인덱싱
            Console.WriteLine("\n📚 Phase 2: Test Data Indexing for Small-to-Big");
            Console.WriteLine("================================================");

            await IndexTestDocuments(context);
            Console.WriteLine("✅ 테스트 문서 인덱싱 완료");

            // 3. 쿼리 복잡도 분석 테스트
            Console.WriteLine("\n🧠 Phase 3: Query Complexity Analysis");
            Console.WriteLine("=====================================");

            var testQueries = new[]
            {
                "machine learning",
                "how does neural network training work in practice",
                "compare deep learning frameworks TensorFlow vs PyTorch for production deployment",
                "what are the mathematical foundations behind transformer architectures and attention mechanisms"
            };

            foreach (var query in testQueries)
            {
                await TestQueryComplexity(smallToBigRetriever, query);
            }

            // 4. Small-to-Big 검색 테스트
            Console.WriteLine("\n🔍 Phase 4: Small-to-Big Search Testing");
            Console.WriteLine("=======================================");

            foreach (var query in testQueries.Take(2))
            {
                await TestSmallToBigSearch(smallToBigRetriever, query);
                await Task.Delay(100); // Rate limiting
            }

            // 5. 윈도우 크기 최적화 테스트
            Console.WriteLine("\n⚖️ Phase 5: Window Size Optimization");
            Console.WriteLine("====================================");

            await TestWindowSizeOptimization(smallToBigRetriever, "machine learning neural networks");

            Console.WriteLine("\n🎉 Phase 8.2 Small-to-Big 검색 테스트 완료!");
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
            new { Id = "ml_basics", Title = "Machine Learning Basics", Content = "Machine learning is a subset of artificial intelligence that focuses on algorithms and statistical models. Python is widely used for implementing machine learning algorithms due to its extensive libraries like scikit-learn, TensorFlow, and PyTorch. Common algorithms include linear regression, decision trees, and neural networks. The field has evolved significantly with the advent of deep learning." },
            new { Id = "deep_learning", Title = "Deep Learning Guide", Content = "Deep learning uses neural networks with multiple layers to model and understand complex patterns. Popular frameworks include TensorFlow, PyTorch, and Keras. Applications include computer vision, natural language processing, and speech recognition. GPU acceleration is often used for training deep neural networks. The training process involves backpropagation and gradient descent optimization." },
            new { Id = "neural_networks", Title = "Neural Network Architecture", Content = "Neural networks consist of interconnected nodes called neurons arranged in layers. Each connection has a weight that gets adjusted during training. The architecture includes input layers, hidden layers, and output layers. Activation functions like ReLU, sigmoid, and tanh introduce non-linearity. Modern architectures include CNNs for vision and RNNs for sequential data." },
            new { Id = "transformers", Title = "Transformer Architecture", Content = "Transformers revolutionized natural language processing with the attention mechanism. The architecture consists of encoder and decoder blocks with multi-head attention layers. Key innovations include positional encoding, layer normalization, and residual connections. Models like BERT, GPT, and T5 are based on transformer architecture. The attention mechanism allows the model to focus on relevant parts of the input sequence." },
            new { Id = "python_ml", Title = "Python for Machine Learning", Content = "Python has become the dominant language for machine learning due to its rich ecosystem. Key libraries include NumPy for numerical computing, pandas for data manipulation, matplotlib for visualization, and scikit-learn for traditional ML. Deep learning frameworks like TensorFlow and PyTorch provide high-level APIs. Jupyter notebooks enable interactive development and experimentation." },
            new { Id = "ml_deployment", Title = "ML Model Deployment", Content = "Deploying machine learning models to production requires careful consideration of scalability, monitoring, and maintenance. Common deployment patterns include REST APIs, batch processing, and real-time inference. Container technologies like Docker and orchestration with Kubernetes are widely used. Model versioning, A/B testing, and continuous integration are essential practices. MLOps principles help manage the entire ML lifecycle." }
        };

        foreach (var doc in documents)
        {
            var document = Document.Create(doc.Id);

            // 문서를 더 작은 청크로 분할 (Small-to-Big 테스트를 위해)
            var sentences = doc.Content.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var chunks = new List<FluxIndex.Domain.Models.DocumentChunk>();

            for (int i = 0; i < sentences.Length; i++)
            {
                var sentence = sentences[i].Trim();
                if (!string.IsNullOrEmpty(sentence))
                {
                    chunks.Add(new FluxIndex.Domain.Models.DocumentChunk
                    {
                        Id = $"{doc.Id}_chunk_{i}",
                        DocumentId = doc.Id,
                        Content = sentence + ".",
                        ChunkIndex = i,
                        TotalChunks = sentences.Length,
                        Metadata = new Dictionary<string, object>
                        {
                            ["title"] = doc.Title,
                            ["category"] = "technology",
                            ["sentence_index"] = i,
                            ["indexed_at"] = DateTime.UtcNow
                        }
                    });
                }
            }

            await context.IndexChunksAsync(chunks, doc.Id, new Dictionary<string, object>
            {
                ["title"] = doc.Title,
                ["category"] = "technology"
            });

            await context.IndexAsync(document);
            Console.WriteLine($"   📄 Indexed: {doc.Title} ({chunks.Count} chunks)");
        }
    }

    static async Task TestQueryComplexity(ISmallToBigRetriever retriever, string query)
    {
        Console.WriteLine($"\n🧠 Query: \"{query}\"");
        Console.WriteLine("─".PadRight(50, '─'));

        var stopwatch = Stopwatch.StartNew();
        var complexity = await retriever.AnalyzeQueryComplexityAsync(query);
        stopwatch.Stop();

        Console.WriteLine($"📊 복잡도 분석 ({stopwatch.ElapsedMilliseconds}ms):");
        Console.WriteLine($"   전체 복잡도: {complexity.OverallComplexity:F3}");
        Console.WriteLine($"   어휘 복잡도: {complexity.LexicalComplexity:F3}");
        Console.WriteLine($"   구문 복잡도: {complexity.SyntacticComplexity:F3}");
        Console.WriteLine($"   의미 복잡도: {complexity.SemanticComplexity:F3}");
        Console.WriteLine($"   추론 복잡도: {complexity.ReasoningComplexity:F3}");
        Console.WriteLine($"   권장 윈도우: {complexity.RecommendedWindowSize}");
        Console.WriteLine($"   분석 신뢰도: {complexity.AnalysisConfidence:F3}");
    }

    static async Task TestSmallToBigSearch(ISmallToBigRetriever retriever, string query)
    {
        Console.WriteLine($"\n🔍 Small-to-Big Search: \"{query}\"");
        Console.WriteLine("─".PadRight(60, '─'));

        var options = new SmallToBigOptions
        {
            MaxResults = 3,
            EnableAdaptiveWindowing = true,
            EnableHierarchicalExpansion = true,
            EnableSequentialExpansion = true,
            EnableSemanticExpansion = true,
            MinRelevanceScore = 0.1,
            ContextQualityThreshold = 0.3
        };

        var stopwatch = Stopwatch.StartNew();
        var results = await retriever.SearchAsync(query, options);
        stopwatch.Stop();

        Console.WriteLine($"🎯 Small-to-Big 결과 ({stopwatch.ElapsedMilliseconds}ms):");
        Console.WriteLine($"   총 결과: {results.Count}개");

        for (int i = 0; i < Math.Min(2, results.Count); i++)
        {
            var result = results[i];
            Console.WriteLine($"\n   {i + 1}. Primary Chunk: {result.PrimaryChunk.Id}");
            Console.WriteLine($"      내용: {result.PrimaryChunk.Content.Substring(0, Math.Min(80, result.PrimaryChunk.Content.Length))}...");
            Console.WriteLine($"      관련도: {result.RelevanceScore:F3}");
            Console.WriteLine($"      윈도우 크기: {result.WindowSize}");
            Console.WriteLine($"      확장 컨텍스트: {result.ContextChunks.Count}개 청크");

            if (result.Metadata != null)
            {
                Console.WriteLine($"      컨텍스트 품질: {result.Metadata.ContextQualityScore:F3}");
                Console.WriteLine($"      확장 효율성: {result.Metadata.ExpansionEfficiency:F3}");
                Console.WriteLine($"      검색 시간: {result.Metadata.SearchTimeMs:F1}ms");
            }
        }
    }

    static async Task TestWindowSizeOptimization(ISmallToBigRetriever retriever, string query)
    {
        Console.WriteLine($"\n⚖️ Window Size Testing: \"{query}\"");
        Console.WriteLine("─".PadRight(50, '─'));

        var optimalSize = await retriever.DetermineOptimalWindowSizeAsync(query);
        Console.WriteLine($"🎯 최적 윈도우 크기: {optimalSize}");

        // 다양한 윈도우 크기로 테스트
        var windowSizes = new[] { 1, 2, 4, 6, 8 };

        foreach (var windowSize in windowSizes)
        {
            var options = new SmallToBigOptions
            {
                MaxResults = 2,
                DefaultWindowSize = windowSize,
                EnableAdaptiveWindowing = false
            };

            var stopwatch = Stopwatch.StartNew();
            var results = await retriever.SearchAsync(query, options);
            stopwatch.Stop();

            var avgRelevance = results.Any() ? results.Average(r => r.RelevanceScore) : 0.0;
            var avgContextSize = results.Any() ? results.Average(r => r.ContextChunks.Count) : 0.0;

            Console.WriteLine($"   윈도우 {windowSize}: 관련도 {avgRelevance:F3}, " +
                            $"평균 컨텍스트 {avgContextSize:F1}개, {stopwatch.ElapsedMilliseconds}ms");
        }
    }
}