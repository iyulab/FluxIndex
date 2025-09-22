using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;
using TestDocument = RealQualityTest.StandaloneTestDocument;

namespace RealQualityTest;

/// <summary>
/// 스탠드얼론 하이브리드 검색 성능 테스트
/// OpenAI API를 직접 사용하여 하이브리드 검색의 품질과 성능을 평가
/// </summary>
public class StandaloneHybridTest
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly List<TestDocument> _documents;

    public StandaloneHybridTest(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        _documents = InitializeTestDocuments();
    }

    /// <summary>
    /// 테스트 문서 집합 초기화
    /// </summary>
    private List<TestDocument> InitializeTestDocuments()
    {
        return new List<TestDocument>
        {
            new("doc1", "Machine learning is a subset of artificial intelligence that focuses on algorithms that can learn from data without explicit programming.",
                new[] { "machine", "learning", "artificial", "intelligence", "algorithms", "data", "programming" }),

            new("doc2", "Deep learning uses neural networks with multiple layers to process information in complex ways, enabling breakthrough in computer vision and natural language processing.",
                new[] { "deep", "learning", "neural", "networks", "layers", "computer", "vision", "language", "processing" }),

            new("doc3", "Natural language processing enables computers to understand and generate human language through various techniques like tokenization, parsing, and semantic analysis.",
                new[] { "natural", "language", "processing", "computers", "understand", "generate", "human", "tokenization", "parsing", "semantic" }),

            new("doc4", "Computer vision allows machines to interpret and understand visual information from images and videos using convolutional neural networks.",
                new[] { "computer", "vision", "machines", "interpret", "visual", "information", "images", "videos", "convolutional", "neural" }),

            new("doc5", "Reinforcement learning is a type of machine learning where agents learn optimal actions through trial and error in an environment.",
                new[] { "reinforcement", "learning", "machine", "agents", "optimal", "actions", "trial", "error", "environment" }),

            new("doc6", "Data science combines statistics, programming, and domain expertise to extract insights from large datasets using various analytical methods.",
                new[] { "data", "science", "statistics", "programming", "domain", "expertise", "insights", "datasets", "analytical", "methods" }),

            new("doc7", "Big data refers to extremely large datasets that require specialized tools and techniques to process, store, and analyze efficiently.",
                new[] { "big", "data", "large", "datasets", "specialized", "tools", "techniques", "process", "store", "analyze" }),

            new("doc8", "Cloud computing provides on-demand access to computing resources over the internet, enabling scalable and flexible infrastructure solutions.",
                new[] { "cloud", "computing", "demand", "access", "computing", "resources", "internet", "scalable", "flexible", "infrastructure" }),

            new("doc9", "Cybersecurity protects digital systems and data from unauthorized access and attacks through various security measures and protocols.",
                new[] { "cybersecurity", "protects", "digital", "systems", "data", "unauthorized", "access", "attacks", "security", "measures" }),

            new("doc10", "Blockchain technology creates distributed, immutable ledgers for secure transactions without requiring a central authority.",
                new[] { "blockchain", "technology", "distributed", "immutable", "ledgers", "secure", "transactions", "central", "authority" })
        };
    }

    /// <summary>
    /// 하이브리드 검색 품질 및 성능 테스트 실행
    /// </summary>
    public async Task RunQualityTestAsync()
    {
        AnsiConsole.Write(new FigletText("Hybrid Search Quality Test").Centered().Color(Color.Green1));
        AnsiConsole.WriteLine();

        var testQueries = new[]
        {
            new TestQuery("machine learning algorithms", new[] { "doc1", "doc5" }), // 키워드 + 의미적 유사성
            new TestQuery("neural network deep learning", new[] { "doc2", "doc4" }), // 복합 키워드
            new TestQuery("understanding human language", new[] { "doc3", "doc2" }), // 자연어 표현
            new TestQuery("visual image processing", new[] { "doc4", "doc2" }), // 시각 관련
            new TestQuery("data analysis techniques", new[] { "doc6", "doc7" }), // 데이터 분석
            new TestQuery("secure digital systems", new[] { "doc9", "doc10" }) // 보안 시스템
        };

        var results = new List<TestResult>();

        AnsiConsole.Status()
            .Start("하이브리드 검색 테스트 실행 중...", async ctx =>
            {
                foreach (var query in testQueries)
                {
                    ctx.Status($"테스트 중: {query.Query}");

                    var result = await ExecuteSingleTestAsync(query);
                    results.Add(result);

                    await Task.Delay(1000); // API 제한 고려
                }
            });

        // 결과 분석 및 출력
        await AnalyzeAndDisplayResultsAsync(results);
    }

    /// <summary>
    /// 단일 쿼리 테스트 실행
    /// </summary>
    private async Task<TestResult> ExecuteSingleTestAsync(TestQuery query)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. 벡터 검색 (OpenAI API로 임베딩 생성)
            var queryEmbedding = await GetEmbeddingAsync(query.Query);
            var vectorResults = await ExecuteVectorSearchAsync(queryEmbedding);

            // 2. 키워드 검색 (BM25 시뮬레이션)
            var keywordResults = ExecuteKeywordSearch(query.Query);

            // 3. 하이브리드 융합 (RRF 방법)
            var hybridResults = FuseResults(vectorResults, keywordResults);

            stopwatch.Stop();

            // 4. 품질 메트릭 계산
            var metrics = CalculateMetrics(hybridResults, query.ExpectedResults);

            return new TestResult
            {
                Query = query.Query,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                VectorResultCount = vectorResults.Count,
                KeywordResultCount = keywordResults.Count,
                HybridResultCount = hybridResults.Count,
                Precision = metrics.Precision,
                Recall = metrics.Recall,
                F1Score = metrics.F1Score,
                MRR = metrics.MRR,
                VectorResults = vectorResults,
                KeywordResults = keywordResults,
                HybridResults = hybridResults
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AnsiConsole.MarkupLine($"[red]쿼리 '{query.Query}' 실행 중 오류: {ex.Message}[/]");

            return new TestResult
            {
                Query = query.Query,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// OpenAI API를 통한 임베딩 생성
    /// </summary>
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

    /// <summary>
    /// 벡터 검색 실행 (코사인 유사도)
    /// </summary>
    private async Task<List<SearchResult>> ExecuteVectorSearchAsync(float[] queryEmbedding)
    {
        var results = new List<SearchResult>();

        foreach (var doc in _documents)
        {
            // 문서 임베딩 생성 (실제로는 미리 계산되어 있어야 함)
            var docEmbedding = await GetEmbeddingAsync(doc.Content);

            // 코사인 유사도 계산
            var similarity = CalculateCosineSimilarity(queryEmbedding, docEmbedding);

            results.Add(new SearchResult
            {
                DocumentId = doc.Id,
                Content = doc.Content,
                Score = similarity,
                Source = "Vector"
            });
        }

        return results.Where(r => r.Score > 0.7) // 임계값 설정
                     .OrderByDescending(r => r.Score)
                     .Take(5)
                     .ToList();
    }

    /// <summary>
    /// 키워드 검색 실행 (BM25 시뮬레이션)
    /// </summary>
    private List<SearchResult> ExecuteKeywordSearch(string query)
    {
        var queryTerms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<SearchResult>();

        foreach (var doc in _documents)
        {
            var score = CalculateBM25Score(queryTerms, doc);

            if (score > 0)
            {
                results.Add(new SearchResult
                {
                    DocumentId = doc.Id,
                    Content = doc.Content,
                    Score = score,
                    Source = "Keyword"
                });
            }
        }

        return results.OrderByDescending(r => r.Score)
                     .Take(5)
                     .ToList();
    }

    /// <summary>
    /// BM25 점수 계산
    /// </summary>
    private double CalculateBM25Score(string[] queryTerms, TestDocument doc)
    {
        var k1 = 1.2;
        var b = 0.75;
        var avgDocLength = _documents.Average(d => d.Terms.Length);

        var score = 0.0;
        var docLength = doc.Terms.Length;

        foreach (var term in queryTerms)
        {
            var tf = doc.Terms.Count(t => t.Equals(term, StringComparison.OrdinalIgnoreCase));
            if (tf == 0) continue;

            var df = _documents.Count(d => d.Terms.Any(t => t.Equals(term, StringComparison.OrdinalIgnoreCase)));
            var idf = Math.Log((_documents.Count - df + 0.5) / (df + 0.5));

            var termScore = idf * (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * (docLength / avgDocLength)));
            score += termScore;
        }

        return score;
    }

    /// <summary>
    /// 하이브리드 결과 융합 (RRF 방법)
    /// </summary>
    private List<SearchResult> FuseResults(List<SearchResult> vectorResults, List<SearchResult> keywordResults)
    {
        var fusedResults = new Dictionary<string, SearchResult>();
        var k = 60.0; // RRF 매개변수

        // 벡터 결과 처리
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (k + i + 1);

            fusedResults[result.DocumentId] = new SearchResult
            {
                DocumentId = result.DocumentId,
                Content = result.Content,
                Score = rrfScore * 0.7, // 벡터 가중치 70%
                Source = "Vector",
                VectorScore = result.Score,
                KeywordScore = 0,
                FusedScore = rrfScore * 0.7
            };
        }

        // 키워드 결과 처리
        for (int i = 0; i < keywordResults.Count; i++)
        {
            var result = keywordResults[i];
            var rrfScore = 1.0 / (k + i + 1);

            if (fusedResults.TryGetValue(result.DocumentId, out var existing))
            {
                // 기존 결과에 융합
                existing.FusedScore += rrfScore * 0.3; // 키워드 가중치 30%
                existing.KeywordScore = result.Score;
                existing.Source = "Hybrid";
            }
            else
            {
                // 새 결과 생성
                fusedResults[result.DocumentId] = new SearchResult
                {
                    DocumentId = result.DocumentId,
                    Content = result.Content,
                    Score = rrfScore * 0.3,
                    Source = "Keyword",
                    VectorScore = 0,
                    KeywordScore = result.Score,
                    FusedScore = rrfScore * 0.3
                };
            }
        }

        return fusedResults.Values
                          .OrderByDescending(r => r.FusedScore)
                          .Take(5)
                          .ToList();
    }

    /// <summary>
    /// 코사인 유사도 계산
    /// </summary>
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

    /// <summary>
    /// 품질 메트릭 계산
    /// </summary>
    private QualityMetrics CalculateMetrics(List<SearchResult> results, string[] expectedResults)
    {
        var retrievedIds = results.Select(r => r.DocumentId).ToHashSet();
        var expectedIds = expectedResults.ToHashSet();

        var tp = retrievedIds.Intersect(expectedIds).Count();
        var fp = retrievedIds.Except(expectedIds).Count();
        var fn = expectedIds.Except(retrievedIds).Count();

        var precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0.0;
        var recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0.0;
        var f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0.0;

        // MRR 계산
        var mrr = 0.0;
        for (int i = 0; i < results.Count; i++)
        {
            if (expectedIds.Contains(results[i].DocumentId))
            {
                mrr = 1.0 / (i + 1);
                break;
            }
        }

        return new QualityMetrics
        {
            Precision = precision,
            Recall = recall,
            F1Score = f1,
            MRR = mrr
        };
    }

    /// <summary>
    /// 결과 분석 및 출력
    /// </summary>
    private async Task AnalyzeAndDisplayResultsAsync(List<TestResult> results)
    {
        AnsiConsole.Rule("[yellow]하이브리드 검색 품질 평가 결과[/]");

        // 전체 성능 메트릭
        var avgPrecision = results.Where(r => r.Error == null).Average(r => r.Precision);
        var avgRecall = results.Where(r => r.Error == null).Average(r => r.Recall);
        var avgF1 = results.Where(r => r.Error == null).Average(r => r.F1Score);
        var avgMRR = results.Where(r => r.Error == null).Average(r => r.MRR);
        var avgTime = results.Average(r => r.ElapsedMs);

        var summaryTable = new Table();
        summaryTable.AddColumn("메트릭");
        summaryTable.AddColumn("값");
        summaryTable.AddColumn("평가");

        summaryTable.AddRow("평균 정밀도 (Precision)", $"{avgPrecision:F3}", GetQualityRating(avgPrecision));
        summaryTable.AddRow("평균 재현율 (Recall)", $"{avgRecall:F3}", GetQualityRating(avgRecall));
        summaryTable.AddRow("평균 F1 점수", $"{avgF1:F3}", GetQualityRating(avgF1));
        summaryTable.AddRow("평균 MRR", $"{avgMRR:F3}", GetQualityRating(avgMRR));
        summaryTable.AddRow("평균 응답 시간", $"{avgTime:F0}ms", GetPerformanceRating(avgTime));

        AnsiConsole.Write(summaryTable);

        // 쿼리별 세부 결과
        AnsiConsole.WriteLine();
        AnsiConsole.Rule("[cyan]쿼리별 세부 결과[/]");

        foreach (var result in results)
        {
            if (result.Error != null)
            {
                AnsiConsole.MarkupLine($"[red]❌ {result.Query}: {result.Error}[/]");
                continue;
            }

            AnsiConsole.MarkupLine($"[cyan]🔍 쿼리:[/] {result.Query}");
            AnsiConsole.MarkupLine($"   📊 정밀도: {result.Precision:F3} | 재현율: {result.Recall:F3} | F1: {result.F1Score:F3} | MRR: {result.MRR:F3}");
            AnsiConsole.MarkupLine($"   ⏱️  응답시간: {result.ElapsedMs:F0}ms");
            AnsiConsole.MarkupLine($"   📈 결과 수: 벡터({result.VectorResultCount}) + 키워드({result.KeywordResultCount}) → 하이브리드({result.HybridResultCount})");

            // 상위 3개 결과 표시
            if (result.HybridResults.Any())
            {
                AnsiConsole.MarkupLine("   🥇 상위 결과:");
                for (int i = 0; i < Math.Min(3, result.HybridResults.Count); i++)
                {
                    var r = result.HybridResults[i];
                    AnsiConsole.MarkupLine($"      {i + 1}. {r.DocumentId} (융합점수: {r.FusedScore:F3}, 소스: {r.Source})");
                }
            }

            AnsiConsole.WriteLine();
        }

        // 추천사항
        AnsiConsole.Rule("[green]추천사항[/]");

        if (avgPrecision < 0.7)
            AnsiConsole.MarkupLine("🔧 [yellow]정밀도 개선 필요:[/] 벡터 임베딩 모델 업그레이드 또는 키워드 가중치 조정");

        if (avgRecall < 0.6)
            AnsiConsole.MarkupLine("🔧 [yellow]재현율 개선 필요:[/] 검색 범위 확대 또는 유사도 임계값 낮춤");

        if (avgTime > 2000)
            AnsiConsole.MarkupLine("🔧 [yellow]성능 개선 필요:[/] 임베딩 캐싱 또는 벡터 인덱스 최적화");

        if (avgF1 > 0.8)
            AnsiConsole.MarkupLine("🎉 [green]우수한 하이브리드 검색 품질![/] 현재 설정이 최적화되어 있습니다.");
    }

    private string GetQualityRating(double score)
    {
        return score switch
        {
            >= 0.9 => "[green]우수[/]",
            >= 0.8 => "[green]좋음[/]",
            >= 0.7 => "[yellow]보통[/]",
            >= 0.6 => "[orange1]미흡[/]",
            _ => "[red]개선필요[/]"
        };
    }

    private string GetPerformanceRating(double timeMs)
    {
        return timeMs switch
        {
            <= 500 => "[green]매우빠름[/]",
            <= 1000 => "[green]빠름[/]",
            <= 2000 => "[yellow]보통[/]",
            <= 5000 => "[orange1]느림[/]",
            _ => "[red]매우느림[/]"
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

// 데이터 모델들
public record StandaloneTestDocument(string Id, string Content, string[] Terms);
public record TestQuery(string Query, string[] ExpectedResults);

public class SearchResult
{
    public string DocumentId { get; init; } = "";
    public string Content { get; init; } = "";
    public double Score { get; set; }
    public string Source { get; init; } = "";
    public double VectorScore { get; set; }
    public double KeywordScore { get; set; }
    public double FusedScore { get; set; }
}

public class TestResult
{
    public string Query { get; init; } = "";
    public double ElapsedMs { get; init; }
    public int VectorResultCount { get; init; }
    public int KeywordResultCount { get; init; }
    public int HybridResultCount { get; init; }
    public double Precision { get; init; }
    public double Recall { get; init; }
    public double F1Score { get; init; }
    public double MRR { get; init; }
    public string? Error { get; init; }
    public List<SearchResult> VectorResults { get; init; } = new();
    public List<SearchResult> KeywordResults { get; init; } = new();
    public List<SearchResult> HybridResults { get; init; } = new();
}

public record QualityMetrics(double Precision, double Recall, double F1Score, double MRR);