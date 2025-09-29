using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluxIndex.RealApiTest;

/// <summary>
/// OpenAI API 성능 및 품질 테스트
/// </summary>
public class PerformanceTest
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly List<PerformanceMetric> _metrics = new();

    public static async Task RunAsync(string apiKey)
    {
        var performanceTest = new PerformanceTest();

        Console.WriteLine("\n🚀 성능 및 품질 테스트 시작");
        Console.WriteLine(new string('=', 50));

        await performanceTest.RunResponseTimeTest(apiKey);
        await performanceTest.RunThroughputTest(apiKey);
        await performanceTest.RunQualityTest(apiKey);
        performanceTest.GeneratePerformanceReport();
    }

    public async Task RunResponseTimeTest(string apiKey)
    {
        Console.WriteLine("\n⏱️ 응답시간 테스트");
        Console.WriteLine(new string('-', 30));

        var testTexts = new[]
        {
            "짧은 텍스트",
            "중간 길이의 텍스트입니다. FluxIndex는 RAG 시스템을 위한 라이브러리로서 다양한 기능을 제공합니다.",
            "긴 텍스트입니다. FluxIndex는 검색 증강 생성(RAG) 시스템을 구축하기 위한 포괄적인 라이브러리입니다. 이 라이브러리는 문서 인덱싱, 벡터 저장소, 하이브리드 검색, 그리고 다양한 AI 모델과의 통합을 지원합니다. PostgreSQL과 pgvector를 사용한 벡터 저장소, Redis를 활용한 캐싱, OpenAI 및 Azure OpenAI와의 통합 등 현대적인 RAG 시스템 구축에 필요한 모든 구성 요소를 포함하고 있습니다."
        };

        foreach (var text in testTexts)
        {
            Console.Write($"📝 텍스트 길이 {text.Length}자: ");

            var stopwatch = Stopwatch.StartNew();
            var success = await TestEmbeddingGeneration(apiKey, text);
            stopwatch.Stop();

            var metric = new PerformanceMetric
            {
                TestType = "응답시간",
                InputSize = text.Length,
                ResponseTime = stopwatch.ElapsedMilliseconds,
                Success = success,
                Timestamp = DateTime.UtcNow
            };
            _metrics.Add(metric);

            if (success)
            {
                Console.WriteLine($"✅ {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                Console.WriteLine($"❌ 실패 ({stopwatch.ElapsedMilliseconds}ms)");
            }
        }
    }

    public async Task RunThroughputTest(string apiKey)
    {
        Console.WriteLine("\n📊 처리량 테스트 (10개 요청 동시 처리)");
        Console.WriteLine(new string('-', 40));

        var testText = "FluxIndex는 RAG 시스템을 위한 고성능 라이브러리입니다.";
        var tasks = new List<Task<(bool success, long responseTime)>>();

        var overallStopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(MeasureEmbeddingRequest(apiKey, testText, i + 1));
        }

        var results = await Task.WhenAll(tasks);
        overallStopwatch.Stop();

        var successCount = results.Count(r => r.success);
        var averageResponseTime = results.Where(r => r.success).Average(r => r.responseTime);
        var throughput = (double)successCount / (overallStopwatch.ElapsedMilliseconds / 1000.0);

        Console.WriteLine($"✅ 성공: {successCount}/10");
        Console.WriteLine($"⏱️ 평균 응답시간: {averageResponseTime:F1}ms");
        Console.WriteLine($"🚄 처리량: {throughput:F2} 요청/초");
        Console.WriteLine($"🕒 전체 소요시간: {overallStopwatch.ElapsedMilliseconds}ms");

        _metrics.Add(new PerformanceMetric
        {
            TestType = "처리량",
            InputSize = testText.Length,
            ResponseTime = (long)averageResponseTime,
            Success = successCount == 10,
            Throughput = throughput,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task RunQualityTest(string apiKey)
    {
        Console.WriteLine("\n🎯 임베딩 품질 테스트");
        Console.WriteLine(new string('-', 30));

        var semanticPairs = new[]
        {
            ("검색", "찾기"),
            ("문서", "도큐먼트"),
            ("인공지능", "AI"),
            ("데이터베이스", "DB"),
            ("성능", "퍼포먼스")
        };

        var qualityScores = new List<double>();

        foreach (var (word1, word2) in semanticPairs)
        {
            Console.Write($"🔍 '{word1}' vs '{word2}': ");

            var similarity = await CalculateCosineSimilarity(apiKey, word1, word2);
            qualityScores.Add(similarity);

            Console.WriteLine($"{similarity:F3}");
        }

        var averageQuality = qualityScores.Average();
        Console.WriteLine($"\n📈 평균 의미적 유사도: {averageQuality:F3}");

        _metrics.Add(new PerformanceMetric
        {
            TestType = "품질",
            Success = averageQuality > 0.7, // 임계값 0.7
            QualityScore = averageQuality,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task<(bool success, long responseTime)> MeasureEmbeddingRequest(string apiKey, string text, int requestId)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var success = await TestEmbeddingGeneration(apiKey, text);
            stopwatch.Stop();
            return (success, stopwatch.ElapsedMilliseconds);
        }
        catch
        {
            stopwatch.Stop();
            return (false, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<bool> TestEmbeddingGeneration(string apiKey, string text)
    {
        var requestBody = new
        {
            input = text,
            model = "text-embedding-3-small"
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content);
        return response.IsSuccessStatusCode;
    }

    private async Task<double> CalculateCosineSimilarity(string apiKey, string text1, string text2)
    {
        try
        {
            var embedding1 = await GetEmbedding(apiKey, text1);
            var embedding2 = await GetEmbedding(apiKey, text2);

            if (embedding1 == null || embedding2 == null) return 0.0;

            return ComputeCosineSimilarity(embedding1, embedding2);
        }
        catch
        {
            return 0.0;
        }
    }

    private async Task<float[]?> GetEmbedding(string apiKey, string text)
    {
        var requestBody = new
        {
            input = text,
            model = "text-embedding-3-small"
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content);

        if (!response.IsSuccessStatusCode) return null;

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseJson = JsonDocument.Parse(responseContent);

        var embeddingArray = responseJson.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");

        var embedding = new float[embeddingArray.GetArrayLength()];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = embeddingArray[i].GetSingle();
        }

        return embedding;
    }

    private double ComputeCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length) return 0.0;

        double dotProduct = 0.0;
        double magnitude1 = 0.0;
        double magnitude2 = 0.0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0.0 || magnitude2 == 0.0) return 0.0;

        return dotProduct / (magnitude1 * magnitude2);
    }

    private void GeneratePerformanceReport()
    {
        Console.WriteLine("\n" + new string('=', 50));
        Console.WriteLine("📊 성능 및 품질 분석 보고서");
        Console.WriteLine(new string('=', 50));

        // 응답시간 분석
        var responseTimeMetrics = _metrics.Where(m => m.TestType == "응답시간").ToList();
        if (responseTimeMetrics.Any())
        {
            var avgResponseTime = responseTimeMetrics.Average(m => m.ResponseTime);
            var maxResponseTime = responseTimeMetrics.Max(m => m.ResponseTime);
            var minResponseTime = responseTimeMetrics.Min(m => m.ResponseTime);

            Console.WriteLine($"\n⏱️ 응답시간 분석:");
            Console.WriteLine($"   평균: {avgResponseTime:F1}ms");
            Console.WriteLine($"   최대: {maxResponseTime}ms");
            Console.WriteLine($"   최소: {minResponseTime}ms");
        }

        // 처리량 분석
        var throughputMetric = _metrics.FirstOrDefault(m => m.TestType == "처리량");
        if (throughputMetric != null)
        {
            Console.WriteLine($"\n🚄 처리량 분석:");
            Console.WriteLine($"   처리량: {throughputMetric.Throughput:F2} 요청/초");
            Console.WriteLine($"   성공률: {(throughputMetric.Success ? "100%" : "< 100%")}");
        }

        // 품질 분석
        var qualityMetric = _metrics.FirstOrDefault(m => m.TestType == "품질");
        if (qualityMetric != null)
        {
            Console.WriteLine($"\n🎯 품질 분석:");
            Console.WriteLine($"   평균 유사도: {qualityMetric.QualityScore:F3}");
            Console.WriteLine($"   품질 평가: {(qualityMetric.QualityScore > 0.7 ? "우수" : "보통")}");
        }

        // 성능 리포트 파일 저장
        SaveDetailedReport();
    }

    private void SaveDetailedReport()
    {
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "performance-test-results.txt");
        var reportContent = new StringBuilder();

        reportContent.AppendLine("FluxIndex Performance & Quality Test Results");
        reportContent.AppendLine("==========================================");
        reportContent.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        reportContent.AppendLine();

        // 상세 메트릭 기록
        foreach (var metric in _metrics)
        {
            reportContent.AppendLine($"Test Type: {metric.TestType}");
            reportContent.AppendLine($"Timestamp: {metric.Timestamp:yyyy-MM-dd HH:mm:ss}");
            reportContent.AppendLine($"Success: {metric.Success}");

            if (metric.ResponseTime > 0)
                reportContent.AppendLine($"Response Time: {metric.ResponseTime}ms");

            if (metric.InputSize > 0)
                reportContent.AppendLine($"Input Size: {metric.InputSize} characters");

            if (metric.Throughput > 0)
                reportContent.AppendLine($"Throughput: {metric.Throughput:F2} requests/sec");

            if (metric.QualityScore > 0)
                reportContent.AppendLine($"Quality Score: {metric.QualityScore:F3}");

            reportContent.AppendLine();
        }

        reportContent.AppendLine("Generated by FluxIndex Performance Test");

        try
        {
            File.WriteAllText(reportPath, reportContent.ToString());
            Console.WriteLine($"\n📄 상세 성능 보고서 저장됨: {reportPath}");
        }
        catch
        {
            Console.WriteLine("\n⚠️ 성능 보고서 저장 실패");
        }
    }
}

public class PerformanceMetric
{
    public string TestType { get; set; } = "";
    public int InputSize { get; set; }
    public long ResponseTime { get; set; }
    public bool Success { get; set; }
    public double Throughput { get; set; }
    public double QualityScore { get; set; }
    public DateTime Timestamp { get; set; }
}