using FluxIndex.SDK;
using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace RealQualityTest;

// 임시 FusionMethod enum 정의
public enum FusionMethod
{
    RRF,
    WeightedSum,
    Product,
    Maximum,
    HarmonicMean
}

/// <summary>
/// 하이브리드 검색 품질 테스트 - 실제 OpenAI API 사용
/// </summary>
public class HybridSearchQualityTest
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<HybridSearchQualityTest> _logger;

    public HybridSearchQualityTest(IConfiguration configuration, ILogger<HybridSearchQualityTest> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 하이브리드 검색 품질 테스트 실행
    /// </summary>
    public async Task RunHybridSearchQualityTestAsync()
    {
        AnsiConsole.Write(new FigletText("Hybrid Search Quality Test").Centered().Color(Color.Cyan1));
        AnsiConsole.WriteLine();

        try
        {
            // 1. FluxIndex 클라이언트 구성
            var client = await SetupFluxIndexClientAsync();

            // 2. 테스트 문서 인덱싱
            await IndexTestDocumentsAsync(client);

            // 3. 하이브리드 검색 품질 테스트
            await ExecuteHybridSearchTestsAsync(client);

            // 4. 융합 방법 비교 테스트
            await CompareFusionMethodsAsync(client);

            // 5. 성능 벤치마크
            await RunPerformanceBenchmarkAsync(client);

            AnsiConsole.Write(new Panel("[green]✅ 하이브리드 검색 품질 테스트 완료![/]")
                .BorderColor(Color.Green).Header("테스트 완료"));
        }
        catch (Exception ex)
        {
            AnsiConsole.Write(new Panel($"[red]❌ 테스트 실패: {ex.Message}[/]")
                .BorderColor(Color.Red).Header("오류"));
            _logger.LogError(ex, "하이브리드 검색 품질 테스트 실패");
            throw;
        }
    }

    private async Task<FluxIndexContext> SetupFluxIndexClientAsync()
    {
        AnsiConsole.Write(new Panel("FluxIndex 클라이언트 설정 중...")
            .BorderColor(Color.Blue).Header("초기화"));

        var apiKey = _configuration["OPENAI_API_KEY"];
        var model = _configuration["OPENAI_MODEL"] ?? "gpt-5-nano";
        var embeddingModel = _configuration["OPENAI_EMBEDDING_MODEL"] ?? "text-embedding-3-small";

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY가 설정되지 않았습니다.");
        }

        var client = new FluxIndexClientBuilder()
            .UseOpenAI(apiKey, embeddingModel)
            .UseSQLiteInMemory()
            .UseMemoryCache()
            .WithChunking("Auto", 512, 64)
            .WithSearchOptions(50, 0.1f) // 더 많은 결과, 낮은 최소 점수
            .WithLogging(builder => builder.AddConsole())
            .Build();

        _logger.LogInformation("FluxIndex 클라이언트 설정 완료");
        AnsiConsole.WriteLine("✅ FluxIndex 클라이언트 준비 완료");

        return client;
    }

    private async Task IndexTestDocumentsAsync(FluxIndexContext client)
    {
        AnsiConsole.Write(new Panel("테스트 문서 인덱싱 중...")
            .BorderColor(Color.Yellow).Header("인덱싱"));

        var testDocuments = new List<(string id, string title, string content)>
        {
            ("doc1", "AI와 머신러닝 기초",
             "인공지능(AI)은 컴퓨터가 인간처럼 학습하고 추론할 수 있게 하는 기술입니다. " +
             "머신러닝은 AI의 하위 분야로, 데이터로부터 패턴을 학습하여 예측을 수행합니다. " +
             "딥러닝은 신경망을 사용하는 머신러닝의 한 방법입니다."),

            ("doc2", "자연어 처리 기술",
             "자연어 처리(NLP)는 컴퓨터가 인간의 언어를 이해하고 처리하는 기술입니다. " +
             "토큰화, 형태소 분석, 구문 분석 등의 기본 기술부터 시작해서 " +
             "최근에는 Transformer 모델과 BERT, GPT 같은 대화형 AI까지 발전했습니다."),

            ("doc3", "벡터 데이터베이스와 임베딩",
             "벡터 데이터베이스는 고차원 벡터 데이터를 효율적으로 저장하고 검색하는 시스템입니다. " +
             "임베딩은 텍스트나 이미지 같은 데이터를 수치 벡터로 변환하는 과정입니다. " +
             "코사인 유사도나 유클리드 거리를 사용해 유사한 벡터를 찾을 수 있습니다."),

            ("doc4", "검색 알고리즘과 정보 검색",
             "정보 검색에서는 TF-IDF, BM25 같은 키워드 기반 알고리즘이 전통적으로 사용되었습니다. " +
             "최근에는 밀집 벡터 검색과 희소 검색을 결합한 하이브리드 검색이 주목받고 있습니다. " +
             "Reciprocal Rank Fusion(RRF) 같은 방법으로 여러 검색 결과를 효과적으로 결합할 수 있습니다."),

            ("doc5", "RAG 시스템과 생성형 AI",
             "Retrieval-Augmented Generation(RAG)은 검색과 생성을 결합한 AI 시스템입니다. " +
             "외부 지식 베이스에서 관련 정보를 검색한 후, 이를 바탕으로 정확한 답변을 생성합니다. " +
             "GPT, Claude 같은 대형 언어 모델과 벡터 검색을 조합하여 구현됩니다."),

            ("doc6", "데이터베이스 기술",
             "관계형 데이터베이스는 SQL을 사용해 구조화된 데이터를 관리합니다. " +
             "NoSQL 데이터베이스는 문서, 그래프, 키-값 등 다양한 형태의 데이터를 다룹니다. " +
             "PostgreSQL, MongoDB, Redis는 각각 다른 용도로 널리 사용되는 데이터베이스입니다.")
        };

        foreach (var (id, title, content) in testDocuments)
        {
            var document = Document.Create(id);
            document.AddMetadata("title", title);
            document.AddChunk(new DocumentChunk(content, 0));

            await client.Indexer.IndexDocumentAsync(document);
            AnsiConsole.WriteLine($"✅ 문서 인덱싱 완료: {title}");
        }

        _logger.LogInformation("테스트 문서 {Count}개 인덱싱 완료", testDocuments.Count);
    }

    private async Task ExecuteHybridSearchTestsAsync(FluxIndexContext client)
    {
        AnsiConsole.Write(new Panel("하이브리드 검색 테스트 실행 중...")
            .BorderColor(Color.Green).Header("하이브리드 검색"));

        var testQueries = new List<(string query, string expectedDoc, string description)>
        {
            ("머신러닝과 딥러닝", "doc1", "일반적인 자연어 쿼리"),
            ("NLP 자연어 처리", "doc2", "약어와 전체 용어 조합"),
            ("벡터 검색 코사인", "doc3", "기술 용어 조합"),
            ("BM25 알고리즘", "doc4", "특정 알고리즘명"),
            ("RAG 시스템", "doc5", "약어 검색"),
            ("PostgreSQL 데이터베이스", "doc6", "제품명 검색")
        };

        var results = new List<HybridSearchTestResult>();

        foreach (var (query, expectedDoc, description) in testQueries)
        {
            AnsiConsole.WriteLine($"🔍 검색 중: {query}");

            var stopwatch = Stopwatch.StartNew();

            // 기본 검색 (자동 전략)
            var searchResults = await client.Retriever.SearchAsync(query);

            stopwatch.Stop();

            var found = searchResults.Any(r => r.DocumentChunk.DocumentId == expectedDoc);
            var rank = found ? searchResults.Select((r, i) => new { Result = r, Index = i })
                                          .FirstOrDefault(x => x.Result.DocumentChunk.DocumentId == expectedDoc)?.Index + 1 ?? -1 : -1;

            var testResult = new HybridSearchTestResult
            {
                Query = query,
                Description = description,
                ExpectedDocumentId = expectedDoc,
                Found = found,
                Rank = rank,
                SearchTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                ResultCount = searchResults.Count,
                TopScore = searchResults.FirstOrDefault()?.Score ?? 0
            };

            results.Add(testResult);

            var status = found ? $"[green]✅ 찾음 (순위: {rank})[/]" : "[red]❌ 못찾음[/]";
            AnsiConsole.WriteLine($"   {status} - {testResult.SearchTimeMs:F1}ms, {testResult.ResultCount}개 결과");
        }

        // 결과 요약 출력
        DisplayHybridSearchSummary(results);
    }

    private async Task CompareFusionMethodsAsync(FluxIndexContext client)
    {
        AnsiConsole.Write(new Panel("융합 방법 비교 테스트...")
            .BorderColor(Color.Purple).Header("융합 방법 비교"));

        // 하이브리드 검색 서비스에 직접 접근 (테스트용)
        var serviceProvider = ((FluxIndexClient)client).ServiceProvider;
        var hybridService = serviceProvider?.GetService<IHybridSearchService>();

        if (hybridService == null)
        {
            AnsiConsole.WriteLine("[yellow]⚠️ 하이브리드 검색 서비스를 사용할 수 없습니다.[/]");
            return;
        }

        var testQuery = "벡터 검색과 키워드 검색";
        var fusionMethods = new[]
        {
            FusionMethod.RRF,
            FusionMethod.WeightedSum,
            FusionMethod.Product,
            FusionMethod.Maximum,
            FusionMethod.HarmonicMean
        };

        var comparisonResults = new List<FusionComparisonResult>();

        foreach (var method in fusionMethods)
        {
            var options = new HybridSearchOptions
            {
                FusionMethod = method,
                MaxResults = 10,
                VectorWeight = 0.7,
                SparseWeight = 0.3
            };

            var stopwatch = Stopwatch.StartNew();
            var results = await hybridService.SearchAsync(testQuery, options);
            stopwatch.Stop();

            comparisonResults.Add(new FusionComparisonResult
            {
                Method = method,
                ResultCount = results.Count,
                SearchTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                TopScore = results.FirstOrDefault()?.FusedScore ?? 0,
                AverageScore = results.Any() ? results.Average(r => r.FusedScore) : 0
            });

            AnsiConsole.WriteLine($"✅ {method}: {results.Count}개 결과, {stopwatch.ElapsedMilliseconds}ms");
        }

        DisplayFusionComparisonSummary(comparisonResults);
    }

    private async Task RunPerformanceBenchmarkAsync(FluxIndexContext client)
    {
        AnsiConsole.Write(new Panel("성능 벤치마크 실행 중...")
            .BorderColor(Color.Orange3).Header("성능 테스트"));

        var benchmarkQueries = new[]
        {
            "인공지능 머신러닝",
            "자연어 처리 NLP",
            "벡터 데이터베이스",
            "검색 알고리즘 BM25",
            "RAG 생성형 AI"
        };

        var searchTimes = new List<double>();
        const int iterations = 3;

        foreach (var query in benchmarkQueries)
        {
            var queryTimes = new List<double>();

            for (int i = 0; i < iterations; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                await client.Retriever.SearchAsync(query);
                stopwatch.Stop();

                queryTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            var avgTime = queryTimes.Average();
            searchTimes.Add(avgTime);

            AnsiConsole.WriteLine($"📊 {query}: 평균 {avgTime:F1}ms");
        }

        var overallAverage = searchTimes.Average();
        var minTime = searchTimes.Min();
        var maxTime = searchTimes.Max();

        var performanceTable = new Table();
        performanceTable.AddColumn("메트릭");
        performanceTable.AddColumn("값");

        performanceTable.AddRow("평균 검색 시간", $"{overallAverage:F1} ms");
        performanceTable.AddRow("최소 검색 시간", $"{minTime:F1} ms");
        performanceTable.AddRow("최대 검색 시간", $"{maxTime:F1} ms");
        performanceTable.AddRow("처리된 쿼리 수", $"{benchmarkQueries.Length * iterations}");

        AnsiConsole.Write(performanceTable);

        _logger.LogInformation("성능 벤치마크 완료 - 평균: {Average:F1}ms", overallAverage);
    }

    private void DisplayHybridSearchSummary(List<HybridSearchTestResult> results)
    {
        var successCount = results.Count(r => r.Found);
        var successRate = (double)successCount / results.Count * 100;
        var avgSearchTime = results.Average(r => r.SearchTimeMs);
        var avgRank = results.Where(r => r.Found).Select(r => r.Rank).DefaultIfEmpty(0).Average();

        var summaryTable = new Table();
        summaryTable.AddColumn("메트릭");
        summaryTable.AddColumn("값");

        summaryTable.AddRow("성공률", $"{successRate:F1}% ({successCount}/{results.Count})");
        summaryTable.AddRow("평균 검색 시간", $"{avgSearchTime:F1} ms");
        summaryTable.AddRow("평균 순위", $"{avgRank:F1}");
        summaryTable.AddRow("총 테스트 수", $"{results.Count}");

        AnsiConsole.Write(new Panel(summaryTable)
            .BorderColor(Color.Green)
            .Header("하이브리드 검색 요약"));

        // 상세 결과 테이블
        var detailTable = new Table();
        detailTable.AddColumn("쿼리");
        detailTable.AddColumn("설명");
        detailTable.AddColumn("결과");
        detailTable.AddColumn("순위");
        detailTable.AddColumn("시간(ms)");

        foreach (var result in results)
        {
            var status = result.Found ? "[green]✅ 성공[/]" : "[red]❌ 실패[/]";
            var rank = result.Found ? result.Rank.ToString() : "-";

            detailTable.AddRow(
                result.Query,
                result.Description,
                status,
                rank,
                $"{result.SearchTimeMs:F1}"
            );
        }

        AnsiConsole.Write(detailTable);
    }

    private void DisplayFusionComparisonSummary(List<FusionComparisonResult> results)
    {
        var comparisonTable = new Table();
        comparisonTable.AddColumn("융합 방법");
        comparisonTable.AddColumn("결과 수");
        comparisonTable.AddColumn("검색 시간(ms)");
        comparisonTable.AddColumn("최고 점수");
        comparisonTable.AddColumn("평균 점수");

        foreach (var result in results)
        {
            comparisonTable.AddRow(
                result.Method.ToString(),
                result.ResultCount.ToString(),
                $"{result.SearchTimeMs:F1}",
                $"{result.TopScore:F3}",
                $"{result.AverageScore:F3}"
            );
        }

        AnsiConsole.Write(new Panel(comparisonTable)
            .BorderColor(Color.Purple)
            .Header("융합 방법 비교 결과"));
    }
}

#region Data Models

internal class HybridSearchTestResult
{
    public string Query { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExpectedDocumentId { get; set; } = string.Empty;
    public bool Found { get; set; }
    public int Rank { get; set; }
    public double SearchTimeMs { get; set; }
    public int ResultCount { get; set; }
    public double TopScore { get; set; }
}

internal class FusionComparisonResult
{
    public FusionMethod Method { get; set; }
    public int ResultCount { get; set; }
    public double SearchTimeMs { get; set; }
    public double TopScore { get; set; }
    public double AverageScore { get; set; }
}

#endregion