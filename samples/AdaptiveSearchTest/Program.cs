using FluxIndex.SDK;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Samples.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace AdaptiveSearchTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🤖 FluxIndex Adaptive Search Test (Phase 8.2)");
        Console.WriteLine("==============================================\n");

        // 환경 변수에서 OpenAI 설정 로드
        var (apiKey, embeddingModel, completionModel) = ConfigurationHelper.LoadOpenAIConfiguration();

        if (!ConfigurationHelper.ValidateApiKey(apiKey))
        {
            return;
        }

        ConfigurationHelper.DisplayConfiguration(apiKey!, embeddingModel, completionModel);

        try
        {
            // 1. FluxIndex 컨텍스트 생성 (적응형 검색 활성화)
            Console.WriteLine("🔧 Phase 1: Adaptive Search System Setup");
            Console.WriteLine("========================================");

            var context = FluxIndexContext.CreateBuilder()
                .UseOpenAI(apiKey, embeddingModel)
                .UseSQLiteInMemory()
                .UseMemoryCache(maxCacheSize: 2000)
                .WithQualityMonitoring(enableRealTimeAlerts: true)
                .Build();

            Console.WriteLine("✅ FluxIndex context with Adaptive Search initialized");

            // 서비스 확인
            var serviceProvider = ((FluxIndexContext)context).ServiceProvider;
            var adaptiveSearchService = serviceProvider.GetService<IAdaptiveSearchService>();
            var queryAnalyzer = serviceProvider.GetService<IQueryComplexityAnalyzer>();

            if (adaptiveSearchService != null && queryAnalyzer != null)
            {
                Console.WriteLine("✅ AdaptiveSearchService and QueryComplexityAnalyzer registered successfully");
            }
            else
            {
                Console.WriteLine("❌ Adaptive Search services not found");
                return;
            }

            // 2. 테스트 데이터 인덱싱
            Console.WriteLine("\n📚 Phase 2: Test Data Indexing for Adaptive Search");
            Console.WriteLine("==================================================");

            await IndexTestDocuments(context);
            Console.WriteLine("✅ 테스트 문서 인덱싱 완료");

            // 3. 쿼리 복잡도 분석 테스트
            Console.WriteLine("\n🧠 Phase 3: Query Complexity Analysis");
            Console.WriteLine("=====================================");

            var testQueries = new[]
            {
                "machine learning",
                "What is machine learning and how does it work?",
                "Compare TensorFlow vs PyTorch for deep learning",
                "How do neural networks learn and why are they effective for pattern recognition?",
                "Explain the mathematical foundations behind transformer attention mechanisms and their advantages over RNNs"
            };

            foreach (var query in testQueries)
            {
                await TestQueryAnalysis(queryAnalyzer, query);
            }

            // 4. 적응형 검색 테스트
            Console.WriteLine("\n🤖 Phase 4: Adaptive Search Testing");
            Console.WriteLine("===================================");

            foreach (var query in testQueries.Take(3))
            {
                await TestAdaptiveSearch(adaptiveSearchService, query);
                await Task.Delay(100); // Rate limiting
            }

            // 5. 전략별 성능 비교
            Console.WriteLine("\n⚖️ Phase 5: Strategy Performance Comparison");
            Console.WriteLine("==========================================");

            await TestStrategyComparison(adaptiveSearchService, "machine learning neural networks");

            // 6. 성능 보고서 생성
            Console.WriteLine("\n📊 Phase 6: Performance Report Generation");
            Console.WriteLine("========================================");

            var performanceReport = await adaptiveSearchService.GetPerformanceReportAsync();
            DisplayPerformanceReport(performanceReport);

            Console.WriteLine("\n🎉 Phase 8.2 적응형 검색 테스트 완료!");
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
            new { Id = "ml_intro", Title = "Introduction to Machine Learning", Content = "Machine learning is a powerful branch of artificial intelligence that enables computers to learn and make decisions from data without being explicitly programmed. It encompasses various algorithms including supervised learning, unsupervised learning, and reinforcement learning. Popular applications include image recognition, natural language processing, and predictive analytics." },
            new { Id = "dl_fundamentals", Title = "Deep Learning Fundamentals", Content = "Deep learning represents a subset of machine learning that uses neural networks with multiple layers to model complex patterns in data. Key architectures include convolutional neural networks (CNNs) for computer vision, recurrent neural networks (RNNs) for sequential data, and transformers for natural language understanding. Training deep networks requires large datasets and computational resources." },
            new { Id = "tensorflow_guide", Title = "TensorFlow Framework Guide", Content = "TensorFlow is Google's open-source machine learning framework designed for production-scale machine learning applications. It provides APIs for Python, JavaScript, and mobile platforms. TensorFlow offers eager execution, robust model deployment options, and comprehensive tools for model development and debugging. The framework excels in both research and production environments." },
            new { Id = "pytorch_overview", Title = "PyTorch Framework Overview", Content = "PyTorch is Facebook's dynamic deep learning framework favored by researchers for its intuitive design and debugging capabilities. It features dynamic computational graphs, pythonic programming interface, and seamless integration with Python's scientific computing ecosystem. PyTorch has gained significant adoption in academic research and is increasingly used in production systems." },
            new { Id = "transformer_architecture", Title = "Transformer Architecture", Content = "The transformer architecture revolutionized natural language processing through the attention mechanism. Unlike RNNs, transformers can process sequences in parallel, making them much faster to train. The self-attention mechanism allows models to focus on relevant parts of the input sequence. Modern language models like BERT, GPT, and T5 are all based on transformer architectures." },
            new { Id = "attention_mechanisms", Title = "Attention Mechanisms in Neural Networks", Content = "Attention mechanisms enable neural networks to focus on specific parts of the input when making predictions. Originally developed for machine translation, attention has become fundamental to many modern architectures. Types include additive attention, multiplicative attention, and self-attention. The attention mechanism has proven crucial for handling long sequences and improving model interpretability." }
        };

        foreach (var doc in documents)
        {
            var document = Document.Create(doc.Id);

            // 문서를 청크로 분할
            var sentences = doc.Content.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var chunks = new List<FluxIndex.Core.Domain.Models.CacheDocumentChunk>();

            for (int i = 0; i < sentences.Length; i++)
            {
                var sentence = sentences[i].Trim();
                if (!string.IsNullOrEmpty(sentence))
                {
                    chunks.Add(new FluxIndex.Core.Domain.Models.CacheDocumentChunk
                    {
                        Id = $"{doc.Id}_chunk_{i}",
                        DocumentId = doc.Id,
                        Content = sentence + ".",
                        ChunkIndex = i,
                        TotalChunks = sentences.Length,
                        Metadata = new Dictionary<string, object>
                        {
                            ["title"] = doc.Title,
                            ["category"] = "AI/ML",
                            ["sentence_index"] = i,
                            ["indexed_at"] = DateTime.UtcNow
                        }
                    });
                }
            }

            await context.IndexChunksAsync(chunks, doc.Id, new Dictionary<string, object>
            {
                ["title"] = doc.Title,
                ["category"] = "AI/ML"
            });

            await context.IndexAsync(document);
            Console.WriteLine($"   📄 Indexed: {doc.Title} ({chunks.Count} chunks)");
        }
    }

    static async Task TestQueryAnalysis(IQueryComplexityAnalyzer analyzer, string query)
    {
        Console.WriteLine($"\n🧠 Query: \"{query}\"");
        Console.WriteLine("─".PadRight(70, '─'));

        var stopwatch = Stopwatch.StartNew();
        var analysis = await analyzer.AnalyzeAsync(query);
        stopwatch.Stop();

        Console.WriteLine($"📊 분석 결과 ({stopwatch.ElapsedMilliseconds}ms):");
        Console.WriteLine($"   쿼리 유형: {analysis.Type}");
        Console.WriteLine($"   복잡도: {analysis.Complexity}");
        Console.WriteLine($"   특정성: {analysis.Specificity:F3}");
        Console.WriteLine($"   의도: {analysis.Intent}");
        Console.WriteLine($"   언어: {analysis.Language}");
        Console.WriteLine($"   추론 필요: {analysis.RequiresReasoning}");
        Console.WriteLine($"   다단계: {analysis.IsMultiHop}");
        Console.WriteLine($"   비교적: {analysis.HasComparativeContext}");
        Console.WriteLine($"   시간적: {analysis.HasTemporalContext}");
        Console.WriteLine($"   신뢰도: {analysis.ConfidenceScore:F3}");
        Console.WriteLine($"   예상 처리시간: {analysis.EstimatedProcessingTime.TotalMilliseconds:F0}ms");

        var recommendedStrategy = analyzer.RecommendStrategy(analysis);
        Console.WriteLine($"   권장 전략: {recommendedStrategy}");

        if (analysis.Keywords.Any())
        {
            Console.WriteLine($"   키워드: {string.Join(", ", analysis.Keywords.Take(5))}");
        }
        if (analysis.Entities.Any())
        {
            Console.WriteLine($"   개체명: {string.Join(", ", analysis.Entities.Take(3))}");
        }
    }

    static async Task TestAdaptiveSearch(IAdaptiveSearchService adaptiveService, string query)
    {
        Console.WriteLine($"\n🤖 Adaptive Search: \"{query}\"");
        Console.WriteLine("─".PadRight(80, '─'));

        var options = new AdaptiveSearchOptions
        {
            MaxResults = 3,
            EnableDetailedLogging = true,
            EnableABTest = false, // A/B 테스트는 리소스 절약을 위해 비활성화
            UseCache = true
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await adaptiveService.SearchAsync(query, options);
        stopwatch.Stop();

        Console.WriteLine($"🎯 적응형 검색 결과 ({stopwatch.ElapsedMilliseconds}ms):");
        Console.WriteLine($"   사용된 전략: {result.UsedStrategy}");
        Console.WriteLine($"   결과 수: {result.Performance.ResultCount}");
        Console.WriteLine($"   평균 관련성: {result.Performance.AverageRelevanceScore:F3}");
        Console.WriteLine($"   분석 시간: {result.Performance.AnalysisTime.TotalMilliseconds:F1}ms");
        Console.WriteLine($"   검색 시간: {result.Performance.SearchTime.TotalMilliseconds:F1}ms");
        Console.WriteLine($"   신뢰도: {result.ConfidenceScore:F3}");
        Console.WriteLine($"   캐시 히트: {result.Performance.CacheHit}");

        if (result.StrategyReasons.Any())
        {
            Console.WriteLine($"   전략 선택 이유:");
            foreach (var reason in result.StrategyReasons)
            {
                Console.WriteLine($"     - {reason}");
            }
        }

        Console.WriteLine($"   검색 결과:");
        var documents = result.Documents.Take(2);
        int index = 1;
        foreach (var doc in documents)
        {
            var chunkContent = doc.Metadata.GetValueOrDefault("chunk_content", "No content") as string ?? "No content";
            var title = doc.Metadata.GetValueOrDefault("title", "No title") as string ?? "No title";
            Console.WriteLine($"     {index}. {title}");
            Console.WriteLine($"        {chunkContent.Substring(0, Math.Min(100, chunkContent.Length))}...");
            index++;
        }
    }

    static async Task TestStrategyComparison(IAdaptiveSearchService adaptiveService, string query)
    {
        Console.WriteLine($"\n⚖️ Strategy Comparison: \"{query}\"");
        Console.WriteLine("─".PadRight(60, '─'));

        var strategies = new[]
        {
            SearchStrategy.DirectVector,
            SearchStrategy.KeywordOnly,
            SearchStrategy.Hybrid,
            SearchStrategy.TwoStage
        };

        foreach (var strategy in strategies)
        {
            try
            {
                var options = new AdaptiveSearchOptions
                {
                    MaxResults = 3,
                    ForceStrategy = strategy,
                    UseCache = false
                };

                var stopwatch = Stopwatch.StartNew();
                var result = await adaptiveService.SearchWithStrategyAsync(query, strategy, options);
                stopwatch.Stop();

                Console.WriteLine($"   {strategy}: {result.Performance.ResultCount}개 결과, " +
                                $"관련성 {result.Performance.AverageRelevanceScore:F3}, " +
                                $"{stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   {strategy}: 오류 - {ex.Message}");
            }

            await Task.Delay(50); // Rate limiting
        }
    }

    static void DisplayPerformanceReport(StrategyPerformanceReport report)
    {
        Console.WriteLine($"📈 성능 보고서 (생성 시간: {report.GeneratedAt:HH:mm:ss})");
        Console.WriteLine("==========================================");

        Console.WriteLine($"전체 통계:");
        Console.WriteLine($"   총 검색 횟수: {report.Overall.TotalSearches}");
        Console.WriteLine($"   평균 처리 시간: {report.Overall.AverageProcessingTime.TotalMilliseconds:F1}ms");
        Console.WriteLine($"   캐시 히트율: {report.Overall.CacheHitRate:P1}");
        Console.WriteLine($"   전체 만족도: {report.Overall.OverallSatisfaction:F2}/5.0");
        Console.WriteLine($"   최다 사용 전략: {report.Overall.MostUsedStrategy}");
        Console.WriteLine($"   최고 성능 전략: {report.Overall.BestPerformingStrategy}");

        if (report.StrategyMetrics.Any())
        {
            Console.WriteLine($"\n전략별 성능:");
            foreach (var kvp in report.StrategyMetrics.Take(5))
            {
                var strategy = kvp.Key;
                var metrics = kvp.Value;
                Console.WriteLine($"   {strategy}:");
                Console.WriteLine($"     사용 횟수: {metrics.TotalUses}");
                Console.WriteLine($"     성공률: {metrics.SuccessRate:P1}");
                Console.WriteLine($"     평균 처리 시간: {metrics.AverageProcessingTime.TotalMilliseconds:F1}ms");
                Console.WriteLine($"     평균 만족도: {metrics.AverageSatisfaction:F2}/5.0");
            }
        }

        if (report.OptimalStrategies.Any())
        {
            Console.WriteLine($"\n쿼리 유형별 최적 전략:");
            foreach (var kvp in report.OptimalStrategies.Take(5))
            {
                Console.WriteLine($"   {kvp.Key}: {kvp.Value}");
            }
        }
    }
}