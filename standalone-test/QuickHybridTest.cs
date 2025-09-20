using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RealQualityTest;

public class QuickHybridTest : IDisposable
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;

    public QuickHybridTest(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task RunTestAsync()
    {
        Console.WriteLine("================================");
        Console.WriteLine("   하이브리드 검색 품질 테스트   ");
        Console.WriteLine("================================");
        Console.WriteLine();

        var documents = new[]
        {
            "Machine learning is a subset of artificial intelligence that focuses on algorithms.",
            "Deep learning uses neural networks with multiple layers to process information.",
            "Natural language processing enables computers to understand human language.",
            "Computer vision allows machines to interpret visual information from images.",
            "Data science combines statistics and programming to extract insights from data."
        };

        var testQueries = new[]
        {
            "machine learning algorithms",
            "neural networks processing",
            "understanding language",
            "visual image analysis",
            "data analysis techniques"
        };

        var results = new List<TestResult>();

        for (int i = 0; i < testQueries.Length; i++)
        {
            var query = testQueries[i];
            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine($"🔍 테스트 {i + 1}: {query}");

            try
            {
                // 1. 벡터 검색 시뮬레이션
                var queryEmbedding = await GetEmbeddingAsync(query);
                var vectorResults = new List<(int docIndex, double score)>();

                for (int j = 0; j < documents.Length; j++)
                {
                    var docEmbedding = await GetEmbeddingAsync(documents[j]);
                    var similarity = CalculateCosineSimilarity(queryEmbedding, docEmbedding);
                    vectorResults.Add((j, similarity));
                }

                vectorResults = vectorResults.OrderByDescending(x => x.score).Take(3).ToList();

                // 2. 키워드 검색 시뮬레이션
                var keywordResults = new List<(int docIndex, double score)>();
                var queryTerms = query.ToLower().Split(' ');

                for (int j = 0; j < documents.Length; j++)
                {
                    var docTerms = documents[j].ToLower().Split(' ');
                    var matchCount = queryTerms.Count(term => docTerms.Any(docTerm => docTerm.Contains(term)));
                    var score = (double)matchCount / queryTerms.Length;
                    keywordResults.Add((j, score));
                }

                keywordResults = keywordResults.Where(x => x.score > 0).OrderByDescending(x => x.score).Take(3).ToList();

                // 3. 하이브리드 융합 (RRF)
                var hybridScores = new Dictionary<int, double>();
                var k = 60.0;

                // 벡터 결과 처리
                for (int rank = 0; rank < vectorResults.Count; rank++)
                {
                    var docIndex = vectorResults[rank].docIndex;
                    var rrfScore = 1.0 / (k + rank + 1);
                    hybridScores[docIndex] = hybridScores.GetValueOrDefault(docIndex, 0) + rrfScore * 0.7;
                }

                // 키워드 결과 처리
                for (int rank = 0; rank < keywordResults.Count; rank++)
                {
                    var docIndex = keywordResults[rank].docIndex;
                    var rrfScore = 1.0 / (k + rank + 1);
                    hybridScores[docIndex] = hybridScores.GetValueOrDefault(docIndex, 0) + rrfScore * 0.3;
                }

                var hybridResults = hybridScores.OrderByDescending(x => x.Value).Take(3).ToList();

                stopwatch.Stop();

                // 결과 출력
                Console.WriteLine($"   ⏱️  실행시간: {stopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine($"   📊 벡터 검색 결과: {vectorResults.Count}개");
                Console.WriteLine($"   🔤 키워드 검색 결과: {keywordResults.Count}개");
                Console.WriteLine($"   🔀 하이브리드 결과: {hybridResults.Count}개");

                Console.WriteLine("   🥇 상위 하이브리드 결과:");
                for (int r = 0; r < Math.Min(2, hybridResults.Count); r++)
                {
                    var docIndex = hybridResults[r].Key;
                    var score = hybridResults[r].Value;
                    var preview = documents[docIndex].Length > 50 ? documents[docIndex].Substring(0, 50) + "..." : documents[docIndex];
                    Console.WriteLine($"      {r + 1}. [문서{docIndex + 1}] 점수: {score:F3} - {preview}");
                }

                results.Add(new TestResult
                {
                    Query = query,
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                    VectorCount = vectorResults.Count,
                    KeywordCount = keywordResults.Count,
                    HybridCount = hybridResults.Count,
                    Success = true
                });

                Console.WriteLine();
                await Task.Delay(1000); // API 제한 고려
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 오류: {ex.Message}");
                results.Add(new TestResult
                {
                    Query = query,
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                    Success = false,
                    Error = ex.Message
                });
                Console.WriteLine();
            }
        }

        // 전체 결과 요약
        Console.WriteLine("================================");
        Console.WriteLine("        전체 결과 요약           ");
        Console.WriteLine("================================");

        var successfulTests = results.Where(r => r.Success).ToList();
        if (successfulTests.Any())
        {
            var avgTime = successfulTests.Average(r => r.ElapsedMs);
            var avgVectorCount = successfulTests.Average(r => r.VectorCount);
            var avgKeywordCount = successfulTests.Average(r => r.KeywordCount);
            var avgHybridCount = successfulTests.Average(r => r.HybridCount);

            Console.WriteLine($"📊 성공한 테스트: {successfulTests.Count}/{results.Count}");
            Console.WriteLine($"⏱️  평균 응답시간: {avgTime:F0}ms");
            Console.WriteLine($"📈 평균 결과 수:");
            Console.WriteLine($"   - 벡터: {avgVectorCount:F1}개");
            Console.WriteLine($"   - 키워드: {avgKeywordCount:F1}개");
            Console.WriteLine($"   - 하이브리드: {avgHybridCount:F1}개");

            Console.WriteLine();
            Console.WriteLine("🎯 성능 평가:");
            if (avgTime <= 1000) Console.WriteLine("   ✅ 응답시간: 우수 (1초 이하)");
            else if (avgTime <= 2000) Console.WriteLine("   ⚠️  응답시간: 보통 (2초 이하)");
            else Console.WriteLine("   ❌ 응답시간: 개선 필요 (2초 초과)");

            if (avgHybridCount >= 2) Console.WriteLine("   ✅ 결과 품질: 좋음 (충분한 결과)");
            else Console.WriteLine("   ⚠️  결과 품질: 개선 고려");
        }

        var failedTests = results.Where(r => !r.Success).ToList();
        if (failedTests.Any())
        {
            Console.WriteLine();
            Console.WriteLine("❌ 실패한 테스트:");
            foreach (var failed in failedTests)
            {
                Console.WriteLine($"   - {failed.Query}: {failed.Error}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("🎉 하이브리드 검색 테스트 완료!");
    }

    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        var requestBody = new
        {
            input = text,
            model = "text-embedding-3-small",
            encoding_format = "float"
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseJson);

        var embeddingArray = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");

        var embedding = new float[embeddingArray.GetArrayLength()];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = embeddingArray[i].GetSingle();
        }

        return embedding;
    }

    private double CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        var dotProduct = 0.0;
        var normA = 0.0;
        var normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public class TestResult
{
    public string Query { get; set; } = "";
    public double ElapsedMs { get; set; }
    public int VectorCount { get; set; }
    public int KeywordCount { get; set; }
    public int HybridCount { get; set; }
    public bool Success { get; set; }
    public string Error { get; set; } = "";
}

public class QuickTestRunner
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("FluxIndex 하이브리드 검색 품질 테스트");
        Console.WriteLine();

        var apiKey = LoadApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("❌ OpenAI API 키를 찾을 수 없습니다.");
            Console.WriteLine("⚠️  .env.local 파일에 OPENAI_API_KEY=your-key-here 를 설정하세요.");
            return;
        }

        Console.WriteLine($"✅ API 키 로드 완료: {apiKey.Substring(0, 8)}...");
        Console.WriteLine();

        using var test = new QuickHybridTest(apiKey);
        await test.RunTestAsync();

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static string LoadApiKey()
    {
        var envFile = ".env.local";
        if (!System.IO.File.Exists(envFile))
        {
            return Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        }

        try
        {
            var lines = System.IO.File.ReadAllLines(envFile);
            foreach (var line in lines)
            {
                if (line.StartsWith("OPENAI_API_KEY="))
                {
                    return line.Substring("OPENAI_API_KEY=".Length).Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ .env.local 파일 읽기 오류: {ex.Message}");
        }

        return "";
    }
}