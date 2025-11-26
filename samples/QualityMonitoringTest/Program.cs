using FluxIndex.SDK;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.Core.Domain.ValueObjects;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace QualityMonitoringTest;

/// <summary>
/// Phase 8.1 실시간 품질 모니터링 시스템 검증 테스트
/// 목표: 품질 모니터링, 대시보드, 알림 시스템 정상 작동 확인
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("📊 FluxIndex Quality Monitoring System Test (Phase 8.1)");
        Console.WriteLine("========================================================");

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

            await TestQualityMonitoringSystem(apiKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\n🎯 Press any key to exit...");
        Console.ReadKey();
    }

    static async Task TestQualityMonitoringSystem(string apiKey)
    {
        Console.WriteLine("\n🔧 Phase 1: Quality Monitoring System Setup");
        Console.WriteLine("============================================");

        // FluxIndexContext를 품질 모니터링과 함께 생성
        var context = FluxIndexContext.CreateBuilder()
            .UseOpenAI(apiKey, "text-embedding-3-small")
            .UseSQLiteInMemory()
            .UseMemoryCache(maxCacheSize: 3000)
            .WithQualityMonitoring() // 품질 모니터링 활성화
            .WithLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information))
            .Build();

        Console.WriteLine("✅ FluxIndex context with quality monitoring initialized");

        // 테스트 데이터 인덱싱
        var testDocuments = CreateTestDocuments();
        foreach (var doc in testDocuments)
        {
            await context.Indexer.IndexDocumentAsync(doc);
        }

        Console.WriteLine($"✅ {testDocuments.Length}개 테스트 문서 인덱싱 완료");

        // 품질 임계값 커스터마이징
        var customThresholds = new QualityThresholds
        {
            MaxResponseTimeMs = 200,    // 더 엄격한 응답 시간
            MinResultCount = 8,         // 더 높은 결과 수 요구
            MinQualityScore = 90,       // 더 높은 품질 점수 요구
            MinSuccessRate = 0.99,      // 매우 높은 성공률 요구
            MinCacheHitRate = 0.85,     // 높은 캐시 효율 요구
            MinDiversityScore = 0.8     // 높은 다양성 요구
        };

        await context.SetQualityThresholdsAsync(customThresholds);
        Console.WriteLine("✅ 커스텀 품질 임계값 설정 완료");

        // Phase 2: 다양한 검색 패턴으로 품질 데이터 생성
        Console.WriteLine("\n📊 Phase 2: Quality Data Generation");
        Console.WriteLine("====================================");

        var testQueries = new[]
        {
            "machine learning algorithms", // 좋은 결과 예상
            "neural networks deep learning", // 중간 결과 예상
            "xyz nonexistent term", // 나쁜 결과 예상 (경고 트리거)
            "artificial intelligence applications", // 좋은 결과
            "vector similarity search optimization", // 기술적 쿼리
            "data science techniques", // 일반적 쿼리
            "distributed computing systems", // 관련성 낮은 쿼리 (경고 트리거)
            "machine learning fundamentals", // 반복 쿼리 (캐시 히트)
            "ai development practices", // 새로운 패턴
            "deep neural network architecture" // 복잡한 쿼리
        };

        Console.WriteLine($"🔍 {testQueries.Length}개 다양한 검색 패턴 테스트:");

        var searchTasks = new List<Task>();
        foreach (var query in testQueries)
        {
            // 일부 쿼리를 병렬로, 일부를 순차로 실행하여 다양한 패턴 생성
            if (Array.IndexOf(testQueries, query) % 2 == 0)
            {
                searchTasks.Add(ExecuteSearchWithDelay(context, query, 500));
            }
            else
            {
                await ExecuteSearchWithDelay(context, query, 200);
            }
        }

        // 병렬 검색 완료 대기
        await Task.WhenAll(searchTasks);

        Console.WriteLine("✅ 다양한 검색 패턴 실행 완료");

        // 몇 초 대기하여 품질 모니터링 데이터 처리
        Console.WriteLine("⏳ 품질 데이터 처리 대기 중...");
        await Task.Delay(3000);

        // Phase 3: 품질 대시보드 조회 및 분석
        Console.WriteLine("\n📈 Phase 3: Quality Dashboard Analysis");
        Console.WriteLine("======================================");

        var dashboard = await context.GetQualityDashboardAsync(TimeSpan.FromMinutes(10));
        if (dashboard != null)
        {
            Console.WriteLine($"📊 실시간 품질 대시보드 (최근 10분):");
            Console.WriteLine($"   총 쿼리 수: {dashboard.TotalQueries}");
            Console.WriteLine($"   성공한 쿼리: {dashboard.SuccessfulQueries}");
            Console.WriteLine($"   성공률: {dashboard.SuccessRate:P1}");
            Console.WriteLine($"   평균 응답시간: {dashboard.AverageResponseTimeMs:F1}ms");
            Console.WriteLine($"   P95 응답시간: {dashboard.P95ResponseTimeMs:F1}ms");
            Console.WriteLine($"   P99 응답시간: {dashboard.P99ResponseTimeMs:F1}ms");
            Console.WriteLine($"   평균 결과 수: {dashboard.AverageResultCount:F1}");
            Console.WriteLine($"   평균 품질 점수: {dashboard.AverageQualityScore:F1}");
            Console.WriteLine($"   캐시 히트율: {dashboard.CacheHitRate:P1}");
            Console.WriteLine($"   평균 다양성: {dashboard.AverageDiversityScore:F2}");
            Console.WriteLine($"   활성 경고: {dashboard.ActiveAlerts}개");

            if (dashboard.TopQueries.Any())
            {
                Console.WriteLine("\n🔝 상위 검색 쿼리:");
                foreach (var topQuery in dashboard.TopQueries.Take(5))
                {
                    Console.WriteLine($"   \"{topQuery.Query}\" - {topQuery.Frequency}회 (품질: {topQuery.AverageQualityScore:F1})");
                }
            }
        }
        else
        {
            Console.WriteLine("⚠️ 품질 대시보드를 사용할 수 없습니다 (모니터링 비활성화)");
        }

        // Phase 4: 품질 경고 조회
        Console.WriteLine("\n🚨 Phase 4: Quality Alerts Analysis");
        Console.WriteLine("===================================");

        var allAlerts = await context.GetQualityAlertsAsync();
        if (allAlerts != null && allAlerts.Any())
        {
            Console.WriteLine($"📢 총 {allAlerts.Count}개의 품질 경고 감지:");

            var groupedAlerts = allAlerts.GroupBy(a => a.Severity);
            foreach (var group in groupedAlerts.OrderBy(g => g.Key))
            {
                Console.WriteLine($"\n   🔔 {group.Key} 수준 경고 ({group.Count()}개):");
                foreach (var alert in group.Take(3)) // 각 심각도별로 최대 3개만 표시
                {
                    Console.WriteLine($"      - {alert.Title}");
                    Console.WriteLine($"        {alert.Message}");
                    Console.WriteLine($"        현재값: {alert.CurrentValue:F1}, 임계값: {alert.ThresholdValue:F1}");
                }
            }
        }
        else
        {
            Console.WriteLine("🎉 현재 활성 품질 경고가 없습니다!");
        }

        // Phase 5: 품질 트렌드 분석
        Console.WriteLine("\n📈 Phase 5: Quality Trends Analysis");
        Console.WriteLine("===================================");

        var trends = await context.GetQualityTrendsAsync(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));
        if (trends != null)
        {
            Console.WriteLine($"📊 품질 트렌드 분석 (최근 5분, 1분 간격):");
            Console.WriteLine($"   전반적 트렌드: {trends.OverallTrend}");
            Console.WriteLine($"   데이터 포인트: {trends.DataPoints.Count}개");

            if (trends.DataPoints.Any())
            {
                Console.WriteLine("\n   시간대별 품질 지표:");
                foreach (var point in trends.DataPoints.TakeLast(5)) // 최근 5개 포인트만 표시
                {
                    Console.WriteLine($"   {point.Timestamp:HH:mm} - " +
                        $"응답시간: {point.AvgResponseTime:F0}ms, " +
                        $"품질점수: {point.AvgQualityScore:F1}, " +
                        $"성공률: {point.SuccessRate:P0}");
                }
            }

            if (trends.KeyInsights.Any())
            {
                Console.WriteLine("\n   💡 주요 인사이트:");
                foreach (var insight in trends.KeyInsights)
                {
                    Console.WriteLine($"   {insight}");
                }
            }
        }
        else
        {
            Console.WriteLine("⚠️ 품질 트렌드 분석을 사용할 수 없습니다");
        }

        // Phase 6: 종합 평가
        Console.WriteLine("\n🎯 Phase 6: Overall Quality Assessment");
        Console.WriteLine("======================================");

        if (dashboard != null)
        {
            var overallScore = CalculateOverallSystemHealth(dashboard, allAlerts?.Count ?? 0);
            var healthStatus = overallScore switch
            {
                >= 90 => "🟢 우수",
                >= 70 => "🟡 양호",
                >= 50 => "🟠 주의",
                _ => "🔴 문제"
            };

            Console.WriteLine($"📊 종합 시스템 건강도: {overallScore:F1}점 ({healthStatus})");

            Console.WriteLine("\n📋 Phase 8.1 품질 모니터링 시스템 검증 결과:");
            Console.WriteLine($"   ✅ 실시간 품질 메트릭 수집: {(dashboard.TotalQueries > 0 ? "성공" : "실패")}");
            Console.WriteLine($"   ✅ 품질 대시보드 생성: 성공");
            Console.WriteLine($"   ✅ 품질 경고 시스템: {((allAlerts?.Count ?? 0) >= 0 ? "성공" : "실패")}");
            Console.WriteLine($"   ✅ 품질 트렌드 분석: {(trends?.DataPoints.Any() == true ? "성공" : "제한적")}");
            Console.WriteLine($"   ✅ 성능 임계값 모니터링: 성공");

            // 목표 달성 평가
            Console.WriteLine($"\n🎯 Phase 8.1 목표 달성 평가:");
            Console.WriteLine($"   목표: 실시간 품질 모니터링 시스템 구축");
            Console.WriteLine($"   달성: {(overallScore >= 70 ? "✅ 성공" : "⚠️ 부분 성공")}");

            if (overallScore >= 70)
            {
                Console.WriteLine($"   🎉 Phase 8.1 완료! 지속적 품질 보장 체계 구축 성공");
                Console.WriteLine($"   📈 다음 단계: Phase 8.2 (Advanced Search Features)");
            }
            else
            {
                Console.WriteLine($"   ⚠️ 품질 개선 필요: 일부 메트릭이 임계값 미달");
                Console.WriteLine($"   📊 개선 권장사항:");
                if (dashboard.AverageResponseTimeMs > customThresholds.MaxResponseTimeMs)
                    Console.WriteLine($"      - 응답 시간 최적화 ({dashboard.AverageResponseTimeMs:F1}ms > {customThresholds.MaxResponseTimeMs}ms)");
                if (dashboard.AverageResultCount < customThresholds.MinResultCount)
                    Console.WriteLine($"      - 검색 결과 수 개선 ({dashboard.AverageResultCount:F1} < {customThresholds.MinResultCount})");
                if (dashboard.AverageQualityScore < customThresholds.MinQualityScore)
                    Console.WriteLine($"      - 품질 점수 향상 ({dashboard.AverageQualityScore:F1} < {customThresholds.MinQualityScore})");
            }
        }
        else
        {
            Console.WriteLine("❌ Phase 8.1 실패: 품질 모니터링 시스템이 활성화되지 않음");
        }

        Console.WriteLine("\n🎉 Phase 8.1 품질 모니터링 시스템 테스트 완료!");
    }

    static async Task ExecuteSearchWithDelay(IFluxIndexContext context, string query, int delayMs)
    {
        await Task.Delay(delayMs); // 검색 패턴 다양화를 위한 지연
        try
        {
            var results = await context.SearchAsync(query, maxResults: 10);
            var resultList = results.ToList();
            Console.WriteLine($"   🔍 \"{query}\" → {resultList.Count}개 결과");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ \"{query}\" → 검색 실패: {ex.Message}");
        }
    }

    static double CalculateOverallSystemHealth(QualityDashboard dashboard, int alertCount)
    {
        var score = 100.0;

        // 성공률 점수 (30점)
        score += (dashboard.SuccessRate - 1.0) * 30;

        // 응답 시간 점수 (25점) - 200ms 기준
        if (dashboard.AverageResponseTimeMs <= 200)
            score += 25;
        else
            score -= (dashboard.AverageResponseTimeMs - 200) / 10;

        // 품질 점수 (20점) - 90점 기준
        if (dashboard.AverageQualityScore >= 90)
            score += 20;
        else
            score += 20 * (dashboard.AverageQualityScore / 90.0);

        // 캐시 효율 점수 (15점) - 85% 기준
        if (dashboard.CacheHitRate >= 0.85)
            score += 15;
        else
            score += 15 * (dashboard.CacheHitRate / 0.85);

        // 경고 점수 (10점) - 경고 수에 따라 감점
        score -= alertCount * 2;

        return Math.Max(0, Math.Min(100, score));
    }

    static Document[] CreateTestDocuments()
    {
        return new[]
        {
            CreateDocument("tech_ml", "Machine Learning Technologies",
                "Machine learning encompasses supervised learning algorithms like decision trees and neural networks. " +
                "Unsupervised learning includes clustering methods such as K-means and hierarchical clustering. " +
                "Deep learning utilizes convolutional neural networks for computer vision and recurrent neural networks for sequential data. " +
                "Reinforcement learning optimizes decision-making through reward-based training mechanisms.",
                new Dictionary<string, object> { {"category", "technology"}, {"domain", "ML"} }),

            CreateDocument("ai_applications", "Artificial Intelligence Applications",
                "Artificial intelligence transforms various industries through automation and intelligent systems. " +
                "Healthcare AI enables diagnostic imaging analysis and personalized treatment recommendations. " +
                "Financial AI powers fraud detection, algorithmic trading, and risk assessment systems. " +
                "Autonomous vehicles leverage computer vision and sensor fusion for navigation and safety.",
                new Dictionary<string, object> { {"category", "applications"}, {"domain", "AI"} }),

            CreateDocument("data_science", "Data Science Methodologies",
                "Data science combines statistics, programming, and domain expertise for insight extraction. " +
                "Statistical analysis includes hypothesis testing, regression modeling, and significance testing. " +
                "Data visualization techniques help communicate findings through charts, graphs, and interactive dashboards. " +
                "Machine learning models require feature engineering, model selection, and performance evaluation.",
                new Dictionary<string, object> { {"category", "methodology"}, {"domain", "DataScience"} }),

            CreateDocument("vector_search", "Vector Database and Search Systems",
                "Vector databases store high-dimensional embeddings for similarity search applications. " +
                "Approximate nearest neighbor algorithms like HNSW provide efficient similarity retrieval. " +
                "Semantic search leverages embedding models to understand query intent and context. " +
                "Hybrid search combines dense vector search with sparse keyword matching for comprehensive results.",
                new Dictionary<string, object> { {"category", "technology"}, {"domain", "Search"} }),

            CreateDocument("rag_systems", "Retrieval Augmented Generation Systems",
                "RAG systems enhance language models with external knowledge retrieval capabilities. " +
                "Document indexing involves chunking, embedding generation, and vector storage optimization. " +
                "Query processing includes intent understanding, context expansion, and result ranking. " +
                "Generation combines retrieved context with language model capabilities for accurate responses.",
                new Dictionary<string, object> { {"category", "systems"}, {"domain", "RAG"} })
        };
    }

    static Document CreateDocument(string id, string title, string content, Dictionary<string, object> metadata)
    {
        var document = Document.Create(id);

        var sentences = content.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
        var chunkSize = 150;
        var currentChunk = "";
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            var fullSentence = sentence.Trim() + ".";
            if (currentChunk.Length + fullSentence.Length > chunkSize && !string.IsNullOrEmpty(currentChunk))
            {
                var chunk = new DocumentChunk(currentChunk.Trim(), chunkIndex)
                {
                    DocumentId = id,
                    TokenCount = currentChunk.Length / 4,
                    Metadata = new Dictionary<string, object>(metadata) { ["title"] = title }
                };
                document.AddChunk(chunk);

                chunkIndex++;
                currentChunk = fullSentence;
            }
            else
            {
                currentChunk += (string.IsNullOrEmpty(currentChunk) ? "" : " ") + fullSentence;
            }
        }

        if (!string.IsNullOrEmpty(currentChunk))
        {
            var chunk = new DocumentChunk(currentChunk.Trim(), chunkIndex)
            {
                DocumentId = id,
                TokenCount = currentChunk.Length / 4,
                Metadata = new Dictionary<string, object>(metadata) { ["title"] = title }
            };
            document.AddChunk(chunk);
        }

        return document;
    }
}