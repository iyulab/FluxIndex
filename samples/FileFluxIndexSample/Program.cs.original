using FileFlux;
using FluxIndex.SDK;
using FluxIndex.Extensions.FileFlux;
using FluxIndex.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;

namespace FileFluxIndexSample;

class Program
{
    static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        var app = host.Services.GetRequiredService<SampleApplication>();
        
        await app.RunAsync();
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                      .AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;

                // FileFlux 서비스 등록
                services.AddFileFlux();
                
                // OpenAI 서비스 구현 등록 (FileFlux용)
                services.AddScoped<ITextCompletionService, OpenAITextCompletionService>();
                services.AddScoped<IImageToTextService, OpenAIImageToTextService>();
                
                // FluxIndex 설정
                var connectionString = configuration.GetConnectionString("SQLite") 
                    ?? "Data Source=fluxindex.db";
                var openAiApiKey = configuration["OpenAI:ApiKey"] 
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

                // FluxIndex 클라이언트 빌드
                var fluxIndexClient = FluxIndexClient.CreateBuilder()
                    .ConfigureVectorStore(store => store.UseSQLite(connectionString))
                    .ConfigureEmbedding(embed => embed.UseOpenAI(openAiApiKey))
                    .ConfigureReranking(rerank => rerank.UseOpenAI(openAiApiKey))
                    .EnableMetadataEnrichment()
                    .EnableAdvancedReranking()
                    .Build();

                services.AddSingleton(fluxIndexClient);
                
                // FileFlux-FluxIndex 통합 서비스
                services.AddScoped<IFileFluxIntegration, FileFluxIntegration>();
                
                // 샘플 애플리케이션
                services.AddScoped<SampleApplication>();
                services.AddScoped<PerformanceTester>();
                services.AddScoped<QualityTester>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            });
}

/// <summary>
/// 메인 샘플 애플리케이션
/// </summary>
public class SampleApplication
{
    private readonly IFileFluxIntegration _integration;
    private readonly PerformanceTester _performanceTester;
    private readonly QualityTester _qualityTester;
    private readonly ILogger<SampleApplication> _logger;
    private readonly IConfiguration _configuration;

    public SampleApplication(
        IFileFluxIntegration integration,
        PerformanceTester performanceTester,
        QualityTester qualityTester,
        ILogger<SampleApplication> logger,
        IConfiguration configuration)
    {
        _integration = integration;
        _performanceTester = performanceTester;
        _qualityTester = qualityTester;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task RunAsync()
    {
        AnsiConsole.Write(
            new FigletText("FluxIndex + FileFlux")
                .LeftJustified()
                .Color(Color.Blue));

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]무엇을 테스트하시겠습니까?[/]")
                    .AddChoices(new[]
                    {
                        "1. 단일 문서 처리 (End-to-End)",
                        "2. 배치 처리 성능 테스트",
                        "3. 검색 품질 테스트",
                        "4. 재순위화 전략 비교",
                        "5. 스트리밍 처리 데모",
                        "6. 멀티모달 문서 처리",
                        "7. 전체 벤치마크 실행",
                        "8. 종료"
                    }));

            if (choice.StartsWith("8")) break;

            await ExecuteChoice(choice);
        }
    }

    private async Task ExecuteChoice(string choice)
    {
        try
        {
            switch (choice[0])
            {
                case '1':
                    await ProcessSingleDocument();
                    break;
                case '2':
                    await RunBatchProcessingTest();
                    break;
                case '3':
                    await RunQualityTest();
                    break;
                case '4':
                    await CompareRerankingStrategies();
                    break;
                case '5':
                    await DemoStreamingProcessing();
                    break;
                case '6':
                    await ProcessMultimodalDocument();
                    break;
                case '7':
                    await RunFullBenchmark();
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Press Enter to continue...[/]")
            .AllowEmpty());
    }

    private async Task ProcessSingleDocument()
    {
        var filePath = AnsiConsole.Ask<string>("문서 경로를 입력하세요: ", @"D:\data\FileFlux\test\test-pdf\oai_gpt-oss_model_card.pdf");

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]문서 처리 중...[/]");

                var stopwatch = Stopwatch.StartNew();

                // FileFlux로 청킹
                task.Description = "[yellow]FileFlux: 문서 청킹 중...[/]";
                task.Increment(20);

                var result = await _integration.ProcessAndIndexAsync(filePath, new ProcessingOptions
                {
                    ChunkingStrategy = "Auto",
                    MaxChunkSize = 512,
                    OverlapSize = 64,
                    EnableQualityScoring = true
                });

                task.Description = "[yellow]FluxIndex: 임베딩 생성 중...[/]";
                task.Increment(30);

                task.Description = "[yellow]FluxIndex: 벡터 저장 중...[/]";
                task.Increment(30);

                task.Description = "[green]완료![/]";
                task.Increment(20);

                stopwatch.Stop();

                // 결과 표시
                var table = new Table();
                table.AddColumn("메트릭");
                table.AddColumn("값");

                table.AddRow("문서 ID", result.DocumentId);
                table.AddRow("청크 수", result.ChunkCount.ToString());
                table.AddRow("처리 시간", $"{stopwatch.ElapsedMilliseconds}ms");
                table.AddRow("평균 품질 점수", $"{result.AverageQualityScore:F2}");
                table.AddRow("메타데이터 항목", result.MetadataCount.ToString());

                AnsiConsole.Write(table);
            });
    }

    private async Task RunBatchProcessingTest()
    {
        var testPath = _configuration["TestConfiguration:TestDataPath"] ?? @"D:\data\FileFlux\test";
        var files = Directory.GetFiles(testPath, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.Contains("extract"))
            .ToArray();
        
        AnsiConsole.WriteLine($"[yellow]{files.Length}개 파일 발견[/]");

        var results = await _performanceTester.TestBatchProcessing(files);

        // 성능 결과 표시
        var chart = new BarChart()
            .Width(60)
            .Label("[green]파일별 처리 시간 (ms)[/]");

        foreach (var result in results.Take(10))
        {
            chart.AddItem(Path.GetFileName(result.FileName), result.ProcessingTimeMs, Color.Aqua);
        }

        AnsiConsole.Write(chart);

        // 통계 표시
        var stats = new Table();
        stats.AddColumn("통계");
        stats.AddColumn("값");

        stats.AddRow("총 파일 수", results.Count.ToString());
        stats.AddRow("총 청크 수", results.Sum(r => r.ChunkCount).ToString());
        stats.AddRow("평균 처리 시간", $"{results.Average(r => r.ProcessingTimeMs):F2}ms");
        stats.AddRow("총 처리 시간", $"{results.Sum(r => r.ProcessingTimeMs):F2}ms");
        stats.AddRow("처리량", $"{results.Count / (results.Sum(r => r.ProcessingTimeMs) / 1000.0):F2} files/sec");

        AnsiConsole.Write(stats);
    }

    private async Task RunQualityTest()
    {
        var testQueries = new[]
        {
            "배터리 최적화 방법",
            "스마트폰 성능 향상",
            "앱 메모리 관리",
            "네트워크 설정 가이드",
            "보안 설정 권장사항"
        };

        var results = await _qualityTester.TestSearchQuality(testQueries);

        // 품질 메트릭 표시
        var table = new Table();
        table.AddColumn("쿼리");
        table.AddColumn("재현율@10");
        table.AddColumn("MRR");
        table.AddColumn("응답시간(ms)");

        foreach (var result in results)
        {
            table.AddRow(
                result.Query,
                $"{result.RecallAt10:P0}",
                $"{result.MRR:F3}",
                $"{result.ResponseTimeMs:F0}"
            );
        }

        AnsiConsole.Write(table);

        // 평균 통계
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]평균 재현율@10:[/] {results.Average(r => r.RecallAt10):P0}");
        AnsiConsole.MarkupLine($"[green]평균 MRR:[/] {results.Average(r => r.MRR):F3}");
        AnsiConsole.MarkupLine($"[green]평균 응답시간:[/] {results.Average(r => r.ResponseTimeMs):F0}ms");
    }

    private async Task CompareRerankingStrategies()
    {
        var query = AnsiConsole.Ask<string>("테스트 쿼리: ", "배터리 수명 연장 방법");

        var strategies = new[]
        {
            RerankingStrategy.Semantic,
            RerankingStrategy.Quality,
            RerankingStrategy.Contextual,
            RerankingStrategy.Hybrid,
            RerankingStrategy.LLM,
            RerankingStrategy.Adaptive
        };

        var results = new List<(string Strategy, double Score, long TimeMs)>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .StartAsync("재순위화 전략 비교 중...", async ctx =>
            {
                foreach (var strategy in strategies)
                {
                    ctx.Status($"[yellow]{strategy} 테스트 중...[/]");
                    
                    var stopwatch = Stopwatch.StartNew();
                    var searchResults = await _integration.SearchWithStrategyAsync(query, strategy);
                    stopwatch.Stop();

                    var avgScore = searchResults.Take(10).Average(r => r.Score);
                    results.Add((strategy.ToString(), avgScore, stopwatch.ElapsedMilliseconds));
                }
            });

        // 결과 차트
        var chart = new BarChart()
            .Width(60)
            .Label("[green]재순위화 전략별 평균 점수[/]");

        foreach (var (strategy, score, _) in results.OrderByDescending(r => r.Score))
        {
            chart.AddItem(strategy, score * 100, Color.Blue);
        }

        AnsiConsole.Write(chart);

        // 성능 비교
        var perfTable = new Table();
        perfTable.AddColumn("전략");
        perfTable.AddColumn("평균 점수");
        perfTable.AddColumn("처리 시간(ms)");

        foreach (var (strategy, score, timeMs) in results.OrderByDescending(r => r.Score))
        {
            perfTable.AddRow(strategy, $"{score:F3}", timeMs.ToString());
        }

        AnsiConsole.Write(perfTable);
    }

    private async Task DemoStreamingProcessing()
    {
        var filePath = AnsiConsole.Ask<string>("문서 경로: ", @"D:\data\FileFlux\test\test-pdf\oai_gpt-oss_model_card.pdf");

        AnsiConsole.WriteLine("[yellow]스트리밍 처리 시작...[/]");
        
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("청크 인덱스")
            .AddColumn("내용 미리보기")
            .AddColumn("품질 점수")
            .AddColumn("토큰 수");

        await AnsiConsole.Live(table)
            .StartAsync(async ctx =>
            {
                await foreach (var progress in _integration.ProcessWithProgressAsync(filePath))
                {
                    if (progress.CurrentChunk != null)
                    {
                        var preview = progress.CurrentChunk.Content.Length > 50
                            ? progress.CurrentChunk.Content[..50] + "..."
                            : progress.CurrentChunk.Content;

                        table.AddRow(
                            progress.ChunkIndex.ToString(),
                            preview,
                            $"{progress.QualityScore:F2}",
                            progress.TokenCount.ToString()
                        );

                        ctx.Refresh();
                        await Task.Delay(100); // 시각적 효과를 위한 지연
                    }
                }
            });

        AnsiConsole.MarkupLine("[green]스트리밍 처리 완료![/]");
    }

    private async Task ProcessMultimodalDocument()
    {
        var filePath = AnsiConsole.Ask<string>("멀티모달 문서 경로: ", @"D:\data\FileFlux\test\test-pptx\samplepptx.pptx");

        var result = await _integration.ProcessMultimodalDocumentAsync(filePath);

        // 결과 표시
        var panel = new Panel(new Markup(
            $"[green]문서 ID:[/] {result.DocumentId}\n" +
            $"[green]총 청크:[/] {result.TotalChunks}\n" +
            $"[green]텍스트 청크:[/] {result.TextChunks}\n" +
            $"[green]이미지 변환 청크:[/] {result.ImageChunks}\n" +
            $"[green]평균 품질:[/] {result.AverageQuality:F2}"))
        {
            Header = new PanelHeader("멀티모달 처리 결과"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
    }

    private async Task RunFullBenchmark()
    {
        AnsiConsole.WriteLine("[yellow]전체 벤치마크 실행 중... (몇 분 소요)[/]");

        var benchmark = new FullBenchmark(_integration);
        await benchmark.RunAsync();

        AnsiConsole.MarkupLine("[green]벤치마크 완료! 결과는 BenchmarkDotNet 리포트를 확인하세요.[/]");
    }
}